namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using System.Text.RegularExpressions;

public interface IAntigravityCdpClient
{
    int? ResolveActivePort();
    Task<string?> CaptureActiveChatPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    Task<bool> RestoreActiveChatAsync(string chatPath, CancellationToken cancellationToken = default) => Task.FromResult(false);
    Task<bool> AutoDismissSecurityNoticeAsync(CancellationToken cancellationToken = default);
    void TriggerAutoDismissInBackground();
    Task<bool> AutoContinueChatAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    void TriggerPostRestartActionsInBackground(string? savedChatPath = null, bool autoContinue = true) { }
}

public sealed class AntigravityCdpClient : IAntigravityCdpClient
{
    private static readonly Regex ChatPathRegex = new(@"^/c/[a-zA-Z0-9_\-]+(\?.*)?$", RegexOptions.Compiled);

    public const string AutoContinueScript = @"(() => {
        // 1. If there's an explicit Continue / Retry / Resume button, click it
        const buttons = Array.from(document.querySelectorAll('button'));
        const retryBtn = buttons.find(b => {
            const t = (b.innerText || b.textContent || b.getAttribute('aria-label') || '').toLowerCase();
            return (t.includes('continue') || t.includes('retry') || t.includes('resume')) && !b.disabled;
        });
        if (retryBtn) {
            retryBtn.click();
            return true;
        }

        // 2. Find message input box
        const input = document.querySelector('[aria-label=""Message input""]') ||
                      document.querySelector('[contenteditable=""true""]');
        if (!input) return false;

        // Check if agent is currently actively running (Cancel / Stop button present)
        const isRunning = buttons.some(b => {
            const aria = (b.getAttribute('aria-label') || '').toLowerCase();
            return aria.includes('cancel') || aria.includes('stop execution');
        });
        if (isRunning) return false;

        // 3. Focus, type 'Continue', and submit
        input.focus();
        document.execCommand('insertText', false, 'Continue');

        // 4. Submit via Enter keydown or clicking send button
        const enterEvent = new KeyboardEvent('keydown', {
            key: 'Enter',
            code: 'Enter',
            keyCode: 13,
            which: 13,
            bubbles: true,
            cancelable: true
        });
        input.dispatchEvent(enterEvent);

        const sendBtn = Array.from(input.parentElement?.parentElement?.querySelectorAll('button') || [])
            .find(b => !b.disabled && (b.getAttribute('aria-label') || '').toLowerCase().includes('send'));
        if (sendBtn) {
            sendBtn.click();
        }

        return true;
    })()";

    public const string DismissNoticeScript = @"(() => {
        const headings = Array.from(document.querySelectorAll('h1, h2, h3, div'));
        const notice = headings.find(h => h.textContent && h.textContent.trim().startsWith('Security Notice & Data Use'));
        if (!notice) {
            return false;
        }

        const container = notice.closest('[role=""dialog""]') || notice.closest('.max-w-xl') || document.body;

        const label = container.querySelector('label');
        const cb = container.querySelector('input[type=""checkbox""]') || container.querySelector('[role=""checkbox""]');
        if (cb && !cb.checked && cb.getAttribute('aria-checked') !== 'true') {
            if (label) {
                label.click();
            } else {
                cb.click();
            }
        }

        const buttons = Array.from(container.querySelectorAll('button'));
        const btn = buttons.find(b => b.textContent && b.textContent.includes('Accept and continue'));
        if (btn && !btn.disabled && btn.getAttribute('aria-disabled') !== 'true') {
            btn.click();
            return true;
        }

        return false;
    })()";

    private readonly HttpClient _http;
    private readonly string _portFilePath;

    public AntigravityCdpClient(HttpClient? http = null, string? portFilePath = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        _portFilePath = portFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Antigravity",
            "DevToolsActivePort");
    }

    public int? ResolveActivePort() => ParsePortFromFile(_portFilePath);

    public static int? ParsePortFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine()?.Trim();
            return int.TryParse(firstLine, out var port) && port is > 0 and <= 65535 ? port : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> CaptureActiveChatPathAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var port = ResolveActivePort();
            if (port is null) return null;

            var response = await _http.GetStringAsync($"http://127.0.0.1:{port}/json", cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(response);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "page" &&
                    element.TryGetProperty("url", out var urlProp) &&
                    urlProp.GetString() is { } url)
                {
                    var idx = url.IndexOf("/c/", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        var path = url.Substring(idx);
                        if (ChatPathRegex.IsMatch(path))
                        {
                            return path;
                        }
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static string BuildRestoreScript(string chatPath)
    {
        var escapedPath = (chatPath ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("'", "\\'");

        return $@"(() => {{
    const p = window.location.pathname;
    if (p === '/' || !p.startsWith('/c/')) {{
        window.location.assign('{escapedPath}');
        return true;
    }}
    return false;
}})()";
    }

    public async Task<bool> RestoreActiveChatAsync(string chatPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chatPath) || !ChatPathRegex.IsMatch(chatPath))
        {
            return false;
        }

        try
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(6);
            int? port = null;
            var script = BuildRestoreScript(chatPath);

            while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                port ??= ResolveActivePort();
                if (port is not null)
                {
                    try
                    {
                        var response = await _http.GetStringAsync($"http://127.0.0.1:{port}/json", cancellationToken).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(response);
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            if (element.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page" &&
                                element.TryGetProperty("webSocketDebuggerUrl", out var wsProp) && wsProp.GetString() is { } wsUrl)
                            {
                                var restored = await TryEvaluateScriptAsync(wsUrl, script, cancellationToken).ConfigureAwait(false);
                                if (restored) return true;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AutoDismissSecurityNoticeAsync(CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(12);
        int? port = null;

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            port ??= ResolveActivePort();
            if (port is not null)
            {
                try
                {
                    var response = await _http.GetStringAsync($"http://127.0.0.1:{port}/json", cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(response);
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (element.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page" &&
                            element.TryGetProperty("webSocketDebuggerUrl", out var wsProp) && wsProp.GetString() is { } wsUrl)
                        {
                            var dismissed = await TryEvaluateScriptAsync(wsUrl, DismissNoticeScript, cancellationToken).ConfigureAwait(false);
                            if (dismissed) return true;
                        }
                    }
                }
                catch
                {
                }
            }

            try
            {
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return false;
    }

    public async Task<bool> AutoContinueChatAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var port = ResolveActivePort();
            if (port is null) return false;

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var response = await _http.GetStringAsync($"http://127.0.0.1:{port}/json", cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(response);
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (element.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page" &&
                            element.TryGetProperty("webSocketDebuggerUrl", out var wsProp) && wsProp.GetString() is { } wsUrl)
                        {
                            var continued = await TryEvaluateScriptAsync(wsUrl, AutoContinueScript, cancellationToken).ConfigureAwait(false);
                            if (continued) return true;
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public void TriggerAutoDismissInBackground() => TriggerPostRestartActionsInBackground(null, autoContinue: false);

    public void TriggerPostRestartActionsInBackground(string? savedChatPath = null, bool autoContinue = true)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                if (!string.IsNullOrWhiteSpace(savedChatPath))
                {
                    await RestoreActiveChatAsync(savedChatPath, cts.Token).ConfigureAwait(false);
                }

                await Task.Delay(500, cts.Token).ConfigureAwait(false);
                await AutoDismissSecurityNoticeAsync(cts.Token).ConfigureAwait(false);

                if (autoContinue)
                {
                    await AutoContinueChatAsync(cts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
            }
        });
    }

    private static async Task<bool> TryEvaluateScriptAsync(string wsUrl, string script, CancellationToken cancellationToken)
    {
        try
        {
            using var ws = new ClientWebSocket();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(2));

            await ws.ConnectAsync(new Uri(wsUrl), connectCts.Token).ConfigureAwait(false);

            var req = JsonSerializer.Serialize(new
            {
                id = 1,
                method = "Runtime.evaluate",
                @params = new
                {
                    expression = script,
                    returnByValue = true
                }
            });

            var bytes = Encoding.UTF8.GetBytes(req);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

            var buffer = new byte[4096];
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.Count > 0)
            {
                using var resDoc = JsonDocument.Parse(new ReadOnlyMemory<byte>(buffer, 0, result.Count));
                if (resDoc.RootElement.TryGetProperty("result", out var r) &&
                    r.TryGetProperty("result", out var inner) &&
                    inner.TryGetProperty("value", out var val) &&
                    val.ValueKind == JsonValueKind.True)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}

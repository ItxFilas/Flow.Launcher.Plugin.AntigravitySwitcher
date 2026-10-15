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
    Task<string?> CaptureOriginAsync(CancellationToken token = default) => Task.FromResult<string?>(null);
    Task<bool> RecoverChatAsync(string? previousOrigin, string? chatPath, CancellationToken token = default) => Task.FromResult(true);
}

public sealed class AntigravityCdpClient : IAntigravityCdpClient
{
    private static readonly string DiagnosticPath = Path.Combine(Path.GetTempPath(), "AntigravitySwitcher.log");
    internal static void Log(string message)
    {
        try { File.AppendAllText(DiagnosticPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); } catch { }
    }

    public async Task<string?> CaptureOriginAsync(CancellationToken token = default)
    {
        try
        {
            var port = ResolveActivePort();
            if (port is null) return null;
            using var doc = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{port}/json", token).ConfigureAwait(false));
            foreach (var page in doc.RootElement.EnumerateArray())
                if (page.GetProperty("type").GetString() == "page" && Uri.TryCreate(page.GetProperty("url").GetString(), UriKind.Absolute, out var url))
                    return url.GetLeftPart(UriPartial.Authority);
        }
        catch { }
        return null;
    }

    // One readiness loop: no fixed notice delay, and no navigation on the dying origin.
    public async Task<bool> RecoverChatAsync(string? previousOrigin, string? chatPath, CancellationToken token = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        var navigated = false;
        try
        {
            while (!budget.IsCancellationRequested)
            {
                try
                {
                    var port = ResolveActivePort();
                    if (port is not null)
                    {
                        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{port}/json", budget.Token).ConfigureAwait(false));
                        foreach (var page in doc.RootElement.EnumerateArray())
                        {
                            if (page.GetProperty("type").GetString() != "page" ||
                                !Uri.TryCreate(page.GetProperty("url").GetString(), UriKind.Absolute, out var url) ||
                                url.Scheme != "https" || !url.IsLoopback ||
                                url.GetLeftPart(UriPartial.Authority) == previousOrigin) continue;
                            var ws = page.GetProperty("webSocketDebuggerUrl").GetString();
                            if (ws is null) continue;
                            // A loaded application document is required before restoring the route.
                            if (!await TryEvaluateScriptAsync(ws, "document.readyState === 'complete' && document.body && document.body.children.length > 0", budget.Token).ConfigureAwait(false)) continue;
                            await TryEvaluateScriptAsync(ws, DismissNoticeScript, budget.Token).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(chatPath) && ChatPathRegex.IsMatch(chatPath) && url.PathAndQuery != chatPath)
                            {
                                if (!navigated)
                                {
                                    navigated = await TryEvaluateScriptAsync(ws, BuildRestoreScript(chatPath), budget.Token).ConfigureAwait(false);
                                    Log("Chat navigation requested");
                                }
                                continue;
                            }
                            if (await TryEvaluateScriptAsync(ws, "(() => { const e = document.querySelector('[aria-label=\"Message input\"]'); return !!e && !!e.getClientRects().length; })()", budget.Token).ConfigureAwait(false))
                            {
                                Log("Chat composer ready");
                                return true;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (budget.IsCancellationRequested) { break; }
                catch { }
                await Task.Delay(100, budget.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        Log("Chat readiness timed out");
        return false;
    }
    private static readonly Regex ChatPathRegex = new(@"^/c/[a-zA-Z0-9_\-]+(\?.*)?$", RegexOptions.Compiled);

    public const string AutoContinueScript = @"(() => {
        const input = document.querySelector('[aria-label=""Message input""]');
        if (!input || !input.getClientRects().length) return false;
        if ((input.innerText || input.value || '').trim()) return false;
        const running = Array.from(document.querySelectorAll('button')).some(b =>
            /stop|cancel/i.test(b.getAttribute('aria-label') || '') && !b.disabled);
        if (running) return false;
        input.focus();
        return document.activeElement === input;
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
    internal Func<string, string, CancellationToken, Task<bool>>? ScriptEvaluator { get; set; }

    public AntigravityCdpClient(HttpClient? http = null, string? portFilePath = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
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
    if (window.location.pathname + window.location.search !== '{escapedPath}') {{
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
                            var continued = await TrySendContinueAsync(wsUrl, cancellationToken).ConfigureAwait(false);
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
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(16));
                if (await RecoverChatAsync(null, savedChatPath, cts.Token).ConfigureAwait(false) && autoContinue)
                {
                    var sent = await AutoContinueChatAsync(cts.Token).ConfigureAwait(false);
                    Log(sent ? "Continue submitted" : "Continue skipped or submission unconfirmed");
                }
            }
            catch
            {
            }
        });
    }

    private static async Task<bool> TrySendContinueAsync(string wsUrl, CancellationToken token)
    {
        using var ws = new ClientWebSocket();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await ws.ConnectAsync(new Uri(wsUrl), timeout.Token).ConfigureAwait(false);
        var session = new CdpSession(ws, timeout.Token);
        if (!await session.Evaluate(AutoContinueScript)) return false;
        await session.Command("Input.insertText", new { text = "Continue" });
        if (!await session.Evaluate("(() => { const e = document.activeElement; return (e.innerText || e.value || '').trim() === 'Continue'; })()")) return false;
        try
        {
            await session.Command("Input.dispatchKeyEvent", new { type = "keyDown", key = "Enter", code = "Enter", windowsVirtualKeyCode = 13 });
            await session.Command("Input.dispatchKeyEvent", new { type = "keyUp", key = "Enter", code = "Enter", windowsVirtualKeyCode = 13 });
            // Never retry after Enter: a slow acknowledgement must not duplicate the message.
            var confirmed = await session.Evaluate("(() => { const e = document.querySelector('[aria-label=\"Message input\"]'); return !!e && !(e.innerText || e.value || '').trim(); })()");
            Log(confirmed ? "Continue composer cleared" : "Enter sent; acknowledgement pending (no retry)");
        }
        catch { Log("Enter attempted; CDP disconnected before acknowledgement (no retry)"); }
        return true;
    }

    private async Task<bool> TryEvaluateScriptAsync(string wsUrl, string script, CancellationToken cancellationToken)
    {
        if (ScriptEvaluator is not null) return await ScriptEvaluator(wsUrl, script, cancellationToken).ConfigureAwait(false);
        try
        {
            using var ws = new ClientWebSocket();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(2));

            await ws.ConnectAsync(new Uri(wsUrl), connectCts.Token).ConfigureAwait(false);

            var session = new CdpSession(ws, connectCts.Token);
            return await session.Evaluate(script).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }
}

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public interface IAntigravityCdpClient
{
    int? ResolveActivePort();
    Task<bool> AutoDismissSecurityNoticeAsync(CancellationToken cancellationToken = default);
    void TriggerAutoDismissInBackground();
}

public sealed class AntigravityCdpClient : IAntigravityCdpClient
{
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
                            var dismissed = await TryEvaluateDismissAsync(wsUrl, cancellationToken).ConfigureAwait(false);
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

    public void TriggerAutoDismissInBackground()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await AutoDismissSecurityNoticeAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
            }
        });
    }

    private static async Task<bool> TryEvaluateDismissAsync(string wsUrl, CancellationToken cancellationToken)
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
                    expression = DismissNoticeScript,
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

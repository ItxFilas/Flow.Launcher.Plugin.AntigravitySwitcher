using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

// Commands are sequential within a session; events and fragmented replies are handled here.
internal sealed class CdpSession(WebSocket socket, CancellationToken token)
{
    private readonly WebSocket _socket = socket;
    private readonly CancellationToken _token = token;
    private int _id;

    public async Task<JsonElement> Command(string method, object parameters)
    {
        var requestId = ++_id;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = requestId, method, @params = parameters }));
        await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _token).ConfigureAwait(false);
        var buffer = new byte[4096];
        while (true)
        {
            using var data = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) throw new IOException("CDP disconnected");
                data.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            using var doc = JsonDocument.Parse(data.ToArray());
            if (doc.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt32() == requestId)
            {
                if (doc.RootElement.TryGetProperty("error", out _)) throw new IOException("CDP command failed");
                return doc.RootElement.Clone();
            }
        }
    }

    public async Task<bool> Evaluate(string script)
    {
        var result = await Command("Runtime.evaluate", new { expression = script, returnByValue = true });
        return result.GetProperty("result").GetProperty("result").TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.True;
    }
}

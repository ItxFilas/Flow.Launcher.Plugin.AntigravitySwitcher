using System.Net.WebSockets;
using System.Text;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class CdpSessionTests
{
    [Fact]
    public async Task Evaluate_IgnoresEventsAndReassemblesFragmentedReply()
    {
        using var socket = new ReplySocket(
            ("{\"method\":\"Runtime.consoleAPICalled\"}", true),
            ("{\"id\":1,\"result\":{", false),
            ("\"result\":{\"value\":true}}}", true));
        Assert.True(await new CdpSession(socket, default).Evaluate("true"));
    }

    [Fact]
    public async Task Evaluate_ReportsProtocolError()
    {
        using var socket = new ReplySocket(("{\"id\":1,\"error\":{\"message\":\"failed\"}}", true));
        await Assert.ThrowsAsync<IOException>(() => new CdpSession(socket, default).Evaluate("true"));
    }

    private sealed class ReplySocket(params (string Text, bool End)[] replies) : WebSocket
    {
        private readonly Queue<(string Text, bool End)> _replies = new(replies);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            var reply = _replies.Dequeue();
            var bytes = Encoding.UTF8.GetBytes(reply.Text);
            bytes.AsSpan().CopyTo(buffer.AsSpan());
            return Task.FromResult(new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, reply.End));
        }
    }
}

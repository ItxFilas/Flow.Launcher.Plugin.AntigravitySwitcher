using System.IO;
using System.Net;
using System.Net.Http;
using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class SwitcherLocatorTests
{
    [Fact]
    public void FindOnPath_FindsStandardExecutable_WithoutWhereExe()
    {
        // cmd.exe is guaranteed to exist on Windows PATH
        var path = SwitcherLocator.FindOnPath("cmd.exe");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void FindOnPath_FindsExecutableWithoutExtension_OnWindows()
    {
        var path = SwitcherLocator.FindOnPath("cmd");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void FindOnPath_ReturnsNullForNonExistentExecutable()
    {
        var path = SwitcherLocator.FindOnPath("non_existent_binary_xyz_123.exe");
        Assert.Null(path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindOnPath_ReturnsNullForEmptyInput(string input)
    {
        var path = SwitcherLocator.FindOnPath(input);
        Assert.Null(path);
    }

    [Fact]
    public void HasWsl_IsCachedBetweenCalls()
    {
        var first = SwitcherLocator.HasWsl();
        var second = SwitcherLocator.HasWsl();
        Assert.Equal(first, second);
    }
}

public sealed class DashboardClientReachabilityTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }

    [Fact]
    public async Task IsReachableAsync_ReturnsTrue_WhenEndpointReturnsOk()
    {
        using var client = new DashboardClient(new StubHandler(req =>
            req.RequestUri?.AbsolutePath == "/api/status"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound)));

        var reachable = await client.IsReachableAsync(new Uri("http://127.0.0.1:8080/"));
        Assert.True(reachable);
    }

    [Fact]
    public async Task IsReachableAsync_ReturnsFalse_WhenEndpointFails()
    {
        using var client = new DashboardClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var reachable = await client.IsReachableAsync(new Uri("http://127.0.0.1:8080/"));
        Assert.False(reachable);
    }

    [Fact]
    public async Task IsReachableAsync_TimesOutFast_WhenProbeExceedsTimeout()
    {
        using var client = new DashboardClient(new StubHandler(_ =>
        {
            Thread.Sleep(100);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var reachable = await client.IsReachableAsync(new Uri("http://127.0.0.1:8080/"), TimeSpan.FromMilliseconds(15));
        Assert.False(reachable);
    }
}

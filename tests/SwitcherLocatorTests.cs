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
    private sealed class StubAsyncHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
        public StubAsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request, cancellationToken);
    }

    [Fact]
    public async Task IsReachableAsync_ReturnsTrue_WhenEndpointReturnsOk()
    {
        using var client = new DashboardClient(new StubAsyncHandler((req, _) =>
            Task.FromResult(req.RequestUri?.AbsolutePath == "/api/status"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound))));

        var reachable = await client.IsReachableAsync(new Uri("http://127.0.0.1:8080/"));
        Assert.True(reachable);
    }

    [Fact]
    public async Task IsReachableAsync_ReturnsFalse_WhenEndpointFails()
    {
        using var client = new DashboardClient(new StubAsyncHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));

        var reachable = await client.IsReachableAsync(new Uri("http://127.0.0.1:8080/"));
        Assert.False(reachable);
    }

    [Fact]
    public async Task IsReachableAsync_TimesOutFast_WhenProbeExceedsTimeout()
    {
        using var client = new DashboardClient(new StubAsyncHandler(async (_, token) =>
        {
            await Task.Delay(100, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var reachable = await client.IsReachableAsync(new Uri("http://127.0.0.1:8080/"), TimeSpan.FromMilliseconds(15));
        Assert.False(reachable);
    }
}

public sealed class CliRunnerTimeoutTests
{
    [Fact]
    public async Task RunAsync_ThrowsTimeoutException_WhenProcessExceedsTimeout()
    {
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (!File.Exists(cmdPath))
        {
            return;
        }

        var prev = Environment.GetEnvironmentVariable("ANTIGRAVITY_SWITCHER_BIN");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_SWITCHER_BIN", cmdPath);
            var locator = new SwitcherLocator();
            var runner = new CliRunner(locator);

            // "cmd.exe /c pause" waits for stdin without closing stdout/stderr
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await runner.RunAsync(["/c", "pause"], timeout: TimeSpan.FromMilliseconds(100));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_SWITCHER_BIN", prev);
        }
    }
}

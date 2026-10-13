using System.Net;
using System.Net.Http;
using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class AccountServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }

    /// <summary>Serves a live /api/status and delegates /api/accounts/{id}/login-swap to the responder.</summary>
    private static HttpMessageHandler SwitcherApi(Func<HttpRequestMessage, HttpResponseMessage> loginSwapResponder) =>
        new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/status")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\"}")
                };
            }

            return loginSwapResponder(request);
        });

    private sealed class StubAppController : IAntigravityAppController
    {
        public bool AppliesSwap { get; init; } = true;
        public int RestartCalls { get; private set; }

        public Task<bool> RestartForLoginSwapAsync(CancellationToken cancellationToken = default)
        {
            RestartCalls++;
            return Task.FromResult(AppliesSwap);
        }
    }

    [Fact]
    public async Task SwitchAsync_SwapsLoginThenRestartsAppForLogin()
    {
        var controller = new StubAppController { AppliesSwap = true };
        var service = CreateService(
            new DashboardClient(SwitcherApi(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"swapped\":true,\"email\":\"signed-in@example.com\"}")
            })),
            controller);

        using var port = SetEnvPort();
        await service.SwitchAsync(new AccountInfo { Id = "acct-1", Email = "signed-in@example.com" });

        Assert.Equal(1, controller.RestartCalls);
    }

    [Fact]
    public async Task SwitchAsync_Throws_WhenSwapIsRejected()
    {
        var controller = new StubAppController { AppliesSwap = true };
        var service = CreateService(
            new DashboardClient(SwitcherApi(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("{\"error\":{\"code\":422,\"message\":\"account has no refresh token\"}}")
            })),
            controller);

        using var port = SetEnvPort();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SwitchAsync(new AccountInfo { Id = "acct-1", Email = "x@example.com" }));

        Assert.Contains("account has no refresh token", ex.Message);
        Assert.Equal(0, controller.RestartCalls);
    }

    [Fact]
    public async Task SwitchAsync_Throws_WhenAppCannotBeBroughtUp()
    {
        var controller = new StubAppController { AppliesSwap = false };
        var service = CreateService(
            new DashboardClient(SwitcherApi(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"swapped\":true,\"email\":\"signed-in@example.com\"}")
            })),
            controller);

        using var port = SetEnvPort();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SwitchAsync(new AccountInfo { Id = "acct-1", Email = "signed-in@example.com" }));

        Assert.Contains("could not be started", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, controller.RestartCalls);
    }

    private static AccountService CreateService(DashboardClient dashboard, IAntigravityAppController controller)
    {
        // The real locator is fine: ANTIGRAVITY_PORT makes the dashboard probe prefer the env port,
        // which the stub handler answers, so the CLI is only touched if that probe fails (it cannot).
        return new AccountService(new CliRunner(new SwitcherLocator()), dashboard, new AccountCache(), controller);
    }

    private static IDisposable SetEnvPort()
    {
        const string env = "ANTIGRAVITY_PORT";
        Environment.SetEnvironmentVariable(env, "65533");
        return new ActionDisposable(() => Environment.SetEnvironmentVariable(env, null));
    }

    private sealed class ActionDisposable : IDisposable
    {
        private readonly Action _action;
        private bool _disposed;

        public ActionDisposable(Action action) => _action = action;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _action();
        }
    }
}
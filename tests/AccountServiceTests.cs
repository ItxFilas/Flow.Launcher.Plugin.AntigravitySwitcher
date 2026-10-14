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
        public int RestartAppCalls { get; private set; }
        public int EnsureAsarOptimizedCalls { get; private set; }

        public Task<bool> RestartForLoginSwapAsync(CancellationToken cancellationToken = default)
        {
            RestartCalls++;
            return Task.FromResult(AppliesSwap);
        }

        public Task<bool> RestartAppAsync(CancellationToken cancellationToken = default)
        {
            RestartAppCalls++;
            return Task.FromResult(AppliesSwap);
        }

        public bool EnsureAsarOptimized(string? asarPath = null)
        {
            EnsureAsarOptimizedCalls++;
            return true;
        }
    }

    private sealed class StubCdpClient : IAntigravityCdpClient
    {
        public int TriggerAutoDismissCalls { get; private set; }
        public int? ActivePort { get; set; } = 9222;

        public int? ResolveActivePort() => ActivePort;

        public Task<bool> AutoDismissSecurityNoticeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public void TriggerAutoDismissInBackground()
        {
            TriggerAutoDismissCalls++;
        }
    }

    [Fact]
    public async Task SwitchAsync_SwapsLoginThenRestartsAppForLogin_AndTriggersCdpAutoDismiss()
    {
        var controller = new StubAppController { AppliesSwap = true };
        var cdp = new StubCdpClient();
        var service = CreateService(
            new DashboardClient(SwitcherApi(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"swapped\":true,\"email\":\"signed-in@example.com\"}")
            })),
            controller,
            cdp);

        using var port = SetEnvPort();
        await service.SwitchAsync(new AccountInfo { Id = "acct-1", Email = "signed-in@example.com" });

        Assert.Equal(1, controller.RestartCalls);
        Assert.Equal(1, cdp.TriggerAutoDismissCalls);
    }

    [Fact]
    public async Task SwitchAsync_Throws_WhenSwapIsRejected()
    {
        var controller = new StubAppController { AppliesSwap = true };
        var cdp = new StubCdpClient();
        var service = CreateService(
            new DashboardClient(SwitcherApi(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("{\"error\":{\"code\":422,\"message\":\"account has no refresh token\"}}")
            })),
            controller,
            cdp);

        using var port = SetEnvPort();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SwitchAsync(new AccountInfo { Id = "acct-1", Email = "x@example.com" }));

        Assert.Contains("account has no refresh token", ex.Message);
        Assert.Equal(0, controller.RestartCalls);
        Assert.Equal(0, cdp.TriggerAutoDismissCalls);
    }

    [Fact]
    public async Task SwitchAsync_Throws_WhenAppCannotBeBroughtUp()
    {
        var controller = new StubAppController { AppliesSwap = false };
        var cdp = new StubCdpClient();
        var service = CreateService(
            new DashboardClient(SwitcherApi(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"swapped\":true,\"email\":\"signed-in@example.com\"}")
            })),
            controller,
            cdp);

        using var port = SetEnvPort();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SwitchAsync(new AccountInfo { Id = "acct-1", Email = "signed-in@example.com" }));

        Assert.Contains("could not be started", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, controller.RestartCalls);
        Assert.Equal(0, cdp.TriggerAutoDismissCalls);
    }

    [Fact]
    public async Task RestartBackendOnlyAsync_DelegatesToAntigravityAppController()
    {
        var controller = new StubAppController { AppliesSwap = true };
        var service = CreateService(
            new DashboardClient(SwitcherApi(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            controller);

        var result = await service.RestartBackendOnlyAsync();

        Assert.True(result);
        Assert.Equal(1, controller.RestartCalls);
    }

    [Fact]
    public async Task RestartAppAsync_DelegatesToAntigravityAppController()
    {
        var controller = new StubAppController { AppliesSwap = true };
        var service = CreateService(
            new DashboardClient(SwitcherApi(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            controller);

        var result = await service.RestartAppAsync();

        Assert.True(result);
        Assert.Equal(1, controller.RestartAppCalls);
    }

    [Fact]
    public void TryGetCached_WithStaleFlag_And_InvalidateCache_DelegateToCache()
    {
        var cache = new AccountCache();
        var service = new AccountService(
            new CliRunner(new SwitcherLocator()),
            new DashboardClient(),
            cache,
            new StubAppController());

        Assert.False(service.TryGetCached(out var initial, out var initialStale));
        Assert.Empty(initial);
        Assert.False(initialStale);

        var sample = new List<AccountInfo> { new() { Id = "test-1", Email = "test@example.com" } };
        cache.Set(sample);

        Assert.True(service.TryGetCached(out var cached, out var isStale));
        Assert.Same(sample, cached);
        Assert.False(isStale);

        service.InvalidateCache();
        Assert.False(service.TryGetCached(out _, out _));
    }

    [Fact]
    public void Constructor_DefaultsCdpClientAndAppController_WhenNull()
    {
        var service = new AccountService(
            new CliRunner(new SwitcherLocator()),
            new DashboardClient(),
            new AccountCache());

        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_AcceptsCustomCdpClient_WithDefaultAppController()
    {
        var cdp = new StubCdpClient();
        var service = new AccountService(
            new CliRunner(new SwitcherLocator()),
            new DashboardClient(),
            new AccountCache(),
            antigravityApp: null,
            cdpClient: cdp);

        Assert.NotNull(service);
    }

    private static AccountService CreateService(DashboardClient dashboard, IAntigravityAppController controller, IAntigravityCdpClient? cdpClient = null)
    {
        // The real locator is fine: ANTIGRAVITY_PORT makes the dashboard probe prefer the env port,
        // which the stub handler answers, so the CLI is only touched if that probe fails (it cannot).
        return new AccountService(new CliRunner(new SwitcherLocator()), dashboard, new AccountCache(), controller, cdpClient);
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
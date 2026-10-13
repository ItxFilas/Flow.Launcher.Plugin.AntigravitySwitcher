using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Flow.Launcher.Plugin.AntigravitySwitcher.Routing;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Flow.Launcher.Plugin.AntigravitySwitcher.UI;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class MainTests
{
    private static Query CreateQuery(string search)
    {
        var query = new Query();
        typeof(Query).GetProperty("Search")?.SetValue(query, search);
        return query;
    }

    [Fact]
    public void Main_ImplementsExpectedInterfaces()
    {
        var main = new Main();
        Assert.IsAssignableFrom<IAsyncPlugin>(main);
        Assert.IsAssignableFrom<IContextMenu>(main);
        Assert.IsAssignableFrom<IReloadable>(main);
    }

    [Fact]
    public async Task InitAsync_InitializesSuccessfully()
    {
        var main = new Main();
        var context = new PluginInitContext();

        var initTask = main.InitAsync(context);
        await initTask;

        Assert.True(initTask.IsCompletedSuccessfully);
    }

    [Fact]
    public void ReloadData_InvalidatesCache()
    {
        var cache = new AccountCache();
        cache.Set(new List<AccountInfo>
        {
            new() { Id = "test-1", Email = "test@example.com" }
        });

        Assert.True(cache.TryGet(out _));

        var runner = new CliRunner(new SwitcherLocator());
        var service = new AccountService(runner, new DashboardClient(), cache);
        var router = new QueryRouter(service);
        var main = new Main(service, router);

        main.ReloadData();

        Assert.False(cache.TryGet(out _));
    }

    [Fact]
    public async Task QueryAsync_DelegatesToQueryRouter()
    {
        var cache = new AccountCache();
        var runner = new CliRunner(new SwitcherLocator());
        var service = new AccountService(runner, new DashboardClient(), cache);
        var router = new QueryRouter(service);
        var main = new Main(service, router);

        var query = CreateQuery("help");
        var results = await main.QueryAsync(query, CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Title == "ag");
        Assert.Contains(results, r => r.Title == "ag add");
    }

    [Fact]
    public void LoadContextMenus_DelegatesToQueryRouter()
    {
        var cache = new AccountCache();
        var runner = new CliRunner(new SwitcherLocator());
        var service = new AccountService(runner, new DashboardClient(), cache);
        var router = new QueryRouter(service);
        var main = new Main(service, router);

        var account = new AccountInfo { Id = "acc-1", Email = "user@test.com" };
        var selectedResult = ResultFactory.CreateAccountResult(account);

        var menus = main.LoadContextMenus(selectedResult);

        Assert.NotEmpty(menus);
        Assert.Contains(menus, m => m.Title.Contains("Sign into Antigravity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(menus, m => m.Title.Contains("Remove this account", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(menus, m => m.Title.Contains("Copy email to clipboard", StringComparison.OrdinalIgnoreCase));
    }
}

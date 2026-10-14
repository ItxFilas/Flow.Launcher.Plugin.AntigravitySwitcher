using System.Net;
using System.Net.Http;
using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Flow.Launcher.Plugin.AntigravitySwitcher.Routing;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Flow.Launcher.Plugin.AntigravitySwitcher.UI;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class QueryRouterTests
{
    [Theory]
    [InlineData("", CommandKind.ListAccounts)]
    [InlineData("add", CommandKind.Add)]
    [InlineData("remove", CommandKind.Remove)]
    [InlineData("refresh", CommandKind.Refresh)]
    [InlineData("status", CommandKind.Status)]
    [InlineData("restart", CommandKind.Restart)]
    [InlineData("help", CommandKind.Help)]
    [InlineData("google@gmail.com", CommandKind.ListAccounts)]
    public void ParseCommand_IdentifiesCommandKind(string queryText, CommandKind expected)
    {
        var parsed = QueryRouter.ParseCommand(queryText);
        Assert.Equal(expected, parsed.Kind);
    }

    [Theory]
    [InlineData("add foo@example.com", CommandKind.Add, "foo@example.com")]
    [InlineData("remove dev@gmail.com", CommandKind.Remove, "dev@gmail.com")]
    [InlineData("refresh quotas", CommandKind.Refresh, "quotas")]
    [InlineData("status verbose", CommandKind.Status, "verbose")]
    [InlineData("restart backend", CommandKind.Restart, "backend")]
    [InlineData("help remove", CommandKind.Help, "remove")]
    [InlineData("  RESTART  ", CommandKind.Restart, "")]
    [InlineData("  REMOVE   alice@example.com  ", CommandKind.Remove, "alice@example.com")]
    public void ParseCommand_ExtractsArguments_AndIgnoresCase(string queryText, CommandKind expectedKind, string expectedArg)
    {
        var parsed = QueryRouter.ParseCommand(queryText);
        Assert.Equal(expectedKind, parsed.Kind);
        Assert.Equal(expectedArg, parsed.Argument);
    }

    [Fact]
    public void StringMatcher_MatchesExpectedPatterns()
    {
        // Empty query matches everything
        var empty = StringMatcher.FuzzySearch("", "anything");
        Assert.True(empty.Success);

        // Null or empty target fails
        var nullTarget = StringMatcher.FuzzySearch("query", "");
        Assert.False(nullTarget.Success);

        // Exact match has highest score
        var exact = StringMatcher.FuzzySearch("work@company.com", "work@company.com");
        Assert.True(exact.Success);
        Assert.Equal(200, exact.Score);

        // Prefix match
        var prefix = StringMatcher.FuzzySearch("work", "work@company.com");
        Assert.True(prefix.Success);
        Assert.True(prefix.Score >= 160);

        // Substring match
        var sub = StringMatcher.FuzzySearch("company", "work@company.com");
        Assert.True(sub.Success);
        Assert.True(sub.Score >= 70);

        // Fuzzy match: acronym/subsequence
        var fuzzy = StringMatcher.FuzzySearch("wk", "work@company.com");
        Assert.True(fuzzy.Success);
        Assert.True(fuzzy.Score > 0);

        // Non-match
        var nonMatch = StringMatcher.FuzzySearch("xyz", "work@company.com");
        Assert.False(nonMatch.Success);
    }

    [Fact]
    public void ResultFactory_CreateAccountResult_SetsPropertiesCorrectly()
    {
        var activeAccount = new AccountInfo
        {
            Id = "acc-1",
            Email = "active@example.com",
            IsActive = true,
            FiveHourPercent = 100,
            WeeklyPercent = 100
        };

        var activeResult = ResultFactory.CreateAccountResult(activeAccount);
        Assert.Contains("active@example.com", activeResult.Title);
        Assert.Contains("[ACTIVE]", activeResult.Title);
        Assert.Equal(ResultFactory.ActiveIcon, activeResult.IcoPath);
        Assert.Equal(180, activeResult.Score);
        Assert.Equal("active@example.com", activeResult.CopyText);
        Assert.IsType<ResultContext>(activeResult.ContextData);
        Assert.Equal(ResultKind.Account, ((ResultContext)activeResult.ContextData).Kind);

        var standbyAccount = new AccountInfo
        {
            Id = "acc-2",
            Email = "standby@example.com",
            IsActive = false,
            RemainingPercent = 80
        };

        var standbyResult = ResultFactory.CreateAccountResult(standbyAccount);
        Assert.Equal(ResultFactory.StandbyIcon, standbyResult.IcoPath);
        Assert.Equal(150, standbyResult.Score); // 100 + clamp(80, 0, 50) = 150

        var exhaustedAccount = new AccountInfo
        {
            Id = "acc-3",
            Email = "exhausted@example.com",
            Status = "exhausted",
            IsActive = false
        };

        var exhaustedResult = ResultFactory.CreateAccountResult(exhaustedAccount);
        Assert.Equal(ResultFactory.ExhaustedIcon, exhaustedResult.IcoPath);
    }

    [Fact]
    public void ResultFactory_CreateNextSwitchResult_HandlesNullAndAvailableAccount()
    {
        var nullResult = ResultFactory.CreateNextSwitchResult(null);
        Assert.Equal("Switch to next account", nullResult.Title);
        Assert.Contains("No other account", nullResult.SubTitle);
        Assert.Equal(ResultFactory.NextIcon, nullResult.IcoPath);
        Assert.Equal(200, nullResult.Score);

        var nextAccount = new AccountInfo
        {
            Id = "acc-next",
            Email = "next@example.com",
            FiveHourPercent = 90,
            WeeklyPercent = 85
        };

        var nextResult = ResultFactory.CreateNextSwitchResult(nextAccount);
        Assert.Contains("next@example.com", nextResult.SubTitle);
        Assert.Contains("5hr 90%", nextResult.SubTitle);
        Assert.Equal(ResultFactory.NextIcon, nextResult.IcoPath);
    }

    [Fact]
    public void ResultFactory_CreatesUtilityResults()
    {
        var addResult = ResultFactory.CreateAddAccountResult();
        Assert.Equal("Add Google account", addResult.Title);
        Assert.Equal(ResultFactory.AddIcon, addResult.IcoPath);

        var refreshResult = ResultFactory.CreateRefreshResult();
        Assert.Equal("Refresh quotas", refreshResult.Title);
        Assert.Equal(ResultFactory.RefreshIcon, refreshResult.IcoPath);

        var restartResult = ResultFactory.CreateRestartResult();
        Assert.Equal("Restart Antigravity Language Server", restartResult.Title);
        Assert.Equal(ResultFactory.AppIcon, restartResult.IcoPath);

        var statusResult = ResultFactory.CreateStatusResult("Connected\nPort: 8080\nStatus: OK");
        Assert.Equal("Connected", statusResult.Title);
        Assert.Contains("Connected · Port: 8080 · Status: OK", statusResult.SubTitle);
        Assert.Equal(ResultFactory.AppIcon, statusResult.IcoPath);

        var errorResult = ResultFactory.CreateErrorResult("Error Title", "Error Subtitle");
        Assert.Equal("Error Title", errorResult.Title);
        Assert.Equal("Error Subtitle", errorResult.SubTitle);
        Assert.Equal(0, errorResult.Score);
    }

    [Fact]
    public async Task RouteAsync_RoutesStandardCommands()
    {
        var cache = new AccountCache();
        var service = new AccountService(new CliRunner(new SwitcherLocator()), new DashboardClient(), cache);
        var router = new QueryRouter(service);

        // Add command via string
        var addResults = await router.RouteAsync("add");
        Assert.Single(addResults);
        Assert.Equal("Add Google account", addResults[0].Title);

        // Restart command via Query object
        var restartResults = await router.RouteAsync(CreateQuery("restart"));
        Assert.Equal(2, restartResults.Count);
        Assert.Equal("Restart Antigravity Backend (Fast)", restartResults[0].Title);
        Assert.Equal("Restart Antigravity IDE (Full)", restartResults[1].Title);

        var restartAppResults = await router.RouteAsync(CreateQuery("restart app"));
        Assert.Equal(2, restartAppResults.Count);
        Assert.Equal("Restart Antigravity IDE (Full)", restartAppResults[0].Title);

        var restartBackendResults = await router.RouteAsync(CreateQuery("restart backend"));
        Assert.Equal(2, restartBackendResults.Count);
        Assert.Equal("Restart Antigravity Backend (Fast)", restartBackendResults[0].Title);

        // Refresh command
        var refreshResults = await router.RouteAsync("refresh");
        Assert.Single(refreshResults);
        Assert.Equal("Refresh quotas", refreshResults[0].Title);

        // Help command
        var helpResults = await router.RouteAsync("help");
        Assert.True(helpResults.Count >= 5);
        Assert.Contains(helpResults, r => r.Title == "ag restart");
    }

    [Fact]
    public async Task RouteAsync_HelpCommand_StripsBracketedParametersInAction()
    {
        var cache = new AccountCache();
        var service = new AccountService(new CliRunner(new SwitcherLocator()), new DashboardClient(), cache);
        var context = new PluginInitContext();
        string? changedQuery = null;
        var api = System.Reflection.DispatchProxy.Create<Flow.Launcher.Plugin.IPublicAPI, TestPublicApiProxy>();
        ((TestPublicApiProxy)(object)api).OnChangeQuery = q => changedQuery = q;
        typeof(PluginInitContext).GetProperty("API")?.SetValue(context, api);
        typeof(PluginInitContext).GetProperty("CurrentPluginMetadata")?.SetValue(context, new Flow.Launcher.Plugin.PluginMetadata { ActionKeyword = "ag" });

        var router = new QueryRouter(service, context);
        var helpResults = await router.RouteAsync("help");
        var removeHint = helpResults.FirstOrDefault(r => r.Title == "ag remove [filter]");
        Assert.NotNull(removeHint);
        Assert.NotNull(removeHint.Action);

        removeHint.Action(null!);
        Assert.Equal("ag remove ", changedQuery);
    }

    public class TestPublicApiProxy : System.Reflection.DispatchProxy
    {
        public Action<string>? OnChangeQuery { get; set; }
        public Action<string, string, string?>? OnShowMsg { get; set; }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "ChangeQuery" && args?.Length > 0 && args[0] is string q)
            {
                OnChangeQuery?.Invoke(q);
            }
            else if (targetMethod?.Name == "ShowMsg" && args?.Length >= 2)
            {
                var title = args[0] as string ?? string.Empty;
                var subTitle = args[1] as string ?? string.Empty;
                var icon = args.Length > 2 ? args[2] as string : null;
                OnShowMsg?.Invoke(title, subTitle, icon);
            }
            return null;
        }
    }

    [Fact]
    public async Task RouteAsync_FuzzyFiltersAccounts()
    {
        var cache = new AccountCache();
        var accounts = new List<AccountInfo>
        {
            new() { Id = "acc-1", Email = "work.primary@gmail.com", FiveHourPercent = 80, WeeklyPercent = 90 },
            new() { Id = "acc-2", Email = "personal.dev@gmail.com", FiveHourPercent = 60, WeeklyPercent = 70 },
            new() { Id = "acc-3", Email = "testing@company.com", FiveHourPercent = 40, WeeklyPercent = 50 }
        };
        cache.Set(accounts);

        var service = new AccountService(new CliRunner(new SwitcherLocator()), new DashboardClient(), cache);
        var router = new QueryRouter(service);

        // Empty query returns Next switch + all 3 accounts + command hints
        var allResults = await router.RouteAsync("");
        Assert.Contains(allResults, r => r.Title.Contains("Switch to next account"));
        Assert.Contains(allResults, r => r.Title.Contains("work.primary@gmail.com"));
        Assert.Contains(allResults, r => r.Title.Contains("personal.dev@gmail.com"));
        Assert.Contains(allResults, r => r.Title.Contains("testing@company.com"));

        // Fuzzy filter: "work" matches work.primary@gmail.com
        var filteredResults = await router.RouteAsync(CreateQuery("work"));
        Assert.Contains(filteredResults, r => r.Title.Contains("work.primary@gmail.com"));
        Assert.DoesNotContain(filteredResults, r => r.Title.Contains("personal.dev@gmail.com"));
        Assert.DoesNotContain(filteredResults, r => r.Title.Contains("testing@company.com"));

        // Fuzzy filter: "dev" matches personal.dev@gmail.com
        var devResults = await router.RouteAsync("dev");
        Assert.Contains(devResults, r => r.Title.Contains("personal.dev@gmail.com"));
        Assert.DoesNotContain(devResults, r => r.Title.Contains("work.primary@gmail.com"));

        // Non-matching query returns "No matching accounts"
        var emptyResults = await router.RouteAsync("nonexistent_query_xyz");
        Assert.Single(emptyResults);
        Assert.Equal("No matching accounts", emptyResults[0].Title);
    }

    [Fact]
    public async Task RouteAsync_RemoveCommand_ListsAndFiltersAccounts()
    {
        var cache = new AccountCache();
        var accounts = new List<AccountInfo>
        {
            new() { Id = "acc-1", Email = "alice@example.com" },
            new() { Id = "acc-2", Email = "bob@example.com" }
        };
        cache.Set(accounts);

        var service = new AccountService(new CliRunner(new SwitcherLocator()), new DashboardClient(), cache);
        var router = new QueryRouter(service);

        // "remove" lists both accounts
        var removeAll = await router.RouteAsync("remove");
        Assert.Equal(2, removeAll.Count);
        Assert.Contains(removeAll, r => r.Title == "Remove alice@example.com");
        Assert.Contains(removeAll, r => r.Title == "Remove bob@example.com");

        // "remove alice" filters to Alice with boosted score
        var removeAlice = await router.RouteAsync(CreateQuery("remove alice"));
        Assert.Single(removeAlice);
        Assert.Equal("Remove alice@example.com", removeAlice[0].Title);
        Assert.True(removeAlice[0].Score > 100); // 100 base + fuzzy match score

        // "remove non-existent" returns empty indicator
        var removeNone = await router.RouteAsync("remove charlie");
        Assert.Single(removeNone);
        Assert.Equal("No account to remove", removeNone[0].Title);
    }

    private static Query CreateQuery(string search)
    {
        var query = new Query();
        typeof(Query).GetProperty("Search")?.SetValue(query, search);
        return query;
    }

    [Fact]
    public void LoadContextMenus_ReturnsMenuItems_ForAccountResult()
    {
        var service = new AccountService(new CliRunner(new SwitcherLocator()), new DashboardClient(), new AccountCache());
        var router = new QueryRouter(service);

        var account = new AccountInfo { Id = "acc-1", Email = "user@example.com" };
        var accountResult = ResultFactory.CreateAccountResult(account);

        var contextMenus = router.LoadContextMenus(accountResult);
        Assert.Equal(3, contextMenus.Count);
        Assert.Contains(contextMenus, r => r.Title == "Sign into Antigravity as this account");
        Assert.Contains(contextMenus, r => r.Title == "Remove this account");
        Assert.Contains(contextMenus, r => r.Title == "Copy email to clipboard");

        // Non-account result returns empty list
        var nonAccountResult = ResultFactory.CreateAddAccountResult();
        var emptyMenus = router.LoadContextMenus(nonAccountResult);
        Assert.Empty(emptyMenus);
    }

    [Fact]
    public async Task TryStartSwitch_InactiveAccount_ShowsImmediateNotificationAndSwitchedOnCompletion()
    {
        const string envVar = "ANTIGRAVITY_PORT";
        Environment.SetEnvironmentVariable(envVar, "65530");
        try
        {
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.RequestUri!.AbsolutePath == "/api/status")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"ok\"}") };
                }
                if (req.RequestUri!.AbsolutePath.Contains("/login-swap"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"swapped\":true,\"email\":\"inactive@example.com\"}") };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var dashboard = new DashboardClient(handler);
            var cache = new AccountCache();
            var service = new AccountService(new CliRunner(new SwitcherLocator()), dashboard, cache, new StubAppController());

            var context = new PluginInitContext();
            var messages = new List<(string Title, string SubTitle, string? Icon)>();
            var switchedTcs = new TaskCompletionSource<bool>();
            var api = System.Reflection.DispatchProxy.Create<Flow.Launcher.Plugin.IPublicAPI, TestPublicApiProxy>();
            var proxy = (TestPublicApiProxy)(object)api;
            proxy.OnShowMsg = (title, subTitle, icon) =>
            {
                messages.Add((title, subTitle, icon));
                if (subTitle.StartsWith("Switched to", StringComparison.OrdinalIgnoreCase))
                {
                    switchedTcs.TrySetResult(true);
                }
            };
            typeof(PluginInitContext).GetProperty("API")?.SetValue(context, api);
            typeof(PluginInitContext).GetProperty("CurrentPluginMetadata")?.SetValue(context, new Flow.Launcher.Plugin.PluginMetadata { ActionKeyword = "ag" });

            var inactiveAccount = new AccountInfo
            {
                Id = "acc-inactive",
                Email = "inactive@example.com",
                IsActive = false
            };
            cache.Set(new List<AccountInfo> { inactiveAccount });
            var router = new QueryRouter(service, context);

            var results = await router.RouteAsync("");
            var accountResult = results.FirstOrDefault(r => r.ContextData is ResultContext { Account.Id: "acc-inactive" });
            Assert.NotNull(accountResult);
            Assert.NotNull(accountResult.AsyncAction);

            // Act: trigger selection
            var actionResult = await accountResult.AsyncAction(new Flow.Launcher.Plugin.ActionContext());

            // 1. Assert immediate notification is shown synchronously upon selection
            Assert.True(actionResult);
            Assert.Contains(messages, m => m.Title == "Antigravity Switcher" && m.SubTitle == $"Switching to {inactiveAccount.DisplayName}...");

            // 2. Wait for async switch to complete and assert completion notification
            var completedTask = await Task.WhenAny(switchedTcs.Task, Task.Delay(3000));
            Assert.Equal(switchedTcs.Task, completedTask);
            Assert.Contains(messages, m => m.Title == "Antigravity Switcher" && m.SubTitle == $"Switched to {inactiveAccount.DisplayName}");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact]
    public async Task TryStartSwitch_ActiveAccount_DoesNotShowSwitchingNotification()
    {
        var context = new PluginInitContext();
        var messages = new List<(string Title, string SubTitle, string? Icon)>();
        var api = System.Reflection.DispatchProxy.Create<Flow.Launcher.Plugin.IPublicAPI, TestPublicApiProxy>();
        var proxy = (TestPublicApiProxy)(object)api;
        proxy.OnShowMsg = (title, subTitle, icon) => messages.Add((title, subTitle, icon));
        typeof(PluginInitContext).GetProperty("API")?.SetValue(context, api);
        typeof(PluginInitContext).GetProperty("CurrentPluginMetadata")?.SetValue(context, new Flow.Launcher.Plugin.PluginMetadata { ActionKeyword = "ag" });

        var cache = new AccountCache();
        var activeAccount = new AccountInfo
        {
            Id = "acc-active",
            Email = "active@example.com",
            IsActive = true
        };
        cache.Set(new List<AccountInfo> { activeAccount });
        var service = new AccountService(new CliRunner(new SwitcherLocator()), new DashboardClient(), cache);
        var router = new QueryRouter(service, context);

        var results = await router.RouteAsync("");
        var accountResult = results.FirstOrDefault(r => r.ContextData is ResultContext { Account.Id: "acc-active" });
        Assert.NotNull(accountResult);
        Assert.NotNull(accountResult.AsyncAction);

        var actionResult = await accountResult.AsyncAction(new Flow.Launcher.Plugin.ActionContext());

        Assert.True(actionResult);
        Assert.Single(messages);
        Assert.Equal($"Already signed in as {activeAccount.DisplayName}", messages[0].SubTitle);
        Assert.DoesNotContain(messages, m => m.SubTitle.StartsWith("Switching to", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubAppController : IAntigravityAppController
    {
        public Task<bool> RestartForLoginSwapAsync(string? savedChatPath = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> RestartAppAsync(string? savedChatPath = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> RestartBackendOnlyAsync(string? savedChatPath = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public bool EnsureAsarOptimized(string? asarPath = null) => true;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}


using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Flow.Launcher.Plugin.AntigravitySwitcher.Routing;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;

namespace Flow.Launcher.Plugin.AntigravitySwitcher;

public sealed class Main : IAsyncPlugin, IContextMenu, IReloadable
{
    private PluginInitContext _context = null!;
    private AccountService _service = null!;
    private QueryRouter _router = null!;

    public Main()
    {
    }

    internal Main(AccountService service, QueryRouter router)
    {
        _service = service;
        _router = router;
    }

    public Task InitAsync(PluginInitContext context)
    {
        _context = context;
        var locator = new SwitcherLocator();
        var runner = new CliRunner(locator);
        _service = new AccountService(runner, new DashboardClient(), new AccountCache());
        _router = new QueryRouter(_service, _context);

        // Pre-warm the account cache in background on startup so the first query is instantaneous
        _ = Task.Run(async () =>
        {
            try
            {
                await _service.ListAccountsAsync(forceRefresh: true).ConfigureAwait(false);
            }
            catch
            {
                // Ignored: first user query will retry if needed
            }
        });

        return Task.CompletedTask;
    }

    public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        return await _router.RouteAsync(query, token).ConfigureAwait(false);
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        return _router.LoadContextMenus(selectedResult);
    }

    public void ReloadData()
    {
        _service.InvalidateCache();
    }
}

using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Flow.Launcher.Plugin.AntigravitySwitcher.Models;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

public sealed class AccountService
{
    private readonly CliRunner _cli;
    private readonly DashboardClient _dashboard;
    private readonly AccountCache _cache;
    private readonly IAntigravityAppController _antigravityApp;
    private int? _dashboardPort;
    private DateTimeOffset _portCachedAt;

    public AccountService(CliRunner cli, DashboardClient dashboard, AccountCache cache, IAntigravityAppController? antigravityApp = null)
    {
        _cli = cli;
        _dashboard = dashboard;
        _cache = cache;
        _antigravityApp = antigravityApp ?? new AntigravityAppController(cli);
    }

    public bool TryGetCached(out IReadOnlyList<AccountInfo> accounts) => _cache.TryGet(out accounts);

    public bool TryGetCached(out IReadOnlyList<AccountInfo> accounts, out bool isStale) => _cache.TryGet(out accounts, out isStale);

    public void InvalidateCache() => _cache.Invalidate();

    public async Task<IReadOnlyList<AccountInfo>> ListAccountsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cache.TryGet(out var cached))
        {
            return cached;
        }

        var accounts = await TryListFromApiAsync(cancellationToken).ConfigureAwait(false);
        if (accounts is null)
        {
            var result = await _cli.RunAsync(["list-accounts"], cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Combined)
                    ? "list-accounts failed."
                    : result.Combined.Trim());
            }

            accounts = AccountParser.ParseListAccounts(result.StdOut);
        }

        _cache.Set(accounts);
        return accounts;
    }

    /// <summary>
    /// Switches the Antigravity IDE login to the given account. The switcher daemon rewrites
    /// the Windows Credential Manager login the IDE reads at startup (real login swap), then
    /// the IDE is closed and relaunched normally so it signs in with the new account.
    /// </summary>
    public async Task SwitchAsync(AccountInfo account, CancellationToken cancellationToken = default)
    {
        var baseUri = await ResolveDashboardUriAsync(cancellationToken).ConfigureAwait(false);
        if (baseUri is null)
        {
            throw new InvalidOperationException(
                "The switcher daemon is not reachable. Run `antigravity-account-switcher serve` (or `launch`) first.");
        }

        var resultSwap = await _dashboard.LoginSwapAsync(baseUri, account.Id, cancellationToken).ConfigureAwait(false);
        var swapped = resultSwap.Succeeded;
        var error = resultSwap.Error;
        if (!swapped)
        {
            throw new InvalidOperationException(
                $"Signing into Antigravity as {account.DisplayName} failed. {error ?? "Unknown error."}");
        }

        // Apply the swap to the running app with minimal disruption: restart only the
        // language_server backend (the Electron frontend respawns it with the new login),
        // or launch the app when it is not running.
        var applied = await _antigravityApp.RestartForLoginSwapAsync(cancellationToken).ConfigureAwait(false);
        if (!applied)
        {
            throw new InvalidOperationException(
                "Login swapped, but Antigravity could not be started — open it manually to sign in with the new account.");
        }

        _cache.Invalidate();
    }

    public async Task<bool> RestartBackendOnlyAsync(CancellationToken cancellationToken = default)
    {
        return await _antigravityApp.RestartForLoginSwapAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RestartAppAsync(CancellationToken cancellationToken = default)
    {
        return await _antigravityApp.RestartAppAsync(cancellationToken).ConfigureAwait(false);
    }

    public void StartAddAccount()
    {
        _cli.StartDetached(["add-account"]);
        _cache.Invalidate();
    }

    public async Task RemoveAsync(AccountInfo account, CancellationToken cancellationToken = default)
    {
        // Preferred path: REST DELETE /api/accounts/{id} on the API.
        var baseUri = await ResolveApiUriAsync(cancellationToken).ConfigureAwait(false);
        if (baseUri is not null &&
            await _dashboard.DeleteAccountAsync(baseUri, account.Id, cancellationToken).ConfigureAwait(false))
        {
            _cache.Invalidate();
            return;
        }

        // Fall back to CLI variants for forward/backward compatibility.
        foreach (var command in new[] { "remove-account", "remove", "delete-account" })
        {
            try
            {
                var result = await _cli.RunAsync([command, account.Id], TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    _cache.Invalidate();
                    return;
                }

                if (!LooksUnknownCommand(result.Combined))
                {
                    break;
                }
            }
            catch (TimeoutException)
            {
                break;
            }
        }

        throw new InvalidOperationException(
            "Removing the account failed. Run the switcher as a daemon (`serve` / `launch`) — REST DELETE is available at /api/accounts/{id}.");
    }

    public async Task RefreshQuotasAsync(CancellationToken cancellationToken = default)
    {
        var baseUri = await ResolveApiUriAsync(cancellationToken).ConfigureAwait(false);
        if (baseUri is not null && await _dashboard.RefreshQuotasAsync(baseUri, cancellationToken).ConfigureAwait(false))
        {
            _cache.Invalidate();
            return;
        }

        var result = await _cli.RunAsync(["refresh-quotas"], TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Combined.Trim());
        }

        _cache.Invalidate();
    }

    public async Task<string> StatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await _cli.RunAsync(["status"], cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Combined.Trim();
    }

    public async Task<Uri> OpenApiUriAsync(CancellationToken cancellationToken = default)
    {
        var uri = await ResolveApiUriAsync(cancellationToken).ConfigureAwait(false);
        return uri ?? new Uri($"http://127.0.0.1:{DefaultApiPort}/");
    }

    /// <summary>Built-in default port of the switcher's REST API/proxy.</summary>
    public const int DefaultApiPort = 8080;
    public const int DefaultDashboardPort = DefaultApiPort;

    public static AccountInfo? SelectNextAvailable(IReadOnlyList<AccountInfo> accounts)
    {
        var eligible = accounts
            .Where(a => !a.IsActive)
            .Where(a => IsAvailableStatus(a.Status))
            .ToList();

        if (eligible.Count == 0)
        {
            return null;
        }

        return eligible
            .OrderByDescending(a => a.RemainingPercent)
            .ThenBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    // The switcher reports account status as one of: active, exhausted, error, disabled.
    // An empty/unknown status is treated as available (older CLI output without a status column).
    private static bool IsAvailableStatus(string status) =>
        string.IsNullOrWhiteSpace(status) ||
        string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<AccountInfo>?> TryListFromApiAsync(CancellationToken cancellationToken)
    {
        var baseUri = await ResolveApiUriAsync(cancellationToken).ConfigureAwait(false);
        if (baseUri is null)
        {
            return null;
        }

        var json = await _dashboard.GetAccountsJsonAsync(baseUri, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || !DashboardClient.LooksLikeJson(json))
        {
            return null;
        }

        return AccountParser.ParseApiAccounts(json);
    }



    private async Task<Uri?> ResolveApiUriInternalAsync(CancellationToken cancellationToken)
    {
        if (_dashboardPort is int cached && DateTimeOffset.UtcNow - _portCachedAt < TimeSpan.FromMinutes(5))
        {
            var cachedUri = new Uri($"http://127.0.0.1:{cached}/");
            if (await _dashboard.IsReachableAsync(cachedUri, cancellationToken).ConfigureAwait(false))
            {
                return cachedUri;
            }
        }

        // Fast path: Probe environment override or default port 8080 first (< 2ms) without spawning a child process
        var quickPorts = new List<int>();
        if (int.TryParse(Environment.GetEnvironmentVariable("ANTIGRAVITY_PORT"), out var envPort) && envPort is > 0 and <= 65535)
        {
            quickPorts.Add(envPort);
        }

        if (!quickPorts.Contains(8080))
        {
            quickPorts.Add(8080);
        }

        foreach (var port in quickPorts)
        {
            var uri = new Uri($"http://127.0.0.1:{port}/");
            if (await _dashboard.IsReachableAsync(uri, cancellationToken).ConfigureAwait(false))
            {
                _dashboardPort = port;
                _portCachedAt = DateTimeOffset.UtcNow;
                return uri;
            }
        }

        // Slow fallback: Only if 8080 and envPort are not reachable, ask switcher config for a custom port
        try
        {
            var result = await _cli.RunAsync(["config", "get", "port"], TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            var parsed = AccountParser.ParseConfigPort(result.StdOut);
            if (parsed is int customPort && !quickPorts.Contains(customPort))
            {
                var customUri = new Uri($"http://127.0.0.1:{customPort}/");
                if (await _dashboard.IsReachableAsync(customUri, cancellationToken).ConfigureAwait(false))
                {
                    _dashboardPort = customPort;
                    _portCachedAt = DateTimeOffset.UtcNow;
                    return customUri;
                }
            }
        }
        catch
        {
        }

        return null;
    }
    private Task<Uri?> ResolveDashboardUriAsync(CancellationToken cancellationToken) => ResolveApiUriInternalAsync(cancellationToken);
    private Task<Uri?> ResolveApiUriAsync(CancellationToken cancellationToken) => ResolveApiUriInternalAsync(cancellationToken);

    private static bool LooksUnknownCommand(string text) =>
        text.Contains("Unknown command", StringComparison.OrdinalIgnoreCase);
}

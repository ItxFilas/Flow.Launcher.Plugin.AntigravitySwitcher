using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Flow.Launcher.Plugin.AntigravitySwitcher.Models;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

public sealed class AccountService
{
    private readonly CliRunner _cli;
    private readonly DashboardClient _dashboard;
    private readonly AccountCache _cache;
    private int? _dashboardPort;
    private DateTimeOffset _portCachedAt;

    public AccountService(CliRunner cli, DashboardClient dashboard, AccountCache cache)
    {
        _cli = cli;
        _dashboard = dashboard;
        _cache = cache;
    }

    public bool TryGetCached(out IReadOnlyList<AccountInfo> accounts) => _cache.TryGet(out accounts);

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
                    ? "list-accounts selhal."
                    : result.Combined.Trim());
            }

            accounts = AccountParser.ParseListAccounts(result.StdOut);
        }

        _cache.Set(accounts);
        return accounts;
    }

    public async Task SwitchAsync(AccountInfo account, CancellationToken cancellationToken = default)
    {
        // Preferred path: the switcher performs account switching through its local dashboard
        // REST API (POST /api/accounts/{id}/select). The CLI has no 'switch' command as of v1.1.0.
        var baseUri = await ResolveDashboardUriAsync(cancellationToken).ConfigureAwait(false);
        var switched = baseUri is not null &&
                       await _dashboard.SelectAccountAsync(baseUri, account.Id, cancellationToken).ConfigureAwait(false);

        if (!switched)
        {
            // Fall back to a CLI 'switch' command for forward/backward compatibility.
            switched = await TryCliSwitchAsync(account, cancellationToken).ConfigureAwait(false);
        }

        if (!switched)
        {
            throw new InvalidOperationException(
                "Přepnutí účtu selhalo. Spusťte switcher jako daemon (`antigravity-account-switcher serve` nebo `launch`) a zkuste to znovu.");
        }

        _cache.Invalidate();
    }

    public async Task<AccountInfo> SwitchToNextAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await ListAccountsAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
        var next = SelectNextAvailable(accounts) ??
                   throw new InvalidOperationException("Není k dispozici žádný další účet s volnou kvótou.");

        await SwitchAsync(next, cancellationToken).ConfigureAwait(false);
        return next;
    }

    public void StartAddAccount()
    {
        _cli.StartDetached(["add-account"]);
        _cache.Invalidate();
    }

    public async Task RemoveAsync(AccountInfo account, CancellationToken cancellationToken = default)
    {
        // Preferred path: REST DELETE /api/accounts/{id} on the dashboard.
        var baseUri = await ResolveDashboardUriAsync(cancellationToken).ConfigureAwait(false);
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
            "Odebrání účtu selhalo. Spusťte switcher jako daemon (`serve` / `launch`) — REST DELETE je dostupný na /api/accounts/{id}.");
    }

    public async Task RefreshQuotasAsync(CancellationToken cancellationToken = default)
    {
        var baseUri = await ResolveDashboardUriAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task<Uri> OpenDashboardUriAsync(CancellationToken cancellationToken = default)
    {
        var uri = await ResolveDashboardUriAsync(cancellationToken).ConfigureAwait(false);
        return uri ?? new Uri($"http://127.0.0.1:{DefaultDashboardPort}/");
    }

    /// <summary>Built-in default port of the switcher's web dashboard.</summary>
    public const int DefaultDashboardPort = 8080;

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

    private async Task<bool> TryCliSwitchAsync(AccountInfo account, CancellationToken cancellationToken)
    {
        foreach (var identifier in new[] { account.Id, account.Email }.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            try
            {
                var result = await _cli.RunAsync(["switch", identifier], TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    return true;
                }

                if (!LooksUnknownCommand(result.Combined))
                {
                    return false;
                }
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<AccountInfo>?> TryListFromApiAsync(CancellationToken cancellationToken)
    {
        var baseUri = await ResolveDashboardUriAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<Uri?> ResolveDashboardUriAsync(CancellationToken cancellationToken)
    {
        if (_dashboardPort is int cached && DateTimeOffset.UtcNow - _portCachedAt < TimeSpan.FromSeconds(30))
        {
            var cachedUri = new Uri($"http://127.0.0.1:{cached}/");
            if (await _dashboard.IsReachableAsync(cachedUri, cancellationToken).ConfigureAwait(false))
            {
                return cachedUri;
            }
        }

        var ports = new List<int>();

        // Explicit override via environment (ANTIGRAVITY_PORT) takes precedence.
        if (int.TryParse(Environment.GetEnvironmentVariable("ANTIGRAVITY_PORT"), out var envPort) &&
            envPort is > 0 and <= 65535)
        {
            ports.Add(envPort);
        }

        try
        {
            var result = await _cli.RunAsync(["config", "get", "port"], TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
            var parsed = AccountParser.ParseConfigPort(result.StdOut);
            if (parsed is int port && !ports.Contains(port))
            {
                ports.Add(port);
            }
        }
        catch
        {
            // config may be unavailable; try known defaults
        }

        // 8080 is the switcher's built-in default; 1831 is a common documented example.
        foreach (var fallback in new[] { 8080, 1831 })
        {
            if (!ports.Contains(fallback))
            {
                ports.Add(fallback);
            }
        }

        foreach (var port in ports)
        {
            var uri = new Uri($"http://127.0.0.1:{port}/");
            if (await _dashboard.IsReachableAsync(uri, cancellationToken).ConfigureAwait(false))
            {
                _dashboardPort = port;
                _portCachedAt = DateTimeOffset.UtcNow;
                return uri;
            }
        }

        return null;
    }

    private static bool LooksUnknownCommand(string text) =>
        text.Contains("Unknown command", StringComparison.OrdinalIgnoreCase);
}

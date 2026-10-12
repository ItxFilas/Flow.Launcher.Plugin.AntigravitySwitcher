using System.Diagnostics;
using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;

namespace Flow.Launcher.Plugin.AntigravitySwitcher;

public sealed class Main : IPlugin, IContextMenu
{
    internal const string AppIcon = "Images\\app.png";
    internal const string NextIcon = "Images\\next.png";
    internal const string ActiveIcon = "Images\\active.png";
    internal const string StandbyIcon = "Images\\standby.png";
    internal const string ExhaustedIcon = "Images\\exhausted.png";
    internal const string AddIcon = "Images\\add.png";
    internal const string RemoveIcon = "Images\\remove.png";
    internal const string RefreshIcon = "Images\\refresh.png";
    internal const string WebIcon = "Images\\web.png";

    private PluginInitContext _context = null!;
    private AccountService _service = null!;
    private bool _refreshQueued;
    private string? _lastError;
    private DateTimeOffset _lastErrorAt;
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(15);

    public void Init(PluginInitContext context)
    {
        _context = context;
        var locator = new SwitcherLocator();
        var runner = new CliRunner(locator);
        _service = new AccountService(runner, new DashboardClient(), new AccountCache());
        _ = WarmCacheAsync();
    }

    public List<Result> Query(Query query)
    {
        var search = query.Search?.Trim() ?? string.Empty;
        var parts = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        var filter = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;

        try
        {
            return command switch
            {
                "add" => [AddAccountResult()],
                "remove" => QueryRemove(filter),
                "refresh" => [RefreshResult()],
                "web" or "dashboard" => [OpenDashboardResult()],
                "status" => QueryStatus(),
                "help" => HelpResults(),
                _ => QueryAccounts(search)
            };
        }
        catch (Exception ex)
        {
            return
            [
                ErrorResult("Plugin narazil na chybu", ex.Message)
            ];
        }
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        if (selectedResult.ContextData is not ResultContext { Account: { } account })
        {
            return [];
        }

        return
        [
            new Result
            {
                Title = "Odebrat tento účet",
                SubTitle = account.DisplayName,
                IcoPath = RemoveIcon,
                Score = 100,
                AsyncAction = async _ =>
                {
                    await RemoveWithConfirmAsync(account).ConfigureAwait(false);
                    return true;
                }
            },
            new Result
            {
                Title = "Zkopírovat email do schránky",
                SubTitle = account.Email,
                IcoPath = AppIcon,
                Score = 90,
                Action = _ =>
                {
                    _context.API.CopyToClipboard(account.Email, directCopy: false, showDefaultNotification: false);
                    _context.API.ShowMsg("Schránka", account.Email, AppIcon);
                    return false;
                }
            },
            new Result
            {
                Title = "Otevřít v prohlížeči",
                SubTitle = "Webový dashboard účtů",
                IcoPath = WebIcon,
                Score = 80,
                AsyncAction = async _ =>
                {
                    await OpenDashboardAsync().ConfigureAwait(false);
                    return true;
                }
            }
        ];
    }

    private List<Result> QueryAccounts(string filter)
    {
        var accounts = ListCachedOrSchedule(out var loading, out var error);
        if (error is not null)
        {
            return [ErrorResult("Nelze načíst účty", error)];
        }

        if (loading)
        {
            return
            [
                new Result
                {
                    Title = "Načítám účty…",
                    SubTitle = "Probíhá list-accounts / dashboard API",
                    IcoPath = AppIcon,
                    Score = 100
                }
            ];
        }

        var results = new List<Result>();
        var next = AccountService.SelectNextAvailable(accounts);
        if (string.IsNullOrWhiteSpace(filter) || Matches("rychlé přepnutí next", filter))
        {
            results.Add(new Result
            {
                Title = "Rychlé přepnutí na další účet",
                SubTitle = next is null
                    ? "Žádný další účet s volnou kvótou"
                    : $"Next available: {next.DisplayName} · kvóta {next.RemainingLabel}",
                IcoPath = NextIcon,
                Score = 200,
                ContextData = new ResultContext { Kind = ResultKind.NextSwitch, Account = next },
                AsyncAction = async _ =>
                {
                    try
                    {
                        var switched = await _service.SwitchToNextAsync().ConfigureAwait(false);
                        _context.API.ShowMsg("Účet přepnut", switched.DisplayName, AppIcon);
                        _context.API.ChangeQuery(_context.CurrentPluginMetadata.ActionKeyword + " ", requery: true);
                    }
                    catch (Exception ex)
                    {
                        _context.API.ShowMsgError("Přepnutí selhalo", ex.Message);
                    }

                    return false;
                }
            });
        }

        foreach (var account in accounts.Where(a => Matches(a.DisplayName + " " + a.Id, filter)))
        {
            var captured = account;
            var title = captured.IsActive ? $"{captured.DisplayName} [AKTIVNÍ]" : captured.DisplayName;
            results.Add(new Result
            {
                Title = title,
                SubTitle = captured.Subtitle,
                IcoPath = IconFor(captured),
                Score = captured.IsActive ? 180 : 100 + (int)Math.Clamp(captured.RemainingPercent, 0, 50),
                CopyText = captured.Email,
                ContextData = new ResultContext { Kind = ResultKind.Account, Account = captured },
                AsyncAction = async _ =>
                {
                    try
                    {
                        await _service.SwitchAsync(captured).ConfigureAwait(false);
                        _context.API.ShowMsg("Účet přepnut", captured.DisplayName, AppIcon);
                        _context.API.ChangeQuery(_context.CurrentPluginMetadata.ActionKeyword + " ", requery: true);
                    }
                    catch (Exception ex)
                    {
                        _context.API.ShowMsgError("Přepnutí selhalo", ex.Message);
                    }

                    return false;
                }
            });
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            results.AddRange(CommandHints());
        }

        if (results.Count == 0)
        {
            results.Add(new Result
            {
                Title = "Žádné účty",
                SubTitle = "Použijte ag add pro přidání Google účtu",
                IcoPath = AddIcon,
                Score = 10,
                Action = _ =>
                {
                    _service.StartAddAccount();
                    _context.API.ShowMsg("Přidání účtu", "OAuth flow byl spuštěn v prohlížeči.", AppIcon);
                    return true;
                }
            });
        }

        return results;
    }

    private List<Result> QueryRemove(string filter)
    {
        var accounts = ListCachedOrSchedule(out var loading, out var error);
        if (error is not null)
        {
            return [ErrorResult("Nelze načíst účty", error)];
        }

        if (loading)
        {
            return
            [
                new Result
                {
                    Title = "Načítám účty k odebrání…",
                    SubTitle = "Probíhá list-accounts / dashboard API",
                    IcoPath = RemoveIcon,
                    Score = 100
                }
            ];
        }

        var filtered = accounts.Where(a => Matches(a.DisplayName + " " + a.Id, filter)).ToList();
        if (filtered.Count == 0)
        {
            return
            [
                new Result
                {
                    Title = "Žádný účet k odebrání",
                    SubTitle = string.IsNullOrWhiteSpace(filter) ? "Seznam je prázdný" : $"Filtr „{filter}“ nic nenašel",
                    IcoPath = RemoveIcon,
                    Score = 50
                }
            ];
        }

        return filtered.Select(account =>
        {
            var captured = account;
            return new Result
            {
                Title = $"Odebrat {captured.DisplayName}",
                SubTitle = "Enter potvrdí odebrání účtu ze switcheru",
                IcoPath = RemoveIcon,
                Score = captured.IsActive ? 80 : 100,
                ContextData = new ResultContext { Kind = ResultKind.RemoveAccount, Account = captured },
                AsyncAction = async _ =>
                {
                    await RemoveWithConfirmAsync(captured).ConfigureAwait(false);
                    return false;
                }
            };
        }).ToList();
    }

    private List<Result> QueryStatus()
    {
        try
        {
            var status = _service.StatusAsync().GetAwaiter().GetResult();
            var first = status.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Status";
            return
            [
                new Result
                {
                    Title = first,
                    SubTitle = status.ReplaceLineEndings(" · "),
                    IcoPath = AppIcon,
                    Score = 100,
                    CopyText = status
                }
            ];
        }
        catch (Exception ex)
        {
            return [ErrorResult("Status selhal", ex.Message)];
        }
    }

    private Result AddAccountResult() => new()
    {
        Title = "Přidat Google účet",
        SubTitle = "Spustí antigravity-account-switcher add-account a otevře OAuth v prohlížeči",
        IcoPath = AddIcon,
        Score = 100,
        Action = _ =>
        {
            try
            {
                _service.StartAddAccount();
                _context.API.ShowMsg("Přidání účtu", "OAuth flow byl spuštěn. Dokončete přihlášení v prohlížeči.", AppIcon);
            }
            catch (Exception ex)
            {
                _context.API.ShowMsgError("add-account selhal", ex.Message);
            }

            return true;
        }
    };

    private Result RefreshResult() => new()
    {
        Title = "Obnovit kvóty",
        SubTitle = "Vynutí refresh-quotas / POST /api/quota/refresh",
        IcoPath = RefreshIcon,
        Score = 100,
        AsyncAction = async _ =>
        {
            try
            {
                await _service.RefreshQuotasAsync().ConfigureAwait(false);
                _context.API.ShowMsg("Kvóty", "Aktualizace dokončena.", AppIcon);
                _context.API.ChangeQuery(_context.CurrentPluginMetadata.ActionKeyword + " ", requery: true);
            }
            catch (Exception ex)
            {
                _context.API.ShowMsgError("Obnova kvót selhala", ex.Message);
            }

            return false;
        }
    };

    private Result OpenDashboardResult() => new()
    {
        Title = "Otevřít webový dashboard",
        SubTitle = $"Webové rozhraní switcheru (výchozí http://127.0.0.1:{AccountService.DefaultDashboardPort})",
        IcoPath = WebIcon,
        Score = 100,
        AsyncAction = async _ =>
        {
            await OpenDashboardAsync().ConfigureAwait(false);
            return true;
        }
    };

    private List<Result> CommandHints() =>
    [
        Hint("ag add", "Přidat účet (OAuth)", AddIcon),
        Hint("ag remove", "Odebrat účet", RemoveIcon),
        Hint("ag refresh", "Obnovit kvóty", RefreshIcon),
        Hint("ag web", "Otevřít dashboard", WebIcon)
    ];

    private List<Result> HelpResults() =>
    [
        Hint("ag", "Seznam účtů a rychlé přepnutí", AppIcon),
        Hint("ag add", "Přidat Google účet", AddIcon),
        Hint("ag remove [filtr]", "Odebrat účet", RemoveIcon),
        Hint("ag refresh", "Obnovit kvóty", RefreshIcon),
        Hint("ag web", "Otevřít dashboard", WebIcon),
        Hint("ag status", "Stav switcheru", AppIcon)
    ];

    private Result Hint(string title, string subtitle, string icon) => new()
    {
        Title = title,
        SubTitle = subtitle,
        IcoPath = icon,
        Score = 20,
        Action = _ =>
        {
            var keyword = _context.CurrentPluginMetadata.ActionKeyword;
            var query = title.StartsWith("ag ", StringComparison.OrdinalIgnoreCase)
                ? title[3..]
                : string.Empty;
            _context.API.ChangeQuery($"{keyword} {query}".TrimEnd(), requery: true);
            return false;
        }
    };

    private Result ErrorResult(string title, string subtitle) => new()
    {
        Title = title,
        SubTitle = subtitle,
        IcoPath = AppIcon,
        Score = 0
    };

    private IReadOnlyList<AccountInfo> ListCachedOrSchedule(out bool loading, out string? error)
    {
        error = null;
        if (_service.TryGetCached(out var cached))
        {
            loading = false;
            return cached;
        }

        // Surface the last failure instead of spinning on "loading" forever, and retry in
        // the background once the cooldown has elapsed.
        if (_lastError is not null && !_refreshQueued)
        {
            error = _lastError;
            loading = false;

            if (DateTimeOffset.UtcNow - _lastErrorAt >= RetryCooldown)
            {
                _refreshQueued = true;
                _ = WarmCacheAsync();
            }

            return [];
        }

        loading = true;
        if (!_refreshQueued)
        {
            _refreshQueued = true;
            _ = WarmCacheAsync();
        }

        return [];
    }

    private async Task WarmCacheAsync()
    {
        try
        {
            await _service.ListAccountsAsync(forceRefresh: true).ConfigureAwait(false);
            _lastError = null;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _lastErrorAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _refreshQueued = false;
            try
            {
                _context.API.ReQuery(reselect: false);
            }
            catch
            {
                // ReQuery can throw while the launcher is shutting down; ignore.
            }
        }
    }

    private async Task OpenDashboardAsync()
    {
        try
        {
            var uri = await _service.OpenDashboardUriAsync().ConfigureAwait(false);
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _context.API.ShowMsgError("Dashboard", ex.Message);
        }
    }

    private async Task RemoveWithConfirmAsync(AccountInfo account)
    {
        var answer = _context.API.ShowMsgBox(
            $"Opravdu odebrat účet {account.DisplayName}?",
            "Odebrat účet");

        if (answer.ToString() is not ("Yes" or "OK"))
        {
            return;
        }

        try
        {
            await _service.RemoveAsync(account).ConfigureAwait(false);
            _context.API.ShowMsg("Účet odebrán", account.DisplayName, AppIcon);
            _context.API.ChangeQuery(_context.CurrentPluginMetadata.ActionKeyword + " ", requery: true);
        }
        catch (Exception ex)
        {
            _context.API.ShowMsgError("Odebrání selhalo", ex.Message);
        }
    }

    private static string IconFor(AccountInfo account) =>
        account.IsActive ? ActiveIcon
        : string.Equals(account.Status, "exhausted", StringComparison.OrdinalIgnoreCase) ? ExhaustedIcon
        : StandbyIcon;

    private static bool Matches(string haystack, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return haystack.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}

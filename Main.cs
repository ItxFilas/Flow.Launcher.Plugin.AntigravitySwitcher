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


    private PluginInitContext _context = null!;
    private AccountService _service = null!;
    private bool _refreshQueued;
    private string? _lastError;
    private DateTimeOffset _lastErrorAt;
    private int _switchInProgress;
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(15);

    public void Init(PluginInitContext context)
    {
        _context = context;
        var locator = new SwitcherLocator();
        var runner = new CliRunner(locator);
        _service = new AccountService(runner, new DashboardClient(), new AccountCache());
        _ = WarmCacheAsync();
    }

    /// <summary>
    /// Resolves a plugin-relative image to an absolute path. Flow Launcher's Windows toast
    /// notification requires an absolute icon URI — a relative path makes `new Uri()` throw
    /// and the API silently falls back to its animated legacy popup.
    /// </summary>
    private string NotifyIconPath(string relativePath) =>
        System.IO.Path.Combine(_context.CurrentPluginMetadata.PluginDirectory, relativePath);

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

                "status" => QueryStatus(),
                "help" => HelpResults(),
                _ => QueryAccounts(search)
            };
        }
        catch (Exception ex)
        {
            return
            [
                ErrorResult("Plugin error", ex.Message)
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
                Title = "Sign into Antigravity as this account",
                SubTitle = "Swaps the IDE login without closing the app",
                IcoPath = NextIcon,
                Score = 100,
                AsyncAction = async _ =>
                {
                    return TryStartSwitch(account);
                }
            },
            new Result
            {
                Title = "Remove this account",
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
                Title = "Copy email to clipboard",
                SubTitle = account.Email,
                IcoPath = AppIcon,
                Score = 90,
                Action = _ =>
                {
                    _context.API.CopyToClipboard(account.Email, directCopy: false, showDefaultNotification: false);
                    _context.API.ShowMsg("Clipboard", account.Email, NotifyIconPath(AppIcon));
                    return false;
                }
            },

        ];
    }

    private List<Result> QueryAccounts(string filter)
    {
        var accounts = ListCachedOrSchedule(out var loading, out var error);
        if (error is not null)
        {
            return [ErrorResult("Cannot load accounts", error)];
        }

        if (loading)
        {
            return
            [
                new Result
                {
                    Title = "Loading accounts…",
                    SubTitle = "Fetching list-accounts / dashboard API",
                    IcoPath = AppIcon,
                    Score = 100
                }
            ];
        }

        var results = new List<Result>();
        var next = AccountService.SelectNextAvailable(accounts);
        if (string.IsNullOrWhiteSpace(filter) || Matches("switch next", filter))
        {
            results.Add(new Result
            {
                Title = "Switch to next account",
                SubTitle = next is null
                    ? "No other account with free quota"
                    : $"Signs into Antigravity as {next.DisplayName} · 5hr {next.FiveHourLabel} · week {next.WeeklyLabel}",
                IcoPath = NextIcon,
                Score = 200,
                ContextData = new ResultContext { Kind = ResultKind.NextSwitch, Account = next },
                AsyncAction = async _ =>
                {
                    return next is not null && TryStartSwitch(next);
                }
            });
        }

        foreach (var account in accounts.Where(a => Matches(a.DisplayName + " " + a.Id, filter)))
        {
            var captured = account;
            var title = captured.IsActive ? $"{captured.DisplayName} [ACTIVE]" : captured.DisplayName;
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
                    return TryStartSwitch(captured);
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
                Title = "No accounts",
                SubTitle = "Use 'ag add' to add a Google account",
                IcoPath = AddIcon,
                Score = 10,
                Action = _ =>
                {
                    _service.StartAddAccount();
                    _context.API.ShowMsg("Add account", "OAuth flow has been started in your browser.", NotifyIconPath(AppIcon));
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
            return [ErrorResult("Cannot load accounts", error)];
        }

        if (loading)
        {
            return
            [
                new Result
                {
                    Title = "Loading accounts to remove…",
                    SubTitle = "Fetching list-accounts / dashboard API",
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
                    Title = "No account to remove",
                    SubTitle = string.IsNullOrWhiteSpace(filter) ? "The list is empty" : $"Filter \"{filter}\" matched nothing",
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
                Title = $"Remove {captured.DisplayName}",
                SubTitle = "Press Enter to confirm removing this account from the switcher",
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
            return [ErrorResult("Status failed", ex.Message)];
        }
    }

    private Result AddAccountResult() => new()
    {
        Title = "Add Google account",
        SubTitle = "Runs antigravity-account-switcher add-account and opens OAuth in your browser",
        IcoPath = AddIcon,
        Score = 100,
        Action = _ =>
        {
            try
            {
                _service.StartAddAccount();
                _context.API.ShowMsg("Add account", "OAuth flow started. Complete the sign-in in your browser.", NotifyIconPath(AppIcon));
            }
            catch (Exception ex)
            {
                _context.API.ShowMsgError("add-account failed", ex.Message);
            }

            return true;
        }
    };

    private Result RefreshResult() => new()
    {
        Title = "Refresh quotas",
        SubTitle = "Forces refresh-quotas / POST /api/quota/refresh",
        IcoPath = RefreshIcon,
        Score = 100,
        AsyncAction = async _ =>
        {
            try
            {
                await _service.RefreshQuotasAsync().ConfigureAwait(false);
                _context.API.ShowMsg("Quotas", "Refresh complete.", NotifyIconPath(AppIcon));
                _context.API.ChangeQuery(_context.CurrentPluginMetadata.ActionKeyword + " ", requery: true);
            }
            catch (Exception ex)
            {
                _context.API.ShowMsgError("Refresh failed", ex.Message);
            }

            return false;
        }
    };



    private List<Result> CommandHints() =>
    [
        Hint("ag add", "Add account (OAuth)", AddIcon),
        Hint("ag remove", "Remove account", RemoveIcon),
        Hint("ag refresh", "Refresh quotas", RefreshIcon)
    ];

    private List<Result> HelpResults() =>
    [
        Hint("ag", "List accounts and quick switch", AppIcon),
        Hint("ag add", "Add Google account", AddIcon),
        Hint("ag remove [filter]", "Remove account", RemoveIcon),
        Hint("ag refresh", "Refresh quotas", RefreshIcon),
        Hint("ag status", "Switcher status", AppIcon)
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

    /// <summary>
    /// Kicks off a switch in the background so Flow Launcher hides instantly — the credential
    /// swap and the app-side reload happen off-screen. The outcome is reported with the same
    /// notification style the NoSleep plugin uses (title = plugin name, terse status message).
    /// </summary>
    private bool TryStartSwitch(AccountInfo account)
    {
        if (Interlocked.Exchange(ref _switchInProgress, 1) == 1)
        {
            return true; // another switch is already running — drop the duplicate Enter
        }

        if (account.IsActive)
        {
            Interlocked.Exchange(ref _switchInProgress, 0);
            _context.API.ShowMsg("Antigravity Switcher", $"Already signed in as {account.DisplayName}", NotifyIconPath(AppIcon));
            return true; // nothing to do; close Flow Launcher
        }

        _ = SwitchAndNotifyAsync(account);
        return true; // hide Flow Launcher immediately
    }

    private async Task SwitchAndNotifyAsync(AccountInfo account)
    {
        try
        {
            await _service.SwitchAsync(account).ConfigureAwait(false);
            _context.API.ShowMsg("Antigravity Switcher", $"Switched to {account.DisplayName}", NotifyIconPath(AppIcon));
        }
        catch (Exception ex)
        {
            _context.API.ShowMsgError("Antigravity Switcher", ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _switchInProgress, 0);
        }
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



    private async Task RemoveWithConfirmAsync(AccountInfo account)
    {
        var answer = _context.API.ShowMsgBox(
            $"Really remove account {account.DisplayName}?",
            "Remove account");

        if (answer.ToString() is not ("Yes" or "OK"))
        {
            return;
        }

        try
        {
            await _service.RemoveAsync(account).ConfigureAwait(false);
            _context.API.ShowMsg("Account removed", account.DisplayName, NotifyIconPath(AppIcon));
            _context.API.ChangeQuery(_context.CurrentPluginMetadata.ActionKeyword + " ", requery: true);
        }
        catch (Exception ex)
        {
            _context.API.ShowMsgError("Removal failed", ex.Message);
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

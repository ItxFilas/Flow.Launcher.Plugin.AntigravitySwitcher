using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Flow.Launcher.Plugin.AntigravitySwitcher.UI;
using Flow.Launcher.Plugin.SharedModels;
using System.Text.RegularExpressions;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Routing;

public enum CommandKind
{
    ListAccounts,
    Add,
    Remove,
    Refresh,
    Status,
    Restart,
    Auto,
    Help
}

public record ParsedCommand(CommandKind Kind, string Argument);

public sealed class QueryRouter
{
    private readonly AccountService _service;
    private readonly PluginInitContext? _context;
    private int _switchInProgress;
    private int _isRefreshing;
    private DateTimeOffset _lastBackgroundRefresh = DateTimeOffset.MinValue;
    private static readonly TimeSpan MinBackgroundRefreshInterval = TimeSpan.FromSeconds(2);

    public QueryRouter(AccountService service, PluginInitContext? context = null)
    {
        _service = service;
        _context = context;
    }

    public static ParsedCommand ParseCommand(string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return new ParsedCommand(CommandKind.ListAccounts, string.Empty);
        }

        var trimmed = queryText.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        var commandWord = firstSpace < 0 ? trimmed : trimmed[..firstSpace];
        var argument = firstSpace < 0 ? string.Empty : trimmed[(firstSpace + 1)..].Trim();

        return commandWord.ToLowerInvariant() switch
        {
            "add" => new ParsedCommand(CommandKind.Add, argument),
            "remove" => new ParsedCommand(CommandKind.Remove, argument),
            "refresh" => new ParsedCommand(CommandKind.Refresh, argument),
            "status" => new ParsedCommand(CommandKind.Status, argument),
            "restart" => new ParsedCommand(CommandKind.Restart, argument),
            "auto" or "autoswitch" => new ParsedCommand(CommandKind.Auto, argument),
            "help" => new ParsedCommand(CommandKind.Help, argument),
            _ => new ParsedCommand(CommandKind.ListAccounts, trimmed)
        };
    }

    public Task<List<Result>> RouteAsync(Query query, CancellationToken token = default) =>
        RouteAsync(query?.Search ?? string.Empty, token);

    public async Task<List<Result>> RouteAsync(string queryText, CancellationToken token = default)
    {
        var search = queryText?.Trim() ?? string.Empty;
        var parsed = ParseCommand(search);

        try
        {
            return parsed.Kind switch
            {
                CommandKind.Add => [AddAccountResult()],
                CommandKind.Remove => await QueryRemoveAsync(parsed.Argument, token).ConfigureAwait(false),
                CommandKind.Refresh => [RefreshResult()],
                CommandKind.Restart => RestartResults(parsed.Argument),
                CommandKind.Status => await QueryStatusAsync(token).ConfigureAwait(false),
                CommandKind.Auto => AutoSwitchResults(),
                CommandKind.Help => HelpResults(),
                _ => await QueryAccountsAsync(parsed.Argument, token).ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            return [ResultFactory.CreateErrorResult("Plugin error", ex.Message)];
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
                IcoPath = ResultFactory.NextIcon,
                Score = 100,
                AsyncAction = async _ => TryStartSwitch(account)
            },
            new Result
            {
                Title = "Remove this account",
                SubTitle = account.DisplayName,
                IcoPath = ResultFactory.RemoveIcon,
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
                IcoPath = ResultFactory.AppIcon,
                Score = 90,
                Action = _ =>
                {
                    _context?.API?.CopyToClipboard(account.Email, directCopy: false, showDefaultNotification: false);
                    _context?.API?.ShowMsg("Clipboard", account.Email, NotifyIconPath(ResultFactory.AppIcon));
                    return false;
                }
            }
        ];
    }

    private void TriggerBackgroundQuotaReload()
    {
        if (DateTimeOffset.UtcNow - _lastBackgroundRefresh < MinBackgroundRefreshInterval)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) != 0)
        {
            return;
        }

        _lastBackgroundRefresh = DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                await _service.RefreshQuotasAsync(CancellationToken.None).ConfigureAwait(false);
                await _service.ListAccountsAsync(forceRefresh: true, CancellationToken.None).ConfigureAwait(false);
                _context?.API?.ReQuery(reselect: false);
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref _isRefreshing, 0);
            }
        }, CancellationToken.None);
    }

    private async Task<List<Result>> QueryAccountsAsync(string filter, CancellationToken token)
    {
        IReadOnlyList<AccountInfo> accounts;
        try
        {
            if (_service.TryGetCached(out var cached, out var isStale) && cached.Count > 0)
            {
                accounts = cached;
                if (string.IsNullOrWhiteSpace(filter) || isStale)
                {
                    TriggerBackgroundQuotaReload();
                }
            }
            else
            {
                accounts = await _service.ListAccountsAsync(forceRefresh: false, token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(filter))
                {
                    TriggerBackgroundQuotaReload();
                }
            }
        }
        catch (Exception ex)
        {
            return [ResultFactory.CreateErrorResult("Cannot load accounts", ex.Message)];
        }

        var results = new List<Result>();
        var next = AccountService.SelectNextAvailable(accounts);

        var maxVisualWidth = accounts.Count > 0
            ? accounts.Max(a => AccountFormatter.ApproximateVisualWidth(a.DisplayName))
            : 0;

        var sortedAccounts = accounts
            .OrderByDescending(a => a.IsActive)
            .ThenByDescending(a => a.RemainingPercent)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(filter))
        {
            results.Add(ResultFactory.CreateNextSwitchResult(
                next,
                async _ => next is not null && TryStartSwitch(next)));

            foreach (var account in sortedAccounts)
            {
                var captured = account;
                results.Add(ResultFactory.CreateAccountResult(
                    captured,
                    async _ => TryStartSwitch(captured),
                    targetVisualWidth: maxVisualWidth));
            }

            results.AddRange(CommandHints());

            if (accounts.Count == 0)
            {
                results.Add(ResultFactory.CreateEmptyResult(
                    "No accounts",
                    "Use 'ag add' to add a Google account",
                    ResultFactory.AddIcon,
                    10,
                    _ =>
                    {
                        StartAddAccount();
                        return true;
                    }));
            }
        }
        else
        {
            var nextMatch = StringMatcher.FuzzySearch(filter, "switch next");
            if (nextMatch.Success)
            {
                results.Add(ResultFactory.CreateNextSwitchResult(
                    next,
                    async _ => next is not null && TryStartSwitch(next),
                    score: 200 + nextMatch.Score));
            }

            var matchedAccounts = new List<(AccountInfo Account, int Score)>();
            foreach (var account in accounts)
            {
                var matchDisplay = StringMatcher.FuzzySearch(filter, account.DisplayName);
                var matchId = string.IsNullOrEmpty(account.Id)
                    ? new MatchResult(false, SearchPrecisionScore.None)
                    : StringMatcher.FuzzySearch(filter, account.Id);

                if (matchDisplay.Success || matchId.Success)
                {
                    var matchScore = Math.Max(matchDisplay.Score, matchId.Score);
                    var baseScore = account.IsActive ? 180 : 100 + (int)Math.Clamp(account.RemainingPercent, 0, 50);
                    matchedAccounts.Add((account, baseScore + matchScore));
                }
            }

            foreach (var (account, score) in matchedAccounts.OrderByDescending(m => m.Score))
            {
                var captured = account;
                results.Add(ResultFactory.CreateAccountResult(
                    captured,
                    async _ => TryStartSwitch(captured),
                    scoreOverride: score,
                    targetVisualWidth: maxVisualWidth));
            }

            if (results.Count == 0)
            {
                results.Add(ResultFactory.CreateEmptyResult(
                    "No matching accounts",
                    $"No account matched \"{filter}\"",
                    ResultFactory.AppIcon,
                    10));
            }
        }

        return results;
    }

    private async Task<List<Result>> QueryRemoveAsync(string filter, CancellationToken token)
    {
        IReadOnlyList<AccountInfo> accounts;
        try
        {
            accounts = await _service.ListAccountsAsync(forceRefresh: false, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return [ResultFactory.CreateErrorResult("Cannot load accounts", ex.Message)];
        }

        var candidates = accounts
            .Select(a =>
            {
                var baseScore = a.IsActive ? 80 : 100;
                if (string.IsNullOrWhiteSpace(filter))
                {
                    return (Account: a, Success: true, Score: baseScore);
                }

                var m1 = StringMatcher.FuzzySearch(filter, a.DisplayName);
                var m2 = string.IsNullOrEmpty(a.Id)
                    ? new MatchResult(false, SearchPrecisionScore.None)
                    : StringMatcher.FuzzySearch(filter, a.Id);
                var success = m1.Success || m2.Success;
                var matchScore = Math.Max(m1.Score, m2.Score);
                return (Account: a, Success: success, Score: baseScore + matchScore);
            })
            .Where(x => x.Success)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (candidates.Count == 0)
        {
            return
            [
                ResultFactory.CreateEmptyResult(
                    "No account to remove",
                    string.IsNullOrWhiteSpace(filter) ? "The list is empty" : $"Filter \"{filter}\" matched nothing",
                    ResultFactory.RemoveIcon,
                    50)
            ];
        }

        return candidates.Select(c =>
        {
            var captured = c.Account;
            return ResultFactory.CreateRemoveAccountResult(
                captured,
                async _ =>
                {
                    await RemoveWithConfirmAsync(captured).ConfigureAwait(false);
                    return false;
                },
                scoreOverride: c.Score);
        }).ToList();
    }

    private async Task<List<Result>> QueryStatusAsync(CancellationToken token)
    {
        try
        {
            var status = await _service.StatusAsync(token).ConfigureAwait(false);
            var autoStatus = AntigravityAppController.IsAutoSwitchEnabled() ? "Auto-Switch: 1 (ON)" : "Auto-Switch: 0 (OFF)";
            var combinedStatus = string.IsNullOrWhiteSpace(status) ? autoStatus : $"{status}\n{autoStatus}";
            return [ResultFactory.CreateStatusResult(combinedStatus)];
        }
        catch (Exception ex)
        {
            return [ResultFactory.CreateErrorResult("Status failed", ex.Message)];
        }
    }

    private List<Result> AutoSwitchResults()
    {
        var isEnabled = AntigravityAppController.IsAutoSwitchEnabled();
        var nextState = !isEnabled;
        var currentVal = isEnabled ? "1 (ON)" : "0 (OFF)";
        var nextVal = nextState ? "1 (ON)" : "0 (OFF)";

        return
        [
            new Result
            {
                Title = $"Auto-Switch: {currentVal}",
                SubTitle = $"Press Enter to switch to {nextVal}",
                IcoPath = NotifyIconPath(ResultFactory.AppIcon),
                Action = _ =>
                {
                    AntigravityAppController.SetAutoSwitch(nextState);
                    _context?.API?.ShowMsg(
                        "Antigravity Switcher",
                        $"Auto-Switch: {(nextState ? "1" : "0")}",
                        NotifyIconPath(ResultFactory.AppIcon));
                    return true;
                }
            }
        ];
    }

    private Result AddAccountResult() =>
        ResultFactory.CreateAddAccountResult(_ =>
        {
            StartAddAccount();
            return true;
        });

    private Result RefreshResult() =>
        ResultFactory.CreateRefreshResult(async _ =>
        {
            try
            {
                await _service.RefreshQuotasAsync(CancellationToken.None).ConfigureAwait(false);
                _context?.API?.ShowMsg("Quotas", "Refresh complete.", NotifyIconPath(ResultFactory.AppIcon));
                var keyword = _context?.CurrentPluginMetadata?.ActionKeyword ?? "ag";
                _context?.API?.ChangeQuery($"{keyword} ", requery: true);
            }
            catch (Exception ex)
            {
                _context?.API?.ShowMsgError("Refresh failed", ex.Message);
            }

            return false;
        });

    private List<Result> RestartResults(string argument)
    {
        var arg = argument?.Trim().ToLowerInvariant() ?? string.Empty;
        if (arg is "app" or "full" or "ide" or "window")
        {
            return [RestartAppResult(100), RestartBackendResult(80)];
        }

        if (arg is "backend" or "server" or "fast" or "lang" or "lsp")
        {
            return [RestartBackendResult(100), RestartAppResult(80)];
        }

        return [RestartBackendResult(100), RestartAppResult(90)];
    }

    private Result RestartBackendResult(int score = 100) =>
        ResultFactory.CreateRestartResult(
            title: "Restart Antigravity Backend (Fast)",
            subtitle: "Instant backend reload (<30ms) to reload indexes and credentials without closing windows",
            onRestart: async _ =>
            {
                try
                {
                    var restarted = await _service.RestartBackendOnlyAsync(CancellationToken.None).ConfigureAwait(false);
                    if (restarted)
                    {
                        _context?.API?.ShowMsg("Antigravity Restart", "Language server backend restarted instantly.", NotifyIconPath(ResultFactory.RestartIcon));
                    }
                    else
                    {
                        _context?.API?.ShowMsgError("Restart failed", "Could not restart Antigravity language server backend.");
                    }
                }
                catch (Exception ex)
                {
                    _context?.API?.ShowMsgError("Restart failed", ex.Message);
                }

                return false;
            },
            score: score);

    private Result RestartAppResult(int score = 90) =>
        ResultFactory.CreateRestartResult(
            title: "Restart Antigravity IDE (Full)",
            subtitle: "Clean process relaunch (~1s) with zero single-instance lock contention",
            onRestart: async _ =>
            {
                try
                {
                    var restarted = await _service.RestartAppAsync(CancellationToken.None).ConfigureAwait(false);
                    if (restarted)
                    {
                        _context?.API?.ShowMsg("Antigravity Restart", "Antigravity IDE relaunched successfully.", NotifyIconPath(ResultFactory.RestartIcon));
                    }
                    else
                    {
                        _context?.API?.ShowMsgError("Restart failed", "Could not relaunch Antigravity IDE.");
                    }
                }
                catch (Exception ex)
                {
                    _context?.API?.ShowMsgError("Restart failed", ex.Message);
                }

                return false;
            },
            score: score);

    private Result RestartResult() => RestartBackendResult();

    private List<Result>? _commandHints;
    private List<Result>? _helpResults;

    private List<Result> CommandHints() => _commandHints ??=
    [
        Hint("ag add", "Add account (OAuth)", ResultFactory.AddIcon),
        Hint("ag remove", "Remove account", ResultFactory.RemoveIcon),
        Hint("ag refresh", "Refresh quotas", ResultFactory.RefreshIcon),
        Hint("ag restart", "Restart backend or IDE", ResultFactory.RestartIcon),
        Hint("ag auto", "Auto-switch failover (0/1)", ResultFactory.AppIcon)
    ];

    private List<Result> HelpResults() => _helpResults ??=
    [
        Hint("ag", "List accounts and quick switch", ResultFactory.AppIcon),
        Hint("ag add", "Add Google account", ResultFactory.AddIcon),
        Hint("ag remove [filter]", "Remove account", ResultFactory.RemoveIcon),
        Hint("ag refresh", "Refresh quotas", ResultFactory.RefreshIcon),
        Hint("ag restart", "Restart backend or IDE", ResultFactory.RestartIcon),
        Hint("ag auto", "Toggle 429 auto-failover (0=OFF, 1=ON)", ResultFactory.AppIcon),
        Hint("ag status", "Switcher status", ResultFactory.AppIcon)
    ];

    private Result Hint(string title, string subtitle, string icon) =>
        ResultFactory.CreateCommandHintResult(title, subtitle, icon, _ =>
        {
            var keyword = _context?.CurrentPluginMetadata?.ActionKeyword ?? "ag";
            var cleanTitle = Regex.Replace(title, @"\[.*\]", string.Empty);
            var query = cleanTitle.StartsWith("ag ", StringComparison.OrdinalIgnoreCase)
                ? cleanTitle[3..].TrimStart()
                : string.Empty;
            var targetQuery = string.IsNullOrEmpty(query)
                ? $"{keyword} "
                : $"{keyword} {query}";
            _context?.API?.ChangeQuery(targetQuery, requery: true);
            return false;
        });

    private void StartAddAccount()
    {
        try
        {
            _service.StartAddAccount();
            _context?.API?.ShowMsg("Add account", "OAuth flow started. Complete the sign-in in your browser.", NotifyIconPath(ResultFactory.AppIcon));
        }
        catch (Exception ex)
        {
            _context?.API?.ShowMsgError("add-account failed", ex.Message);
        }
    }

    private bool TryStartSwitch(AccountInfo account)
    {
        if (Interlocked.Exchange(ref _switchInProgress, 1) == 1)
        {
            return true;
        }

        if (account.IsActive)
        {
            Interlocked.Exchange(ref _switchInProgress, 0);
            _context?.API?.ShowMsg("Antigravity Switcher", $"Already signed in as {account.DisplayName}", NotifyIconPath(ResultFactory.AppIcon));
            return true;
        }

        _context?.API?.ShowMsg("Antigravity Switcher", $"Switched to {account.DisplayName}", NotifyIconPath(ResultFactory.AppIcon));
        _ = SwitchAndNotifyAsync(account);
        return true;
    }

    private async Task SwitchAndNotifyAsync(AccountInfo account)
    {
        try
        {
            await _service.SwitchAsync(account).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _context?.API?.ShowMsgError("Antigravity Switcher", ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _switchInProgress, 0);
        }
    }

    private async Task RemoveWithConfirmAsync(AccountInfo account)
    {
        var answer = _context?.API?.ShowMsgBox(
            $"Really remove account {account.DisplayName}?",
            "Remove account",
            System.Windows.MessageBoxButton.OKCancel);

        if (answer is null || answer.ToString() is not ("Yes" or "OK"))
        {
            return;
        }

        try
        {
            await _service.RemoveAsync(account).ConfigureAwait(false);
            _context?.API?.ShowMsg("Account removed", account.DisplayName, NotifyIconPath(ResultFactory.RemoveIcon));
            var keyword = _context?.CurrentPluginMetadata?.ActionKeyword ?? "ag";
            _context?.API?.ChangeQuery($"{keyword} ", requery: true);
        }
        catch (Exception ex)
        {
            _context?.API?.ShowMsgError("Removal failed", ex.Message);
        }
    }

    private string NotifyIconPath(string relativePath)
    {
        var dir = _context?.CurrentPluginMetadata?.PluginDirectory;
        return string.IsNullOrEmpty(dir)
            ? relativePath
            : System.IO.Path.Combine(dir, relativePath);
    }
}

public static class StringMatcher
{
    private static readonly List<int> EmptyMatchData = [];

    public static MatchResult FuzzySearch(string query, string stringToCompare)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new MatchResult(true, SearchPrecisionScore.None, EmptyMatchData, 100);
        }

        if (string.IsNullOrEmpty(stringToCompare))
        {
            return new MatchResult(false, SearchPrecisionScore.None);
        }

        var q = query.Trim();
        var s = stringToCompare.Trim();

        // Exact match
        if (string.Equals(s, q, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchResult(true, SearchPrecisionScore.None, EmptyMatchData, 200);
        }

        // Starts with match
        if (s.StartsWith(q, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchResult(true, SearchPrecisionScore.None, EmptyMatchData, 160);
        }

        // Substring match
        var index = s.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var score = 120 - Math.Min(30, index * 2);
            return new MatchResult(true, SearchPrecisionScore.None, EmptyMatchData, Math.Max(score, 70));
        }

        // Subsequence / fuzzy match
        var qIdx = 0;
        var sIdx = 0;
        var scoreAcc = 0;
        var consecutive = 0;

        while (qIdx < q.Length && sIdx < s.Length)
        {
            if (char.ToLowerInvariant(q[qIdx]) == char.ToLowerInvariant(s[sIdx]))
            {
                var isBoundary = sIdx == 0 ||
                                 !char.IsLetterOrDigit(s[sIdx - 1]) ||
                                 (char.IsUpper(s[sIdx]) && !char.IsUpper(s[sIdx - 1]));

                var charScore = 10;
                if (isBoundary) charScore += 25;
                if (consecutive > 0) charScore += consecutive * 15;

                scoreAcc += charScore;
                consecutive++;
                qIdx++;
            }
            else
            {
                consecutive = 0;
            }
            sIdx++;
        }

        if (qIdx == q.Length)
        {
            var finalScore = Math.Clamp(scoreAcc / 2, 20, 110);
            return new MatchResult(true, SearchPrecisionScore.None, EmptyMatchData, finalScore);
        }

        return new MatchResult(false, SearchPrecisionScore.None);
    }
}

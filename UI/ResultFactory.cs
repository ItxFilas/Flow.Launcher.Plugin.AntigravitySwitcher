using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.UI;

public static class ResultFactory
{
    public const string AppIcon = "Images/app.png";
    public const string NextIcon = "Images/next.png";
    public const string ActiveIcon = "Images/active.png";
    public const string StandbyIcon = "Images/standby.png";
    public const string ExhaustedIcon = "Images/exhausted.png";
    public const string AddIcon = "Images/add.png";
    public const string RemoveIcon = "Images/remove.png";
    public const string RefreshIcon = "Images/refresh.png";
    public const string RestartIcon = "Images/restart.png";

    public static string IconFor(AccountInfo account) =>
        account.IsActive ? ActiveIcon
        : string.Equals(account.Status, "exhausted", StringComparison.OrdinalIgnoreCase) ? ExhaustedIcon
        : StandbyIcon;

    public static Result CreateAccountResult(
        AccountInfo account,
        Func<ActionContext, ValueTask<bool>>? onSelect = null,
        int? scoreOverride = null,
        double targetVisualWidth = 0)
    {
        return new Result
        {
            Title = AccountFormatter.FormatTitle(account, targetVisualWidth),
            SubTitle = account.Subtitle,
            IcoPath = IconFor(account),
            Score = scoreOverride ?? (account.IsActive ? 180 : 100 + (int)Math.Clamp(account.RemainingPercent, 0, 50)),
            CopyText = account.Email,
            ContextData = new ResultContext { Kind = ResultKind.Account, Account = account },
            AsyncAction = onSelect
        };
    }

    public static Result CreateNextSwitchResult(
        AccountInfo? nextAccount,
        Func<ActionContext, ValueTask<bool>>? onSelect = null,
        int score = 200)
    {
        return new Result
        {
            Title = "Switch to next account",
            SubTitle = nextAccount is null
                ? "No other account with free quota"
                : $"Signs into Antigravity as {nextAccount.DisplayName} · 5hr {nextAccount.FiveHourLabel} · week {nextAccount.WeeklyLabel}",
            IcoPath = NextIcon,
            Score = score,
            ContextData = new ResultContext { Kind = ResultKind.NextSwitch, Account = nextAccount },
            AsyncAction = onSelect
        };
    }

    public static Result CreateRemoveAccountResult(
        AccountInfo account,
        Func<ActionContext, ValueTask<bool>>? onRemove = null,
        int? scoreOverride = null)
    {
        return new Result
        {
            Title = $"Remove {account.DisplayName}",
            SubTitle = "Press Enter to confirm removing this account from the switcher",
            IcoPath = RemoveIcon,
            Score = scoreOverride ?? (account.IsActive ? 80 : 100),
            ContextData = new ResultContext { Kind = ResultKind.RemoveAccount, Account = account },
            AsyncAction = onRemove
        };
    }

    public static Result CreateAddAccountResult(Func<ActionContext, bool>? onAdd = null)
    {
        return new Result
        {
            Title = "Add Google account",
            SubTitle = "Runs antigravity-account-switcher add-account and opens OAuth in your browser",
            IcoPath = AddIcon,
            Score = 100,
            Action = onAdd
        };
    }

    public static Result CreateRefreshResult(Func<ActionContext, ValueTask<bool>>? onRefresh = null)
    {
        return new Result
        {
            Title = "Refresh quotas",
            SubTitle = "Forces refresh-quotas / POST /api/quota/refresh",
            IcoPath = RefreshIcon,
            Score = 100,
            AsyncAction = onRefresh
        };
    }

    public static Result CreateRestartResult(Func<ActionContext, ValueTask<bool>>? onRestart = null) =>
        CreateRestartResult(
            "Restart Antigravity Language Server",
            "Restarts the background language_server process to refresh index and state",
            onRestart,
            100);

    public static Result CreateRestartResult(
        string title,
        string subtitle,
        Func<ActionContext, ValueTask<bool>>? onRestart = null,
        int score = 100)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            IcoPath = RestartIcon,
            Score = score,
            AsyncAction = onRestart
        };
    }

    public static Result CreateStatusResult(string statusText)
    {
        var firstLine = statusText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Status";
        return new Result
        {
            Title = firstLine,
            SubTitle = statusText.ReplaceLineEndings(" · "),
            IcoPath = AppIcon,
            Score = 100,
            CopyText = statusText
        };
    }

    public static Result CreateCommandHintResult(string title, string subtitle, string iconPath, Func<ActionContext, bool>? onSelect = null)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            IcoPath = iconPath,
            Score = 20,
            Action = onSelect
        };
    }

    public static Result CreateLoadingResult(
        string title = "Loading accounts…",
        string subtitle = "Fetching list-accounts / dashboard API",
        string iconPath = AppIcon)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            IcoPath = iconPath,
            Score = 100
        };
    }

    public static Result CreateEmptyResult(
        string title,
        string subtitle,
        string iconPath = AppIcon,
        int score = 10,
        Func<ActionContext, bool>? action = null)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            IcoPath = iconPath,
            Score = score,
            Action = action
        };
    }

    public static Result CreateErrorResult(string title, string subtitle, string iconPath = AppIcon)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            IcoPath = iconPath,
            Score = 0
        };
    }
}

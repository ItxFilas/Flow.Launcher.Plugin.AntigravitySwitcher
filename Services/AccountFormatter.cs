using Flow.Launcher.Plugin.AntigravitySwitcher.Models;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

public static class AccountFormatter
{
    public static string FormatQuotaBar(double percent)
    {
        if (percent < 0) return string.Empty;
        var filled = (int)Math.Round(Math.Clamp(percent, 0, 100) / 20.0);
        return new string('▰', filled) + new string('▱', 5 - filled);
    }

    public static string FormatPercent(double percent) => percent < 0 ? "N/A" : $"{percent:0}%";

    public static string FormatTitle(AccountInfo account)
    {
        var baseName = account.DisplayName;
        var activeTag = account.IsActive ? " [ACTIVE]" : "";
        var bar = FormatQuotaBar(account.RemainingPercent);
        var quotaSummary = string.IsNullOrEmpty(bar) ? "" : $"  {bar} {FormatPercent(account.RemainingPercent)}";
        return $"{baseName}{activeTag}{quotaSummary}";
    }

    public static string FormatSubtitle(AccountInfo account)
    {
        var state = account.StateLabel;
        var fiveHour = $"5hr {FormatPercent(account.FiveHourPercent)}{FormatReset(account.FiveHourResetAt)}";
        var weekly = $"week {FormatPercent(account.WeeklyPercent)}{FormatReset(account.WeeklyResetAt)}";
        return $"{state} · {fiveHour} · {weekly}";
    }

    public static string FormatReset(DateTimeOffset? resetAt)
    {
        if (resetAt is null) return string.Empty;
        var remaining = resetAt.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return " (resets now)";
        if (remaining.TotalHours >= 24) return $" (resets {remaining.TotalDays:0}d)";
        if (remaining.TotalHours >= 1) return $" (resets {remaining.Hours}h{remaining.Minutes:00}m)";
        return $" (resets {remaining.Minutes}m)";
    }
}

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

    public static double ApproximateVisualWidth(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var width = 0.0;
        foreach (var c in text)
        {
            width += c switch
            {
                'i' or 'l' or '.' or ':' or ';' or '!' or '|' or '\'' => 1.0,
                'f' or 'r' or 't' or 'j' => 1.3,
                'm' or 'M' or 'W' => 3.1,
                'w' => 2.6,
                '@' => 3.5,
                ' ' => 1.0,
                _ => char.IsUpper(c) ? 2.4 : 2.0
            };
        }
        return width;
    }

    public static string FormatTitle(AccountInfo account, double targetVisualWidth = 0)
    {
        var baseName = account.DisplayName;
        var bar = FormatQuotaBar(account.RemainingPercent);
        var paddingSpaces = 0;
        if (targetVisualWidth > 0 && !string.IsNullOrEmpty(bar))
        {
            var diff = targetVisualWidth - ApproximateVisualWidth(baseName);
            if (diff > 0)
            {
                paddingSpaces = (int)Math.Round(diff);
            }
        }

        var padding = paddingSpaces > 0 ? new string(' ', paddingSpaces) : "";
        var quotaSummary = string.IsNullOrEmpty(bar) ? "" : $"{padding}  {bar} {FormatPercent(account.RemainingPercent)}";
        var activeTag = account.IsActive ? " [ACTIVE]" : "";
        return $"{baseName}{quotaSummary}{activeTag}";
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

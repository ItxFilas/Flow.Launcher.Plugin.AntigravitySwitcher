using Flow.Launcher.Plugin.AntigravitySwitcher.Services;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Models;

public sealed class AccountInfo
{
    private readonly double? _remainingPercent;

    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsActive { get; init; }

    /// <summary>Worst usable quota across all buckets (-1 when unknown). Used for ordering/quick-switch.</summary>
    public double RemainingPercent
    {
        get
        {
            if (_remainingPercent.HasValue)
            {
                return _remainingPercent.Value;
            }

            if (FiveHourPercent >= 0 && WeeklyPercent >= 0)
            {
                return Math.Min(FiveHourPercent, WeeklyPercent);
            }

            if (FiveHourPercent >= 0)
            {
                return FiveHourPercent;
            }

            if (WeeklyPercent >= 0)
            {
                return WeeklyPercent;
            }

            return -1;
        }
        init => _remainingPercent = value;
    }

    /// <summary>Earliest upcoming reset across all buckets (used for the "next reset" label).</summary>
    public DateTimeOffset? ResetAt { get; init; }

    /// <summary>Worst remaining quota in the short 5-hour window (-1 when unknown).</summary>
    public double FiveHourPercent { get; init; } = -1;

    /// <summary>Worst remaining quota in the weekly window (-1 when unknown).</summary>
    public double WeeklyPercent { get; init; } = -1;

    public DateTimeOffset? FiveHourResetAt { get; init; }

    public DateTimeOffset? WeeklyResetAt { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Email) ? Id : Email;

    public string RoleLabel => IsActive ? "Active" : "Standby";

    public string StatusLabel => string.IsNullOrWhiteSpace(Status)
        ? RoleLabel
        : char.ToUpperInvariant(Status[0]) + Status[1..];

    /// <summary>
    /// Human readable state shown in the result subtitle. Distinguishes the current account
    /// (Active) from other healthy accounts (Standby) and surfaces problem states reported by
    /// the switcher (exhausted / error / disabled).
    /// </summary>
    public string StateLabel
    {
        get
        {
            if (IsActive)
            {
                return "Active";
            }

            return Status.ToLowerInvariant() switch
            {
                "exhausted" => "Exhausted",
                "error" => "Error",
                "disabled" => "Disabled",
                _ => "Standby"
            };
        }
    }

    public string RemainingLabel => AccountFormatter.FormatPercent(RemainingPercent);

    public string FiveHourLabel => AccountFormatter.FormatPercent(FiveHourPercent);

    public string WeeklyLabel => AccountFormatter.FormatPercent(WeeklyPercent);

    public string ResetLabel
    {
        get
        {
            if (ResetAt is null)
            {
                return "reset unknown";
            }

            var remaining = ResetAt.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return "reset now";
            }

            if (remaining.TotalHours >= 24)
            {
                return $"resets in {remaining.TotalDays:0}d";
            }

            if (remaining.TotalHours >= 1)
            {
                return $"resets in {remaining.Hours}h {remaining.Minutes:00}m";
            }

            return $"resets in {remaining.Minutes}m";
        }
    }

    public string Title => AccountFormatter.FormatTitle(this);

    public string Subtitle => AccountFormatter.FormatSubtitle(this);
}

public enum ResultKind
{
    Account,
    NextSwitch,
    RemoveAccount,
    Command
}

public sealed class ResultContext
{
    public ResultKind Kind { get; init; }
    public AccountInfo? Account { get; init; }
}

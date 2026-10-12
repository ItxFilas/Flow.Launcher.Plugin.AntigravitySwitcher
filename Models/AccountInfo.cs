namespace Flow.Launcher.Plugin.AntigravitySwitcher.Models;

public sealed class AccountInfo
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public double RemainingPercent { get; init; }
    public DateTimeOffset? ResetAt { get; init; }

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

    public string RemainingLabel => RemainingPercent < 0 ? "N/A" : $"{RemainingPercent:0}%";

    public string ResetLabel
    {
        get
        {
            if (ResetAt is null)
            {
                return "reset neznámý";
            }

            var remaining = ResetAt.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return "reset teď";
            }

            if (remaining.TotalHours >= 24)
            {
                return $"reset za {remaining.TotalDays:0} d";
            }

            if (remaining.TotalHours >= 1)
            {
                return $"reset za {remaining.Hours}h {remaining.Minutes:00}m";
            }

            return $"reset za {remaining.Minutes}m";
        }
    }

    public string Subtitle => $"{StateLabel} · kvóta {RemainingLabel} · {ResetLabel}";
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

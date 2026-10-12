using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class AccountInfoTests
{
    [Theory]
    [InlineData(true, "active", "Active")]
    [InlineData(false, "active", "Standby")]
    [InlineData(false, "", "Standby")]
    [InlineData(false, "exhausted", "Exhausted")]
    [InlineData(false, "error", "Error")]
    [InlineData(false, "disabled", "Disabled")]
    public void StateLabel_DescribesAccountState(bool isActive, string status, string expected)
    {
        var account = new AccountInfo { IsActive = isActive, Status = status };

        Assert.Equal(expected, account.StateLabel);
    }

    [Fact]
    public void RemainingLabel_ShowsPlaceholderForUnknownQuota()
    {
        Assert.Equal("N/A", new AccountInfo { RemainingPercent = -1 }.RemainingLabel);
        Assert.Equal("87%", new AccountInfo { RemainingPercent = 87.4 }.RemainingLabel);
    }

    [Fact]
    public void WindowLabels_ShowPlaceholderForUnknownQuota()
    {
        Assert.Equal("N/A", new AccountInfo().FiveHourLabel);
        Assert.Equal("N/A", new AccountInfo().WeeklyLabel);
        Assert.Equal("25%", new AccountInfo { FiveHourPercent = 25.4 }.FiveHourLabel);
        Assert.Equal("68%", new AccountInfo { WeeklyPercent = 67.6 }.WeeklyLabel);
    }

    [Fact]
    public void ResetLabel_HandlesUnknownPastAndFutureResets()
    {
        Assert.Equal("reset unknown", new AccountInfo().ResetLabel);
        Assert.Equal("reset now", new AccountInfo { ResetAt = DateTimeOffset.UtcNow.AddMinutes(-5) }.ResetLabel);
        Assert.Equal("resets in 3d", new AccountInfo { ResetAt = DateTimeOffset.UtcNow.AddDays(3) }.ResetLabel);
        Assert.StartsWith("resets in 2h", new AccountInfo { ResetAt = DateTimeOffset.UtcNow.AddHours(2).AddMinutes(30) }.ResetLabel);
        Assert.StartsWith("resets in", new AccountInfo { ResetAt = DateTimeOffset.UtcNow.AddMinutes(30) }.ResetLabel);
    }

    [Fact]
    public void Subtitle_CombinesStateAndBothWindows()
    {
        var account = new AccountInfo
        {
            Email = "alice@example.com",
            IsActive = true,
            RemainingPercent = 68,
            FiveHourPercent = 87,
            WeeklyPercent = 68
        };

        Assert.Equal("Active · 5hr 87% · week 68%", account.Subtitle);
        Assert.Equal("alice@example.com", account.DisplayName);
    }

    [Fact]
    public void Subtitle_AppendsPerWindowResetWhenKnown()
    {
        var account = new AccountInfo
        {
            IsActive = false,
            FiveHourPercent = 25,
            WeeklyPercent = 68,
            FiveHourResetAt = DateTimeOffset.UtcNow.AddHours(2).AddMinutes(45),
            WeeklyResetAt = DateTimeOffset.UtcNow.AddDays(5)
        };

        Assert.StartsWith("Standby · 5hr 25% (resets 2h", account.Subtitle);
        Assert.Contains("week 68% (resets 5d)", account.Subtitle);
    }

    [Fact]
    public void DisplayName_FallsBackToIdWhenEmailMissing()
    {
        Assert.Equal("abc123", new AccountInfo { Id = "abc123" }.DisplayName);
    }
}

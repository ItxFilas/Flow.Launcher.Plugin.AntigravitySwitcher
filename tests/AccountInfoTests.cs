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
    public void ResetLabel_HandlesUnknownPastAndFutureResets()
    {
        Assert.Equal("reset neznámý", new AccountInfo().ResetLabel);
        Assert.Equal("reset teď", new AccountInfo { ResetAt = DateTimeOffset.UtcNow.AddMinutes(-5) }.ResetLabel);
        Assert.Equal("reset za 3 d", new AccountInfo { ResetAt = DateTimeOffset.UtcNow.AddDays(3) }.ResetLabel);
        Assert.StartsWith("reset za 2h", new AccountInfo { ResetAt = DateTimeOffset.UtcNow.AddHours(2).AddMinutes(30) }.ResetLabel);
        Assert.StartsWith("reset za", new AccountInfo { ResetAt = DateTimeOffset.UtcNow.AddMinutes(30) }.ResetLabel);
    }

    [Fact]
    public void Subtitle_CombinesStateQuotaAndReset()
    {
        var account = new AccountInfo
        {
            Email = "alice@example.com",
            IsActive = true,
            RemainingPercent = 87,
            ResetAt = DateTimeOffset.UtcNow.AddDays(2)
        };

        Assert.Equal("Active · kvóta 87% · reset za 2 d", account.Subtitle);
        Assert.Equal("alice@example.com", account.DisplayName);
    }

    [Fact]
    public void DisplayName_FallsBackToIdWhenEmailMissing()
    {
        Assert.Equal("abc123", new AccountInfo { Id = "abc123" }.DisplayName);
    }
}

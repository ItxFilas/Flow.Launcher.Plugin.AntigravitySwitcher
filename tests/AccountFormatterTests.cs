using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class AccountFormatterTests
{
    [Theory]
    [InlineData(100, "▰▰▰▰▰")]
    [InlineData(80, "▰▰▰▰▱")]
    [InlineData(60, "▰▰▰▱▱")]
    [InlineData(40, "▰▰▱▱▱")]
    [InlineData(20, "▰▱▱▱▱")]
    [InlineData(0, "▱▱▱▱▱")]
    [InlineData(-1, "")]
    [InlineData(-10, "")]
    [InlineData(105, "▰▰▰▰▰")]
    public void FormatQuotaBar_RendersExpectedUnicodeBlocks(double percent, string expected)
    {
        Assert.Equal(expected, AccountFormatter.FormatQuotaBar(percent));
    }

    [Fact]
    public void FormatTitle_IncludesActiveTagAndQuotaBar()
    {
        var account = new AccountInfo
        {
            Email = "dev@example.com",
            IsActive = true,
            FiveHourPercent = 80,
            WeeklyPercent = 90
        };

        var title = AccountFormatter.FormatTitle(account);
        Assert.Contains("dev@example.com", title);
        Assert.Contains("[ACTIVE]", title);
        Assert.Contains("▰▰▰▰▱", title);
        Assert.Contains("80%", title);
    }

    [Fact]
    public void FormatTitle_StandbyAccountOmitsActiveTag()
    {
        var account = new AccountInfo
        {
            Email = "standby@example.com",
            IsActive = false,
            RemainingPercent = 60
        };

        var title = AccountFormatter.FormatTitle(account);
        Assert.StartsWith("standby@example.com", title);
        Assert.DoesNotContain("[ACTIVE]", title);
        Assert.Contains("▰▰▰▱▱", title);
        Assert.Contains("60%", title);
    }

    [Fact]
    public void FormatTitle_UnknownQuotaOmitsBar()
    {
        var account = new AccountInfo
        {
            Email = "unknown@example.com",
            IsActive = false,
            RemainingPercent = -1
        };

        var title = AccountFormatter.FormatTitle(account);
        Assert.Equal("unknown@example.com", title);
    }

    [Fact]
    public void FormatSubtitle_CombinesStateAndBothWindows()
    {
        var account = new AccountInfo
        {
            Email = "alice@example.com",
            IsActive = true,
            FiveHourPercent = 87,
            WeeklyPercent = 68
        };

        var subtitle = AccountFormatter.FormatSubtitle(account);
        Assert.Equal("Active · 5hr 87% · week 68%", subtitle);
    }

    [Fact]
    public void FormatSubtitle_IncludesResetTimeWhenAvailable()
    {
        var account = new AccountInfo
        {
            Email = "bob@example.com",
            IsActive = false,
            FiveHourPercent = 25,
            WeeklyPercent = 68,
            FiveHourResetAt = DateTimeOffset.UtcNow.AddHours(2).AddMinutes(30),
            WeeklyResetAt = DateTimeOffset.UtcNow.AddDays(3)
        };

        var subtitle = AccountFormatter.FormatSubtitle(account);
        Assert.StartsWith("Standby · 5hr 25% (resets 2h", subtitle);
        Assert.Contains("week 68% (resets 3d)", subtitle);
    }

    [Theory]
    [InlineData(-1, "N/A")]
    [InlineData(0, "0%")]
    [InlineData(87.4, "87%")]
    [InlineData(100, "100%")]
    public void FormatPercent_ReturnsFormattedString(double percent, string expected)
    {
        Assert.Equal(expected, AccountFormatter.FormatPercent(percent));
    }
}

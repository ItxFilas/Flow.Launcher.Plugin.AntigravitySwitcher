using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class AccountSelectionTests
{
    [Fact]
    public void SelectNextAvailable_PicksHealthyAccountWithBestQuota()
    {
        var accounts = new List<AccountInfo>
        {
            new() { Id = "active", Email = "current@example.com", Status = "active", IsActive = true, RemainingPercent = 10 },
            new() { Id = "best", Email = "best@example.com", Status = "active", RemainingPercent = 90 },
            new() { Id = "exhausted", Email = "exhausted@example.com", Status = "exhausted", RemainingPercent = 99 },
            new() { Id = "disabled", Email = "disabled@example.com", Status = "disabled", RemainingPercent = 100 },
            new() { Id = "error", Email = "error@example.com", Status = "error", RemainingPercent = 100 }
        };

        var next = AccountService.SelectNextAvailable(accounts);

        Assert.NotNull(next);
        Assert.Equal("best", next!.Id);
    }

    [Fact]
    public void SelectNextAvailable_ReturnsNullWhenNoHealthyAccountRemains()
    {
        var accounts = new List<AccountInfo>
        {
            new() { Id = "active", Email = "current@example.com", Status = "active", IsActive = true },
            new() { Id = "exhausted", Email = "exhausted@example.com", Status = "exhausted" },
            new() { Id = "error", Email = "error@example.com", Status = "error" }
        };

        Assert.Null(AccountService.SelectNextAvailable(accounts));
    }

    [Fact]
    public void SelectNextAvailable_BreaksTiesByEmailForStableOrdering()
    {
        var accounts = new List<AccountInfo>
        {
            new() { Id = "b", Email = "b@example.com", Status = "active", RemainingPercent = 50 },
            new() { Id = "a", Email = "a@example.com", Status = "active", RemainingPercent = 50 }
        };

        Assert.Equal("a", AccountService.SelectNextAvailable(accounts)!.Id);
    }

    [Fact]
    public void SelectNextAvailable_TreatsEmptyStatusAsAvailable()
    {
        var accounts = new List<AccountInfo>
        {
            new() { Id = "unknown", Email = "unknown@example.com", Status = string.Empty, RemainingPercent = 42 }
        };

        Assert.Equal("unknown", AccountService.SelectNextAvailable(accounts)!.Id);
    }
}

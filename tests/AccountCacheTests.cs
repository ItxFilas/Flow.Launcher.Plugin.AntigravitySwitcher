using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class AccountCacheTests
{
    private static readonly IReadOnlyList<AccountInfo> Sample =
    [
        new AccountInfo { Id = "a1", Email = "alice@example.com" }
    ];

    [Fact]
    public void TryGet_ReturnsFalseBeforeAnySet()
    {
        var cache = new AccountCache();

        Assert.False(cache.TryGet(out var accounts));
        Assert.Empty(accounts);
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsCachedAccounts()
    {
        var cache = new AccountCache();

        cache.Set(Sample);

        Assert.True(cache.TryGet(out var accounts));
        Assert.Same(Sample, accounts);
    }

    [Fact]
    public void Invalidate_ClearsCachedAccounts()
    {
        var cache = new AccountCache();
        cache.Set(Sample);

        cache.Invalidate();

        Assert.False(cache.TryGet(out _));
    }

    [Fact]
    public async Task TryGet_ReturnsFalseAfterTtlExpires()
    {
        var cache = new AccountCache(TimeSpan.FromMilliseconds(30));
        cache.Set(Sample);

        await Task.Delay(150);

        Assert.False(cache.TryGet(out _));
    }
}

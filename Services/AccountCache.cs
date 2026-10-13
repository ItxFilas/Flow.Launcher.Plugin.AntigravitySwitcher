using Flow.Launcher.Plugin.AntigravitySwitcher.Models;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

public sealed class AccountCache
{
    private readonly TimeSpan _ttl;
    private readonly object _gate = new();
    private IReadOnlyList<AccountInfo>? _accounts;
    private DateTimeOffset _expiresAt;

    public AccountCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(30);
    }

    public bool TryGet(out IReadOnlyList<AccountInfo> accounts)
    {
        if (TryGet(out accounts, out var isStale) && !isStale)
        {
            return true;
        }

        accounts = [];
        return false;
    }

    public bool TryGet(out IReadOnlyList<AccountInfo> accounts, out bool isStale)
    {
        lock (_gate)
        {
            if (_accounts is not null)
            {
                accounts = _accounts;
                isStale = DateTimeOffset.UtcNow >= _expiresAt;
                return true;
            }
        }

        accounts = [];
        isStale = false;
        return false;
    }

    public void Set(IReadOnlyList<AccountInfo> accounts)
    {
        lock (_gate)
        {
            _accounts = accounts;
            _expiresAt = DateTimeOffset.UtcNow + _ttl;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _accounts = null;
            _expiresAt = DateTimeOffset.MinValue;
        }
    }
}

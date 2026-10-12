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
        _ttl = ttl ?? TimeSpan.FromSeconds(8);
    }

    public bool TryGet(out IReadOnlyList<AccountInfo> accounts)
    {
        lock (_gate)
        {
            if (_accounts is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                accounts = _accounts;
                return true;
            }
        }

        accounts = [];
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

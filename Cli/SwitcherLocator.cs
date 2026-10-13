using System.Diagnostics;
using System.IO;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Cli;

public sealed class SwitcherLocator
{
    public const string BinaryName = "antigravity-account-switcher";

    private SwitcherTarget? _cached;
    private DateTimeOffset _cachedAt;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private static bool? s_hasWslCached;
    private static DateTimeOffset s_hasWslCachedAt;
    private static readonly TimeSpan WslCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly object s_wslGate = new();

    public SwitcherTarget Resolve()
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheTtl)
        {
            return _cached;
        }

        var native = ResolveNative();
        if (native is not null)
        {
            _cached = native;
            _cachedAt = DateTimeOffset.UtcNow;
            return native;
        }

        if (HasWsl())
        {
            _cached = new SwitcherTarget(ExecutionMode.Wsl, "wsl.exe", $"-e {BinaryName}");
            _cachedAt = DateTimeOffset.UtcNow;
            return _cached;
        }

        throw new FileNotFoundException(
            "The antigravity-account-switcher tool was not found. Install it into PATH or into WSL.");
    }

    public void Invalidate()
    {
        _cached = null;
        InvalidateWslCache();
    }

    internal static void InvalidateWslCache()
    {
        lock (s_wslGate)
        {
            s_hasWslCached = null;
        }
    }

    private static SwitcherTarget? ResolveNative()
    {
        var envPath = Environment.GetEnvironmentVariable("ANTIGRAVITY_SWITCHER_BIN");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return new SwitcherTarget(ExecutionMode.Native, envPath, string.Empty);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, ".local", "bin", BinaryName),
            Path.Combine(home, ".local", "bin", BinaryName + ".exe"),
            Path.Combine(home, "go", "bin", BinaryName + ".exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "antigravity-account-switcher", BinaryName + ".exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return new SwitcherTarget(ExecutionMode.Native, path, string.Empty);
            }
        }

        var fromPath = FindOnPath(BinaryName) ?? FindOnPath(BinaryName + ".exe");
        return fromPath is null ? null : new SwitcherTarget(ExecutionMode.Native, fromPath, string.Empty);
    }

    internal static string? FindOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        var extensions = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? (Environment.GetEnvironmentVariable("PATHEXT")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [".EXE", ".CMD", ".BAT"])
            : [""];

        foreach (var rawDir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var dir = rawDir.Trim('\"');
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    continue;
                }

                var exact = Path.Combine(dir, fileName);
                if (File.Exists(exact))
                {
                    return exact;
                }

                if (!Path.HasExtension(fileName))
                {
                    foreach (var ext in extensions)
                    {
                        var formattedExt = ext.StartsWith('.') ? ext : "." + ext;
                        var candidate = Path.Combine(dir, fileName + formattedExt);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
                // Ignore directory permission or invalid path issues
            }
        }

        return null;
    }

    internal static bool HasWsl()
    {
        lock (s_wslGate)
        {
            if (s_hasWslCached.HasValue && DateTimeOffset.UtcNow - s_hasWslCachedAt < WslCacheTtl)
            {
                return s_hasWslCached.Value;
            }

            s_hasWslCached = ProbeWsl();
            s_hasWslCachedAt = DateTimeOffset.UtcNow;
            return s_hasWslCached.Value;
        }
    }

    private static bool ProbeWsl()
    {
        if (FindOnPath("wsl.exe") is null)
        {
            return false;
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "-e true",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            return process.WaitForExit(1500) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

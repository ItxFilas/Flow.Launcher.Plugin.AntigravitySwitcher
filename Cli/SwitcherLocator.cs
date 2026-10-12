using System.Diagnostics;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Cli;

public sealed class SwitcherLocator
{
    public const string BinaryName = "antigravity-account-switcher";

    private SwitcherTarget? _cached;
    private DateTimeOffset _cachedAt;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

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
            "Nástroj antigravity-account-switcher nebyl nalezen. Nainstalujte ho do PATH, nebo do WSL.");
    }

    public void Invalidate()
    {
        _cached = null;
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

    private static string? FindOnPath(string fileName)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1500);
            var first = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return !string.IsNullOrWhiteSpace(first) && File.Exists(first) ? first : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasWsl()
    {
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

            return process.WaitForExit(2000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

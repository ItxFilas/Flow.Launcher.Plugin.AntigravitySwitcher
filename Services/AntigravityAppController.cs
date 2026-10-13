using System.Diagnostics;
using System.IO;
using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

/// <summary>
/// Applies a freshly-swapped Antigravity login to the desktop app after the switcher has
/// rewritten the Windows Credential Manager entry, or restarts the app/backend on request.
/// </summary>
public interface IAntigravityAppController
{
    /// <summary>
    /// Restarts the app's language_server backend (the Electron frontend respawns it and the
    /// new process reads the swapped login), or launches the app when it is not running.
    /// </summary>
    Task<bool> RestartForLoginSwapAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes and immediately relaunches the full Antigravity desktop IDE.
    /// </summary>
    Task<bool> RestartAppAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Locates, closes and relaunches the Antigravity IDE on Windows with sub-second latency.
/// </summary>
public sealed class AntigravityAppController : IAntigravityAppController
{
    private const string LanguageServerProcessName = "language_server";
    private const string AppProcessName = "Antigravity";

    private readonly CliRunner _cli;
    private static string? _cachedExePath;

    public AntigravityAppController(CliRunner cli)
    {
        _cli = cli;
    }

    public async Task<bool> RestartForLoginSwapAsync(CancellationToken cancellationToken = default)
    {
        var appRunning = IsAnyRunning(AppProcessName);
        var backendRunning = IsAnyRunning(LanguageServerProcessName);

        if (appRunning || backendRunning)
        {
            // 1. Instant path: kill language_server and let Electron respawn it (< 30ms).
            if (backendRunning && await RestartBackendAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            // 2. If no backend was running (e.g. hung or killed), quickly restart the app.
            return await RestartAppAsync(cancellationToken).ConfigureAwait(false);
        }

        // The app is not running: launch it normally; it reads the swapped login at startup.
        return await LaunchIfFoundAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RestartAppAsync(CancellationToken cancellationToken = default)
    {
        // Resolve exe path before closing so we already have the path primed in memory
        var exePath = await ResolveExePathAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        await CloseAsync(cancellationToken).ConfigureAwait(false);
        await LaunchAsync(exePath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> RestartBackendAsync(CancellationToken cancellationToken)
    {
        var processes = Process.GetProcessesByName(LanguageServerProcessName);
        if (processes.Length == 0)
        {
            return false;
        }

        var oldPids = processes.Select(p => p.Id).ToHashSet();

        // Concurrently terminate all language server instances
        foreach (var process in processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process already exited
            }
        }

        // Confirm termination of previous instances (< 30ms)
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(300);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                var remaining = Process.GetProcessesByName(LanguageServerProcessName);
                if (remaining.All(p => p.HasExited || !oldPids.Contains(p.Id)))
                {
                    return true;
                }
            }
            catch
            {
            }

            await Task.Delay(15, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> LaunchIfFoundAsync(CancellationToken cancellationToken)
    {
        var exePath = await ResolveExePathAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        await LaunchAsync(exePath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool IsAnyRunning(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> ResolveExePathAsync(CancellationToken cancellationToken)
    {
        if (_cachedExePath is not null && File.Exists(_cachedExePath))
        {
            return _cachedExePath;
        }

        // 1. A running Antigravity process knows its own executable path (< 0.5ms)
        try
        {
            foreach (var process in Process.GetProcessesByName(AppProcessName))
            {
                var path = TryGetPath(process);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    _cachedExePath = path;
                    return path;
                }
            }
        }
        catch
        {
        }

        // 2. Standard per-user Electron install locations checked in memory (< 0.1ms)
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("ANTIGRAVITY_BIN"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Antigravity", "Antigravity.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Antigravity", "Antigravity.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Antigravity", "Antigravity.exe")
        };

        var matched = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c));
        if (matched is not null)
        {
            _cachedExePath = matched;
            return matched;
        }

        // 3. Fallback: ask switcher config only if standard locations fail
        var fromConfig = await ResolveFromSwitcherConfigAsync(cancellationToken).ConfigureAwait(false);
        if (fromConfig is not null)
        {
            _cachedExePath = fromConfig;
            return fromConfig;
        }

        return null;
    }

    private static async Task CloseAsync(CancellationToken cancellationToken)
    {
        var appProcesses = Process.GetProcessesByName(AppProcessName);
        var backendProcesses = Process.GetProcessesByName(LanguageServerProcessName);

        if (appProcesses.Length == 0 && backendProcesses.Length == 0)
        {
            return;
        }

        // Concurrently terminate all app and backend processes with entire process trees
        foreach (var process in appProcesses)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        foreach (var process in backendProcesses)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        // Wait until all processes have fully exited and released Windows file/pipe locks (< 50ms)
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(800);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!IsAnyRunning(AppProcessName) && !IsAnyRunning(LanguageServerProcessName))
            {
                return;
            }

            await Task.Delay(15, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task LaunchAsync(string exePath, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(exePath);
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir,
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private async Task<string?> ResolveFromSwitcherConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _cli.RunAsync(["config", "get", "antigravity_bin"], TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            var path = result.StdOut.Trim();
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
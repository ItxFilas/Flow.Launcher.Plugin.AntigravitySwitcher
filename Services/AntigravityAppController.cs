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
    private static readonly TimeSpan BackendRespawnTimeout = TimeSpan.FromSeconds(3);

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
            // 1. Blazing fast path: restart only the backend and let the frontend respawn it (< 300ms).
            if (backendRunning && await RestartBackendAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            // 2. If the frontend did not respawn a backend within 3s, quickly restart the whole app.
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

    private async Task<bool> RestartBackendAsync(CancellationToken cancellationToken)
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

        // High-frequency polling (50ms interval) to catch respawn immediately
        var deadline = DateTimeOffset.UtcNow + BackendRespawnTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);

            try
            {
                var current = Process.GetProcessesByName(LanguageServerProcessName);
                if (current.Any(p => !p.HasExited && !oldPids.Contains(p.Id)))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore process table races
            }
        }

        return false;
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
        var processes = Process.GetProcessesByName(AppProcessName);
        if (processes.Length == 0)
        {
            return;
        }

        // Request graceful window close
        foreach (var process in processes)
        {
            try
            {
                process.CloseMainWindow();
            }
            catch
            {
            }
        }

        // Fast poll for graceful exit up to 600ms
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(600);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!IsAnyRunning(AppProcessName))
            {
                return;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        // Terminate any remaining app processes immediately
        foreach (var process in Process.GetProcessesByName(AppProcessName))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }

        // Terminate any lingering language_server backend
        foreach (var process in Process.GetProcessesByName(LanguageServerProcessName))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
    }

    private static Task LaunchAsync(string exePath, CancellationToken cancellationToken)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
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
using System.Diagnostics;
using System.IO;
using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

/// <summary>
/// Applies a freshly-swapped Antigravity login to the desktop app after the switcher has
/// rewritten the Windows Credential Manager entry.
/// </summary>
public interface IAntigravityAppController
{
    /// <summary>
    /// Restarts the app's language_server backend (the Electron frontend respawns it and the
    /// new process reads the swapped login), or launches the app when it is not running.
    /// Returns false when the app could not be brought up — the swap already succeeded, so the
    /// user just has to open Antigravity.
    /// </summary>
    Task<bool> RestartForLoginSwapAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Locates, closes and relaunches the Antigravity IDE on Windows. To apply a login swap the
/// running app only needs its language_server backend restarted: the Electron frontend respawns
/// it automatically and the new process picks up the swapped credential, keeping the window open.
/// If the backend does not come back, the whole app is closed and relaunched instead.
/// </summary>
public sealed class AntigravityAppController : IAntigravityAppController
{
    private const string LanguageServerProcessName = "language_server";
    private const string AppProcessName = "Antigravity";
    private static readonly TimeSpan BackendRespawnTimeout = TimeSpan.FromSeconds(15);

    private readonly CliRunner _cli;

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
            // The app is up: restart only the backend and let the frontend respawn it.
            if (backendRunning && await RestartBackendAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            // The frontend did not respawn a backend on its own — restart the whole app.
            return await RestartAppAsync(cancellationToken).ConfigureAwait(false);
        }

        // The app is not running: launch it normally; it reads the swapped login at startup.
        return await LaunchIfFoundAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RestartBackendAsync(CancellationToken cancellationToken)
    {
        foreach (var process in Process.GetProcessesByName(LanguageServerProcessName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch
            {
                // The process is already gone.
            }
        }

        var deadline = DateTimeOffset.UtcNow + BackendRespawnTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            if (IsAnyRunning(LanguageServerProcessName))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> RestartAppAsync(CancellationToken cancellationToken)
    {
        await CloseAsync(cancellationToken).ConfigureAwait(false);
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        return await LaunchIfFoundAsync(cancellationToken).ConfigureAwait(false);
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
        // 1. The switcher's saved configuration is the source of truth.
        var fromConfig = await ResolveFromSwitcherConfigAsync(cancellationToken).ConfigureAwait(false);
        if (fromConfig is not null)
        {
            return fromConfig;
        }

        // 2. A running Antigravity process knows its own executable path.
        try
        {
            foreach (var process in Process.GetProcessesByName(AppProcessName))
            {
                var path = TryGetPath(process);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }
        }
        catch
        {
            // Access to another process may fail; fall through to the standard locations.
        }

        // 3. Standard per-user Electron install locations.
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("ANTIGRAVITY_BIN"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Antigravity", "Antigravity.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Antigravity", "Antigravity.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Antigravity", "Antigravity.exe")
        };

        return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c));
    }

    private static Task CloseAsync(CancellationToken cancellationToken)
    {
        foreach (var process in Process.GetProcessesByName(AppProcessName))
        {
            try
            {
                process.CloseMainWindow();
            }
            catch
            {
                // The process may already be closing; ignore.
            }
        }

        // Give Electron a moment to shut down cleanly, then force-kill stragglers.
        var allExited = false;
        try
        {
            allExited = Process.GetProcessesByName(AppProcessName)
                .All(p => p.WaitForExit(5000));
        }
        catch
        {
            // Ignore process list races.
        }

        if (!allExited)
        {
            foreach (var process in Process.GetProcessesByName(AppProcessName).Where(p => !p.HasExited))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // The process exited between HasExited and Kill; ignore.
                }
            }
        }

        return Task.CompletedTask;
    }

    private Task LaunchAsync(string exePath, CancellationToken cancellationToken)
    {
        // UseShellExecute launches with the user's normal environment and app associations.
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
            var result = await _cli.RunAsync(["config", "get", "antigravity_bin"], TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
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
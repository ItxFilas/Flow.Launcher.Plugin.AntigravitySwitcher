using System.Diagnostics;
using System.IO;
using System.Text;
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

    /// <summary>
    /// Optimizes the application's app.asar in-place to remove restart cooldown and limit caps.
    /// </summary>
    bool EnsureAsarOptimized(string? asarPath = null) => false;
}

/// <summary>
/// Locates, closes and relaunches the Antigravity IDE on Windows with sub-second latency.
/// </summary>
public sealed class AntigravityAppController : IAntigravityAppController
{
    private const string LanguageServerProcessName = "language_server";
    private const string AppProcessName = "Antigravity";

    private readonly CliRunner _cli;
    private readonly IAntigravityCdpClient? _cdpClient;
    private static string? _cachedExePath;

    public AntigravityAppController(CliRunner cli, IAntigravityCdpClient? cdpClient = null)
    {
        _cli = cli;
        _cdpClient = cdpClient;
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

    public bool EnsureAsarOptimized(string? asarPath = null)
    {
        if (!string.IsNullOrWhiteSpace(asarPath))
        {
            return OptimizeAsarBytes(asarPath);
        }

        var candidates = new List<string?>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Antigravity", "resources", "app.asar"),
            !string.IsNullOrWhiteSpace(_cachedExePath) ? Path.Combine(Path.GetDirectoryName(_cachedExePath)!, "resources", "app.asar") : null,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Antigravity", "resources", "app.asar"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Antigravity", "resources", "app.asar")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return OptimizeAsarBytes(candidate);
            }
        }

        return false;
    }

    public static bool OptimizeAsarBytes(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            var target1 = Encoding.UTF8.GetBytes("RESTART_COOLDOWN_MS = 2000;");
            var replacement1 = Encoding.UTF8.GetBytes("RESTART_COOLDOWN_MS = 0000;");
            var target2 = Encoding.UTF8.GetBytes("const MAX_RESTARTS = 3;");
            var replacement2 = Encoding.UTF8.GetBytes("const MAX_RESTARTS=999;");

            byte[] fileBytes;
            using (var readStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fileBytes = new byte[readStream.Length];
                int totalRead = 0;
                while (totalRead < fileBytes.Length)
                {
                    int read = readStream.Read(fileBytes, totalRead, fileBytes.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
            }

            var replacements = new List<(long offset, byte[] data)>();
            FindOccurrences(fileBytes, target1, replacement1, replacements);
            FindOccurrences(fileBytes, target2, replacement2, replacements);

            if (replacements.Count == 0)
            {
                return false;
            }

            using (var writeStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                foreach (var (offset, data) in replacements)
                {
                    writeStream.Seek(offset, SeekOrigin.Begin);
                    writeStream.Write(data, 0, data.Length);
                }
                writeStream.Flush();
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void FindOccurrences(byte[] source, byte[] target, byte[] replacement, List<(long offset, byte[] data)> replacements)
    {
        ReadOnlySpan<byte> span = source;
        ReadOnlySpan<byte> targetSpan = target;
        int index = 0;
        while (index <= source.Length - target.Length)
        {
            int found = span.Slice(index).IndexOf(targetSpan);
            if (found < 0) break;
            int matchIndex = index + found;
            replacements.Add((matchIndex, replacement));
            index = matchIndex + target.Length;
        }
    }

    private async Task<bool> RestartBackendAsync(CancellationToken cancellationToken)
    {
        var processes = Process.GetProcessesByName(LanguageServerProcessName);
        if (processes.Length == 0)
        {
            return false;
        }

        foreach (var process in processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(400));
        try
        {
            await Task.WhenAll(processes.Select(p => SafeWaitForExitAsync(p, cts.Token))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }

        _cdpClient?.TriggerAutoDismissInBackground();
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

    private async Task CloseAsync(CancellationToken cancellationToken)
    {
        var appProcesses = Process.GetProcessesByName(AppProcessName);
        var backendProcesses = Process.GetProcessesByName(LanguageServerProcessName);

        if (appProcesses.Length > 0 || backendProcesses.Length > 0)
        {
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

            var all = appProcesses.Concat(backendProcesses).ToList();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(600));
            try
            {
                await Task.WhenAll(all.Select(p => SafeWaitForExitAsync(p, cts.Token))).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                foreach (var p in all)
                {
                    p.Dispose();
                }
            }
        }

        try
        {
            EnsureAsarOptimized();
        }
        catch
        {
        }
    }

    private Task LaunchAsync(string exePath, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(exePath);
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir,
            UseShellExecute = false
        });
        _cdpClient?.TriggerAutoDismissInBackground();
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

    private static async Task SafeWaitForExitAsync(Process process, CancellationToken token)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }
    }
}
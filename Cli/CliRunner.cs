using System.Diagnostics;
using System.Text;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Cli;

public sealed class CliRunner
{
    private readonly SwitcherLocator _locator;
    private readonly TimeSpan _defaultTimeout;

    public CliRunner(SwitcherLocator locator, TimeSpan? defaultTimeout = null)
    {
        _locator = locator;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(12);
    }

    public async Task<CliResult> RunAsync(IEnumerable<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var target = _locator.Resolve();
        var argumentLine = Combine(target.PrefixArguments, arguments);
        var start = new ProcessStartInfo
        {
            FileName = target.FileName,
            Arguments = argumentLine,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Nepodařilo se spustit {target.FileName}.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout ?? _defaultTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"Příkaz '{argumentLine}' překročil časový limit.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new CliResult(process.ExitCode, stdout, stderr);
    }

    public void StartDetached(IEnumerable<string> arguments)
    {
        var target = _locator.Resolve();
        var start = new ProcessStartInfo
        {
            FileName = target.FileName,
            Arguments = Combine(target.PrefixArguments, arguments),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(start);
        if (process is null)
        {
            throw new InvalidOperationException($"Nepodařilo se spustit {target.FileName}.");
        }

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => process.Dispose();
    }

    private static string Combine(string prefix, IEnumerable<string> arguments)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            parts.Add(prefix.Trim());
        }

        parts.AddRange(arguments.Select(Quote));
        return string.Join(' ', parts);
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"")}\"" : value;
    }

    private static void TryKill(Process process)
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
            // ignored
        }
    }
}

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Cli;

public sealed record CliResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Succeeded => ExitCode == 0;

    public string Combined => string.IsNullOrWhiteSpace(StdErr)
        ? StdOut
        : string.IsNullOrWhiteSpace(StdOut) ? StdErr : $"{StdOut}\n{StdErr}";
}

public enum ExecutionMode
{
    Unresolved,
    Native,
    Wsl
}

public sealed record SwitcherTarget(ExecutionMode Mode, string FileName, string PrefixArguments);

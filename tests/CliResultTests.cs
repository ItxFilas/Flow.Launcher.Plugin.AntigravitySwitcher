using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class CliResultTests
{
    [Fact]
    public void Succeeded_ReflectsExitCode()
    {
        Assert.True(new CliResult(0, "ok", string.Empty).Succeeded);
        Assert.False(new CliResult(1, string.Empty, "Unknown command: switch").Succeeded);
    }

    [Fact]
    public void Combined_MergesStdOutAndStdErr()
    {
        Assert.Equal("out", new CliResult(0, "out", string.Empty).Combined);
        Assert.Equal("err", new CliResult(1, string.Empty, "err").Combined);
        Assert.Equal("out\nerr", new CliResult(1, "out", "err").Combined);
    }
}

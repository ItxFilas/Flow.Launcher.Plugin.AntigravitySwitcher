using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class DashboardClientTests
{
    [Theory]
    [InlineData("[{\"id\":\"a1\"}]", true)]
    [InlineData("  {\"status\":\"ok\"}", true)]
    [InlineData("No accounts registered yet.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeJson_DetectsJsonPayloads(string? text, bool expected)
    {
        Assert.Equal(expected, DashboardClient.LooksLikeJson(text));
    }

    [Theory]
    [InlineData("{\"success\":true}", true)]
    [InlineData("{\"success\":false}", false)]
    [InlineData("{\"account_id\":\"a1\"}", false)]
    [InlineData("not json", false)]
    public void TryReadSuccess_ReadsSuccessFlag(string json, bool expected)
    {
        Assert.Equal(expected, DashboardClient.TryReadSuccess(json));
    }
}

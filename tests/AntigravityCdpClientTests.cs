namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Xunit;

public sealed class AntigravityCdpClientTests
{
    [Fact]
    public void ResolveActivePort_ParsesFirstLineOfPortFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "47171\n/devtools/browser/xyz\n");
            var port = AntigravityCdpClient.ParsePortFromFile(tempFile);
            Assert.Equal(47171, port);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("not_a_number\n")]
    [InlineData("0\n")]
    [InlineData("-1\n")]
    [InlineData("65536\n")]
    [InlineData("99999\n")]
    [InlineData("")]
    [InlineData("\n")]
    public void ResolveActivePort_ReturnsNullWhenInvalid(string content)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, content);
            Assert.Null(AntigravityCdpClient.ParsePortFromFile(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ResolveActivePort_ReturnsNullWhenFileMissing()
    {
        Assert.Null(AntigravityCdpClient.ParsePortFromFile("C:\\non_existent_file_path_12345.txt"));
    }

    [Fact]
    public void ResolveActivePort_InstanceMethodUsesConfiguredPath()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, " 9222 \n/devtools/browser/abc\n");
            var client = new AntigravityCdpClient(portFilePath: tempFile);
            Assert.Equal(9222, client.ResolveActivePort());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void BuildDismissScript_ContainsRequiredDomChecks()
    {
        var script = AntigravityCdpClient.DismissNoticeScript;
        Assert.Contains("Security Notice", script);
        Assert.Contains("Accept and continue", script);
        Assert.Contains("checkbox", script);
    }

    [Fact]
    public async Task AutoDismissSecurityNoticeAsync_ReturnsFalseWhenPortCannotBeResolved()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var client = new AntigravityCdpClient(portFilePath: nonExistentPath);

        var result = await client.AutoDismissSecurityNoticeAsync();

        Assert.False(result);
    }

    [Fact]
    public void TriggerAutoDismissInBackground_DoesNotThrow()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var client = new AntigravityCdpClient(portFilePath: nonExistentPath);

        var ex = Record.Exception(() => client.TriggerAutoDismissInBackground());

        Assert.Null(ex);
    }
}

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Xunit;

public sealed class AntigravityAppControllerTests
{
    private sealed class FakeCdpClient : IAntigravityCdpClient
    {
        public int TriggerCallCount { get; private set; }
        public int PostRestartActionCallCount { get; private set; }
        public string? LastPostRestartChatPath { get; private set; }
        public int? ResolveActivePort() => 9222;
        public Task<string?> CaptureActiveChatPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> RestoreActiveChatAsync(string chatPath, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> AutoDismissSecurityNoticeAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void TriggerAutoDismissInBackground()
        {
            TriggerCallCount++;
        }
        public void TriggerPostRestartActionsInBackground(string? savedChatPath = null)
        {
            PostRestartActionCallCount++;
            LastPostRestartChatPath = savedChatPath;
        }
    }

    [Fact]
    public async Task RestartAppAsync_InvokesTriggerPostRestartActionsInBackground_WithProvidedPath()
    {
        AntigravityAppController.ResetCachedExePath();
        var tempExe = Path.GetTempFileName();
        var fakeCdp = new FakeCdpClient();
        var controller = new AntigravityAppController(
            new CliRunner(new SwitcherLocator()),
            fakeCdp,
            processLauncher: _ => { });

        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_BIN", tempExe);
            var result = await controller.RestartAppAsync("/c/test-chat-path");

            Assert.True(result);
            Assert.Equal(1, fakeCdp.PostRestartActionCallCount);
            Assert.Equal("/c/test-chat-path", fakeCdp.LastPostRestartChatPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_BIN", null);
            AntigravityAppController.ResetCachedExePath();
            if (File.Exists(tempExe)) File.Delete(tempExe);
        }
    }

    [Fact]
    public async Task RestartForLoginSwapAsync_InvokesTriggerPostRestartActionsInBackground_WithProvidedPath()
    {
        AntigravityAppController.ResetCachedExePath();
        var tempExe = Path.GetTempFileName();
        var fakeCdp = new FakeCdpClient();
        var controller = new AntigravityAppController(
            new CliRunner(new SwitcherLocator()),
            fakeCdp,
            processLauncher: _ => { });

        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_BIN", tempExe);
            var result = await controller.RestartForLoginSwapAsync("/c/swap-chat-path");

            Assert.True(result);
            Assert.Equal(1, fakeCdp.PostRestartActionCallCount);
            Assert.Equal("/c/swap-chat-path", fakeCdp.LastPostRestartChatPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_BIN", null);
            AntigravityAppController.ResetCachedExePath();
            if (File.Exists(tempExe)) File.Delete(tempExe);
        }
    }

    [Fact]
    public void OptimizeAsarBytes_NonExistentPath_ReturnsFalse()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "app.asar");
        var result = AntigravityAppController.OptimizeAsarBytes(missingPath);
        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OptimizeAsarBytes_InvalidPath_ReturnsFalse(string invalidPath)
    {
        var result = AntigravityAppController.OptimizeAsarBytes(invalidPath);
        Assert.False(result);
    }

    [Fact]
    public void OptimizeAsarBytes_SampleContent_ReplacesCooldownAndMaxRestartsInPlace()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var originalContent = "/* header */ function init() { const RESTART_COOLDOWN_MS = 2000; let count = 0; const MAX_RESTARTS = 3; return true; } /* tail */";
            File.WriteAllText(tempFile, originalContent, Encoding.UTF8);
            var originalLength = new FileInfo(tempFile).Length;

            var result = AntigravityAppController.OptimizeAsarBytes(tempFile);

            Assert.True(result);
            var updatedContent = File.ReadAllText(tempFile, Encoding.UTF8);
            var updatedLength = new FileInfo(tempFile).Length;

            Assert.Equal(originalLength, updatedLength);
            Assert.Contains("RESTART_COOLDOWN_MS =    0;", updatedContent);
            Assert.Contains("const MAX_RESTARTS=999;", updatedContent);
            Assert.DoesNotContain("RESTART_COOLDOWN_MS = 2000;", updatedContent);
            Assert.DoesNotContain("const MAX_RESTARTS = 3;", updatedContent);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void OptimizeAsarBytes_LegacyOctalTargetPresent_RepairsToValidJsAndReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "var config = { const RESTART_COOLDOWN_MS = 0000; const MAX_RESTARTS = 3; };";
            File.WriteAllText(tempFile, content, Encoding.UTF8);
            var originalLength = new FileInfo(tempFile).Length;

            var result = AntigravityAppController.OptimizeAsarBytes(tempFile);

            Assert.True(result);
            var updatedContent = File.ReadAllText(tempFile, Encoding.UTF8);
            var updatedLength = new FileInfo(tempFile).Length;

            Assert.Equal(originalLength, updatedLength);
            Assert.Contains("RESTART_COOLDOWN_MS =    0;", updatedContent);
            Assert.Contains("const MAX_RESTARTS=999;", updatedContent);
            Assert.DoesNotContain("RESTART_COOLDOWN_MS = 0000;", updatedContent);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void OptimizeAsarBytes_AlreadyOptimized_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var optimizedContent = "function init() { const RESTART_COOLDOWN_MS =    0; const MAX_RESTARTS=999; }";
            File.WriteAllText(tempFile, optimizedContent, Encoding.UTF8);

            var result = AntigravityAppController.OptimizeAsarBytes(tempFile);

            Assert.False(result);
            Assert.Equal(optimizedContent, File.ReadAllText(tempFile, Encoding.UTF8));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void OptimizeAsarBytes_ContentWithoutTargets_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "console.log('hello world without target tokens');";
            File.WriteAllText(tempFile, content, Encoding.UTF8);

            var result = AntigravityAppController.OptimizeAsarBytes(tempFile);

            Assert.False(result);
            Assert.Equal(content, File.ReadAllText(tempFile, Encoding.UTF8));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void OptimizeAsarBytes_OnlyTarget1Present_ReplacesAndReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "var config = { RESTART_COOLDOWN_MS = 2000; };";
            File.WriteAllText(tempFile, content, Encoding.UTF8);
            var originalLength = new FileInfo(tempFile).Length;

            var result = AntigravityAppController.OptimizeAsarBytes(tempFile);

            Assert.True(result);
            var updated = File.ReadAllText(tempFile, Encoding.UTF8);
            Assert.Equal(originalLength, new FileInfo(tempFile).Length);
            Assert.Contains("RESTART_COOLDOWN_MS =    0;", updated);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void OptimizeAsarBytes_OnlyTarget2Present_ReplacesAndReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "var limits = { const MAX_RESTARTS = 3; };";
            File.WriteAllText(tempFile, content, Encoding.UTF8);
            var originalLength = new FileInfo(tempFile).Length;

            var result = AntigravityAppController.OptimizeAsarBytes(tempFile);

            Assert.True(result);
            var updated = File.ReadAllText(tempFile, Encoding.UTF8);
            Assert.Equal(originalLength, new FileInfo(tempFile).Length);
            Assert.Contains("const MAX_RESTARTS=999;", updated);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void EnsureAsarOptimized_ExplicitPath_OptimizesTargetFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "RESTART_COOLDOWN_MS = 2000; and const MAX_RESTARTS = 3;";
            File.WriteAllText(tempFile, content, Encoding.UTF8);

            var controller = new AntigravityAppController(new CliRunner(new SwitcherLocator()));
            var result = controller.EnsureAsarOptimized(tempFile);

            Assert.True(result);
            var updated = File.ReadAllText(tempFile, Encoding.UTF8);
            Assert.Contains("RESTART_COOLDOWN_MS =    0;", updated);
            Assert.Contains("const MAX_RESTARTS=999;", updated);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void EnsureAsarOptimized_ExplicitNonExistentPath_ReturnsFalse()
    {
        var controller = new AntigravityAppController(new CliRunner(new SwitcherLocator()));
        var result = controller.EnsureAsarOptimized(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.asar"));
        Assert.False(result);
    }

    [Fact]
    public void Constructor_AcceptsOptionalCdpClient()
    {
        var cli = new CliRunner(new SwitcherLocator());
        var cdp = new FakeCdpClient();

        var controllerWithCdp = new AntigravityAppController(cli, cdp);
        var controllerNullCdp = new AntigravityAppController(cli, null);
        var controllerDefault = new AntigravityAppController(cli);

        Assert.NotNull(controllerWithCdp);
        Assert.NotNull(controllerNullCdp);
        Assert.NotNull(controllerDefault);
    }

    [Fact]
    public void ResolveAsarPath_WithNullAndNonExistentCandidates_ReturnsNullOrValid()
    {
        var resolved = AntigravityAppController.ResolveAsarPath(cachedExePath: "C:\\nonexistent\\Antigravity.exe");
        if (resolved is not null)
        {
            Assert.True(File.Exists(resolved));
            Assert.EndsWith("app.asar", resolved, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void IsAppProcessStale_WithNonExistentAsar_ReturnsFalse()
    {
        var result = AntigravityAppController.IsAppProcessStale("C:\\nonexistent\\app.asar");
        Assert.False(result);
    }
}

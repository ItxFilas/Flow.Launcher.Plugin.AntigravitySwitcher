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

    private sealed class MockHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> _handler;

        public MockHttpMessageHandler(string jsonResponse, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
            : this(_ => new System.Net.Http.HttpResponseMessage(statusCode) { Content = new System.Net.Http.StringContent(jsonResponse) })
        {
        }

        public MockHttpMessageHandler(Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public MockHttpMessageHandler(Exception exceptionToThrow)
            : this(_ => throw exceptionToThrow)
        {
        }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task CaptureActiveChatPathAsync_WithActiveChatTarget_ReturnsChatPath()
    {
        var json = @"[
            {
                ""type"": ""page"",
                ""url"": ""https://127.0.0.1:7886/c/c961b41a-e6c1-4c73-98b7-4f5bfa0e399b"",
                ""webSocketDebuggerUrl"": ""ws://127.0.0.1:1234/page/1""
            }
        ]";
        var tempPortFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPortFile, "1234\n");
            using var http = new System.Net.Http.HttpClient(new MockHttpMessageHandler(json));
            var client = new AntigravityCdpClient(http, tempPortFile);

            var path = await client.CaptureActiveChatPathAsync();

            Assert.Equal("/c/c961b41a-e6c1-4c73-98b7-4f5bfa0e399b", path);
        }
        finally
        {
            if (File.Exists(tempPortFile)) File.Delete(tempPortFile);
        }
    }

    [Fact]
    public async Task CaptureActiveChatPathAsync_WithActiveChatTargetAndQuery_ReturnsChatPathWithQuery()
    {
        var json = @"[
            {
                ""type"": ""page"",
                ""url"": ""https://127.0.0.1:7886/c/c961b41a-e6c1-4c73-98b7-4f5bfa0e399b?section=123"",
                ""webSocketDebuggerUrl"": ""ws://127.0.0.1:1234/page/1""
            }
        ]";
        var tempPortFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPortFile, "1234\n");
            using var http = new System.Net.Http.HttpClient(new MockHttpMessageHandler(json));
            var client = new AntigravityCdpClient(http, tempPortFile);

            var path = await client.CaptureActiveChatPathAsync();

            Assert.Equal("/c/c961b41a-e6c1-4c73-98b7-4f5bfa0e399b?section=123", path);
        }
        finally
        {
            if (File.Exists(tempPortFile)) File.Delete(tempPortFile);
        }
    }

    [Fact]
    public async Task CaptureActiveChatPathAsync_WithRootTargetOnly_ReturnsNull()
    {
        var json = @"[
            {
                ""type"": ""page"",
                ""url"": ""https://127.0.0.1:7886/"",
                ""webSocketDebuggerUrl"": ""ws://127.0.0.1:1234/page/1""
            }
        ]";
        var tempPortFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPortFile, "1234\n");
            using var http = new System.Net.Http.HttpClient(new MockHttpMessageHandler(json));
            var client = new AntigravityCdpClient(http, tempPortFile);

            var path = await client.CaptureActiveChatPathAsync();

            Assert.Null(path);
        }
        finally
        {
            if (File.Exists(tempPortFile)) File.Delete(tempPortFile);
        }
    }

    [Fact]
    public async Task CaptureActiveChatPathAsync_WithMissingPortFile_ReturnsNull()
    {
        var missingPortFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var http = new System.Net.Http.HttpClient(new MockHttpMessageHandler("[]"));
        var client = new AntigravityCdpClient(http, missingPortFile);

        var path = await client.CaptureActiveChatPathAsync();

        Assert.Null(path);
    }

    [Fact]
    public async Task CaptureActiveChatPathAsync_WithServerError_ReturnsNull()
    {
        var tempPortFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPortFile, "1234\n");
            using var http = new System.Net.Http.HttpClient(new MockHttpMessageHandler("Internal Error", System.Net.HttpStatusCode.InternalServerError));
            var client = new AntigravityCdpClient(http, tempPortFile);

            var path = await client.CaptureActiveChatPathAsync();

            Assert.Null(path);
        }
        finally
        {
            if (File.Exists(tempPortFile)) File.Delete(tempPortFile);
        }
    }

    [Fact]
    public async Task CaptureActiveChatPathAsync_WithTimeoutOrException_ReturnsNull()
    {
        var tempPortFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPortFile, "1234\n");
            using var http = new System.Net.Http.HttpClient(new MockHttpMessageHandler(new TaskCanceledException("timed out")));
            var client = new AntigravityCdpClient(http, tempPortFile);

            var path = await client.CaptureActiveChatPathAsync();

            Assert.Null(path);
        }
        finally
        {
            if (File.Exists(tempPortFile)) File.Delete(tempPortFile);
        }
    }

    [Fact]
    public async Task CaptureActiveChatPathAsync_WithNonPageTarget_ReturnsNull()
    {
        var json = @"[
            {
                ""type"": ""node"",
                ""url"": ""https://127.0.0.1:7886/c/c961b41a-e6c1-4c73-98b7-4f5bfa0e399b"",
                ""webSocketDebuggerUrl"": ""ws://127.0.0.1:1234/page/1""
            }
        ]";
        var tempPortFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPortFile, "1234\n");
            using var http = new System.Net.Http.HttpClient(new MockHttpMessageHandler(json));
            var client = new AntigravityCdpClient(http, tempPortFile);

            var path = await client.CaptureActiveChatPathAsync();

            Assert.Null(path);
        }
        finally
        {
            if (File.Exists(tempPortFile)) File.Delete(tempPortFile);
        }
    }

    [Fact]
    public async Task CaptureActiveChatPathAsync_WithInvalidPathFormat_ReturnsNull()
    {
        var json = @"[
            {
                ""type"": ""page"",
                ""url"": ""https://127.0.0.1:7886/c/"",
                ""webSocketDebuggerUrl"": ""ws://127.0.0.1:1234/page/1""
            }
        ]";
        var tempPortFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPortFile, "1234\n");
            using var http = new System.Net.Http.HttpClient(new MockHttpMessageHandler(json));
            var client = new AntigravityCdpClient(http, tempPortFile);

            var path = await client.CaptureActiveChatPathAsync();

            Assert.Null(path);
        }
        finally
        {
            if (File.Exists(tempPortFile)) File.Delete(tempPortFile);
        }
    }

    [Fact]
    public void BuildRestoreScript_ValidPath_GeneratesLocationAssign()
    {
        var script = AntigravityCdpClient.BuildRestoreScript("/c/123-abc?section=456");

        Assert.Contains("window.location.assign('/c/123-abc?section=456')", script);
        Assert.Contains("window.location.pathname", script);
        Assert.Contains("return true;", script);
    }

    [Fact]
    public void BuildRestoreScript_EscapesSpecialCharacters()
    {
        var script = AntigravityCdpClient.BuildRestoreScript(@"/c/test'quote\slash");

        Assert.Contains(@"window.location.assign('/c/test\'quote\\slash')", script);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-path")]
    [InlineData("/c/")]
    public async Task RestoreActiveChatAsync_InvalidPath_ReturnsFalse(string? path)
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var client = new AntigravityCdpClient(portFilePath: nonExistentPath);

        var result = await client.RestoreActiveChatAsync(path!);

        Assert.False(result);
    }

    [Fact]
    public void TriggerPostRestartActionsInBackground_DoesNotThrow()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var client = new AntigravityCdpClient(portFilePath: nonExistentPath);

        var ex1 = Record.Exception(() => client.TriggerPostRestartActionsInBackground(null));
        var ex2 = Record.Exception(() => client.TriggerPostRestartActionsInBackground("/c/valid-path-123"));
        var ex3 = Record.Exception(() => client.TriggerPostRestartActionsInBackground("/c/valid-path-123", autoContinue: true));

        Assert.Null(ex1);
        Assert.Null(ex2);
        Assert.Null(ex3);
    }

    [Fact]
    public void AutoContinueScript_ContainsCorrectSelectorsAndEventDispatch()
    {
        var script = AntigravityCdpClient.AutoContinueScript;
        Assert.Contains("Message input", script);
        Assert.Contains("contenteditable", script);
        Assert.Contains("Continue", script);
        Assert.Contains("insertText", script);
        Assert.Contains("Enter", script);
    }

    [Fact]
    public async Task AutoContinueChatAsync_WithMissingPort_ReturnsFalse()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var client = new AntigravityCdpClient(portFilePath: nonExistentPath);

        var result = await client.AutoContinueChatAsync();

        Assert.False(result);
    }
}


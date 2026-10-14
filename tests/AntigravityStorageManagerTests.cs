using System.IO;
using System.Text.Json.Nodes;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class AntigravityStorageManagerTests
{
    [Fact]
    public void TryPreAcceptOnboardingTerms_SetsKeysInAppStorageJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var storagePath = Path.Combine(tempDir, "app_storage.json");

        try
        {
            File.WriteAllText(storagePath, "{\"existingKey\":\"val\",\"jetski.onboarding.lastLoginUsername\":\"old@test.com\"}");

            var result = AntigravityStorageManager.TryPreAcceptOnboardingTerms("new@test.com", storagePath);

            Assert.True(result);
            var content = File.ReadAllText(storagePath);
            var node = JsonNode.Parse(content)?.AsObject();
            Assert.NotNull(node);
            Assert.Equal("val", node["existingKey"]?.ToString());
            Assert.Equal("new@test.com", node["jetski.onboarding.lastLoginUsername"]?.ToString());
            Assert.Equal("false", node["jetski.onboarding.lastLoginIsGcpTos"]?.ToString());
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TryPreAcceptOnboardingTerms_ReturnsFalseWhenEmailEmpty()
    {
        var result = AntigravityStorageManager.TryPreAcceptOnboardingTerms("  ", "any_path");
        Assert.False(result);
    }

    [Fact]
    public void TryPreAcceptOnboardingTerms_CreatesFileIfMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var storagePath = Path.Combine(tempDir, "app_storage.json");

        try
        {
            var result = AntigravityStorageManager.TryPreAcceptOnboardingTerms("user@domain.com", storagePath);

            Assert.True(result);
            Assert.True(File.Exists(storagePath));
            var content = File.ReadAllText(storagePath);
            var node = JsonNode.Parse(content)?.AsObject();
            Assert.NotNull(node);
            Assert.Equal("user@domain.com", node["jetski.onboarding.lastLoginUsername"]?.ToString());
            Assert.Equal("false", node["jetski.onboarding.lastLoginIsGcpTos"]?.ToString());
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TryPreAcceptOnboardingTerms_HandlesInvalidJsonGracefully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var storagePath = Path.Combine(tempDir, "app_storage.json");

        try
        {
            File.WriteAllText(storagePath, "not valid json {{{{");

            var result = AntigravityStorageManager.TryPreAcceptOnboardingTerms("user@domain.com", storagePath);

            Assert.True(result);
            var content = File.ReadAllText(storagePath);
            var node = JsonNode.Parse(content)?.AsObject();
            Assert.NotNull(node);
            Assert.Equal("user@domain.com", node["jetski.onboarding.lastLoginUsername"]?.ToString());
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

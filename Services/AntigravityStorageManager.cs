using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

public static class AntigravityStorageManager
{
    public const string OnboardingUsernameKey = "jetski.onboarding.lastLoginUsername";
    public const string OnboardingIsGcpTosKey = "jetski.onboarding.lastLoginIsGcpTos";

    public static string DefaultStorageFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Antigravity",
        "app_storage.json");

    /// <summary>
    /// Pre-populates the onboarding acceptance keys for the specified user email in Antigravity's app_storage.json.
    /// This prevents the "Security Notice &amp; Data Use" modal dialog from prompting on account switches.
    /// </summary>
    public static bool TryPreAcceptOnboardingTerms(string email, string? storageFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var path = storageFilePath ?? DefaultStorageFilePath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            JsonObject jsonObject;
            if (File.Exists(path))
            {
                try
                {
                    using var readStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var node = JsonNode.Parse(readStream);
                    jsonObject = node?.AsObject() ?? new JsonObject();
                }
                catch
                {
                    jsonObject = new JsonObject();
                }
            }
            else
            {
                jsonObject = new JsonObject();
            }

            jsonObject[OnboardingUsernameKey] = email.Trim();
            jsonObject[OnboardingIsGcpTosKey] = "false";

            var options = new JsonSerializerOptions { WriteIndented = true };
            using var writeStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new Utf8JsonWriter(writeStream, new JsonWriterOptions { Indented = true });
            jsonObject.WriteTo(writer);
            writer.Flush();

            return true;
        }
        catch
        {
            return false;
        }
    }
}

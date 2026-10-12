using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flow.Launcher.Plugin.AntigravitySwitcher.Models;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Cli;

public static class AccountParser
{
    private static readonly Regex ListRow = new(
        @"^(?<id>\S+)\s+(?<email>\S+)\s+(?<status>\S+)\s+(?<active>\*|)\s+(?<daily>\S+)\s+(?<weekly>\S+)\s*$",
        RegexOptions.Compiled);

    public static IReadOnlyList<AccountInfo> ParseListAccounts(string output)
    {
        var accounts = new List<AccountInfo>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return accounts;
        }

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("ID", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("-", StringComparison.Ordinal) ||
                line.StartsWith("No accounts", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = ListRow.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var daily = ParsePercent(match.Groups["daily"].Value);
            var weekly = ParsePercent(match.Groups["weekly"].Value);
            var remaining = PreferQuota(daily, weekly);

            accounts.Add(new AccountInfo
            {
                Id = match.Groups["id"].Value,
                Email = match.Groups["email"].Value,
                Status = match.Groups["status"].Value,
                IsActive = match.Groups["active"].Value == "*",
                RemainingPercent = remaining
            });
        }

        return accounts;
    }

    public static IReadOnlyList<AccountInfo> ParseApiAccounts(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var accounts = new List<AccountInfo>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var remaining = -1d;
            DateTimeOffset? resetAt = null;

            if (item.TryGetProperty("buckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
            {
                foreach (var bucket in buckets.EnumerateArray())
                {
                    var fraction = bucket.TryGetProperty("remaining_fraction", out var fractionEl) &&
                                   fractionEl.TryGetDouble(out var value)
                        ? value * 100d
                        : -1d;

                    if (fraction >= 0)
                    {
                        remaining = remaining < 0 ? fraction : Math.Min(remaining, fraction);
                    }

                    if (bucket.TryGetProperty("reset_time", out var resetEl) &&
                        resetEl.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(resetEl.GetString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var parsedReset) &&
                        parsedReset > DateTimeOffset.UnixEpoch.AddSeconds(1))
                    {
                        if (resetAt is null || parsedReset < resetAt)
                        {
                            resetAt = parsedReset;
                        }
                    }
                }
            }

            accounts.Add(new AccountInfo
            {
                Id = ReadString(item, "id"),
                Email = ReadString(item, "email"),
                Status = ReadString(item, "status"),
                IsActive = item.TryGetProperty("is_active", out var activeEl) && activeEl.ValueKind is JsonValueKind.True,
                RemainingPercent = remaining,
                ResetAt = resetAt
            });
        }

        return accounts;
    }

    public static int? ParseConfigPort(string output)
    {
        var text = output.Trim();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) &&
               port is > 0 and <= 65535
            ? port
            : null;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static double ParsePercent(string raw)
    {
        if (string.Equals(raw, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        var trimmed = raw.Trim().TrimEnd('%');
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : -1;
    }

    private static double PreferQuota(double daily, double weekly)
    {
        if (daily >= 0)
        {
            return daily;
        }

        return weekly;
    }
}

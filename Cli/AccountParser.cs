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
                RemainingPercent = remaining,
                // The CLI table labels its two columns DAILY / WEEKLY; the switcher's short
                // window is the 5-hour bucket, so map the first column to the "5hr" slot.
                FiveHourPercent = daily,
                WeeklyPercent = weekly
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
            var fiveHour = -1d;
            var weekly = -1d;
            DateTimeOffset? fiveHourReset = null;
            DateTimeOffset? weeklyReset = null;
            DateTimeOffset? earliestReset = null;

            if (item.TryGetProperty("buckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
            {
                foreach (var bucket in buckets.EnumerateArray())
                {
                    var fraction = bucket.TryGetProperty("remaining_fraction", out var fractionEl) &&
                                   fractionEl.TryGetDouble(out var value)
                        ? value * 100d
                        : -1d;

                    var reset = ReadReset(bucket);

                    if (reset is { } resetValue && (earliestReset is null || resetValue < earliestReset))
                    {
                        earliestReset = resetValue;
                    }

                    if (fraction < 0)
                    {
                        continue;
                    }

                    if (IsWeeklyWindow(bucket))
                    {
                        weekly = weekly < 0 ? fraction : Math.Min(weekly, fraction);
                        if (reset is { } w && (weeklyReset is null || w < weeklyReset))
                        {
                            weeklyReset = w;
                        }
                    }
                    else
                    {
                        fiveHour = fiveHour < 0 ? fraction : Math.Min(fiveHour, fraction);
                        if (reset is { } f && (fiveHourReset is null || f < fiveHourReset))
                        {
                            fiveHourReset = f;
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
                RemainingPercent = WorstKnown(fiveHour, weekly),
                ResetAt = earliestReset,
                FiveHourPercent = fiveHour,
                WeeklyPercent = weekly,
                FiveHourResetAt = fiveHourReset,
                WeeklyResetAt = weeklyReset
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

    private static bool IsWeeklyWindow(JsonElement bucket)
    {
        var window = ReadString(bucket, "window");
        if (window.Contains("week", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var id = ReadString(bucket, "bucket_id");
        return id.Contains("week", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ReadReset(JsonElement bucket)
    {
        if (bucket.TryGetProperty("reset_time", out var resetEl) &&
            resetEl.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(resetEl.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed) &&
            parsed > DateTimeOffset.UnixEpoch.AddSeconds(1))
        {
            return parsed;
        }

        return null;
    }

    private static double WorstKnown(double fiveHour, double weekly)
    {
        var values = new[] { fiveHour, weekly }.Where(v => v >= 0).ToList();
        return values.Count == 0 ? -1 : values.Min();
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

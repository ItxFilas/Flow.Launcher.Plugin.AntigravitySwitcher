using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class AccountParserTests
{
    // Mirrors the fixed-width table emitted by `antigravity-account-switcher list-accounts`:
    //   %-36s  %-30s  %-10s  %-8s  %-12s  %-12s
    private const string ListOutput =
        """
        ID                                    EMAIL                          STATUS      ACTIVE    DAILY QUOTA   WEEKLY QUOTA
        -------------------------------------------------------------------------------------------------------------
        3f2504e0-4f89-11d3-9a0c-0305e82c3301  alice@example.com              active      *         87%           92%
        9c858901-8a57-4791-81fe-4c455b099bc9  bob@example.com                exhausted              0%            41%
        """;

    [Fact]
    public void ParseListAccounts_ParsesRowsAndActiveFlag()
    {
        var accounts = AccountParser.ParseListAccounts(ListOutput);

        Assert.Equal(2, accounts.Count);

        var alice = accounts[0];
        Assert.Equal("3f2504e0-4f89-11d3-9a0c-0305e82c3301", alice.Id);
        Assert.Equal("alice@example.com", alice.Email);
        Assert.Equal("active", alice.Status);
        Assert.True(alice.IsActive);
        Assert.Equal(87, alice.RemainingPercent);
        Assert.Equal(87, alice.FiveHourPercent);
        Assert.Equal(92, alice.WeeklyPercent);

        var bob = accounts[1];
        Assert.Equal("bob@example.com", bob.Email);
        Assert.Equal("exhausted", bob.Status);
        Assert.False(bob.IsActive);
        Assert.Equal(0, bob.RemainingPercent);
        Assert.Equal(0, bob.FiveHourPercent);
        Assert.Equal(41, bob.WeeklyPercent);
    }

    [Fact]
    public void ParseListAccounts_FallsBackToWeeklyWhenDailyUnavailable()
    {
        const string output =
            """
            3f2504e0-4f89-11d3-9a0c-0305e82c3301  alice@example.com              active                 N/A           55%
            """;

        var accounts = AccountParser.ParseListAccounts(output);

        var account = Assert.Single(accounts);
        Assert.Equal(55, account.RemainingPercent);
        Assert.Equal(-1, account.FiveHourPercent);
        Assert.Equal(55, account.WeeklyPercent);
    }

    [Fact]
    public void ParseListAccounts_SkipsHeadersSeparatorsAndEmptyState()
    {
        const string output =
            """
            ID                                    EMAIL                          STATUS      ACTIVE    DAILY QUOTA   WEEKLY QUOTA
            -------------------------------------------------------------------------------------------------------------
            """;

        Assert.Empty(AccountParser.ParseListAccounts(output));
        Assert.Empty(AccountParser.ParseListAccounts("No accounts registered yet. Run 'add-account' to add one."));
        Assert.Empty(AccountParser.ParseListAccounts(string.Empty));
    }

    [Fact]
    public void ParseApiAccounts_ReadsFieldsBucketsAndEarliestReset()
    {
        const string json =
            """
            [
              {
                "id": "a1",
                "email": "alice@example.com",
                "status": "active",
                "is_active": true,
                "buckets": [
                  { "window": "DAILY",  "remaining_fraction": 0.8, "reset_time": "2026-01-01T10:00:00Z" },
                  { "window": "WEEKLY", "remaining_fraction": 0.5, "reset_time": "2026-01-05T10:00:00Z" }
                ]
              }
            ]
            """;

        var accounts = AccountParser.ParseApiAccounts(json);

        var account = Assert.Single(accounts);
        Assert.Equal("a1", account.Id);
        Assert.Equal("alice@example.com", account.Email);
        Assert.Equal("active", account.Status);
        Assert.True(account.IsActive);
        Assert.Equal(50, account.RemainingPercent);
        Assert.Equal(80, account.FiveHourPercent);
        Assert.Equal(50, account.WeeklyPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T10:00:00Z"), account.ResetAt);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T10:00:00Z"), account.FiveHourResetAt);
        Assert.Equal(DateTimeOffset.Parse("2026-01-05T10:00:00Z"), account.WeeklyResetAt);
    }

    [Fact]
    public void ParseApiAccounts_ReadsRealFiveHourAndWeeklyWindows()
    {
        const string json =
            """
            [
              {
                "id": "a4",
                "email": "dave@example.com",
                "status": "active",
                "is_active": true,
                "buckets": [
                  { "window": "5h",     "remaining_fraction": 0.250652,  "reset_time": "2026-10-12T20:13:53Z" },
                  { "window": "weekly", "remaining_fraction": 0.6759738, "reset_time": "2026-10-18T17:11:26Z" }
                ]
              }
            ]
            """;

        var account = Assert.Single(AccountParser.ParseApiAccounts(json));

        Assert.Equal(25.065, account.FiveHourPercent, 3);
        Assert.Equal(67.597, account.WeeklyPercent, 3);
        Assert.Equal(25.065, account.RemainingPercent, 3);
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T20:13:53Z"), account.FiveHourResetAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-18T17:11:26Z"), account.WeeklyResetAt);
    }

    [Fact]
    public void ParseApiAccounts_IgnoresZeroResetAndMissingBuckets()
    {
        const string json =
            """
            [
              {
                "id": "a2",
                "email": "bob@example.com",
                "status": "exhausted",
                "is_active": false,
                "buckets": [ { "remaining_fraction": 0.0, "reset_time": "0001-01-01T00:00:00Z" } ]
              },
              {
                "id": "a3",
                "email": "carol@example.com",
                "status": "active",
                "is_active": false
              }
            ]
            """;

        var accounts = AccountParser.ParseApiAccounts(json);

        Assert.Equal(2, accounts.Count);
        Assert.Equal(0, accounts[0].RemainingPercent);
        Assert.Null(accounts[0].ResetAt);
        Assert.Equal(-1, accounts[1].RemainingPercent);
        Assert.Null(accounts[1].ResetAt);
    }

    [Fact]
    public void ParseApiAccounts_ReturnsEmptyForNonArrayPayload()
    {
        Assert.Empty(AccountParser.ParseApiAccounts("{\"error\":\"nope\"}"));
    }

    [Theory]
    [InlineData("1831\n", 1831)]
    [InlineData("8080", 8080)]
    [InlineData("0", null)]
    [InlineData("99999", null)]
    [InlineData("not-a-port", null)]
    [InlineData("", null)]
    public void ParseConfigPort_ParsesValidPortsOnly(string output, int? expected)
    {
        Assert.Equal(expected, AccountParser.ParseConfigPort(output));
    }
}

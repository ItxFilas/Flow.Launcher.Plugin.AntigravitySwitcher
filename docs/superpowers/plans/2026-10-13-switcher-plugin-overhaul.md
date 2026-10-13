# Antigravity Switcher Plugin Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform the Antigravity Switcher Flow Launcher plugin with modern non-"AI slop" Fluent iconography, sub-millisecond query latency via `IAsyncPlugin` and in-memory path resolution, clean modular architecture, and visual Unicode quota progress indicators.

**Architecture:** Deconstruct the monolithic `Main.cs` into dedicated command routers, UI factories, and presentation formatters. Upgrade the plugin to Flow Launcher's `IAsyncPlugin` interface with stale-while-revalidate caching and concurrent CLI stream reading. Generate crisp 128×128 vector-drawn PNG assets with Fluent 2 design language.

**Tech Stack:** .NET 7.0 (`net7.0-windows`), C# 11/12, Flow.Launcher.Plugin v4.7.0, Xunit, System.Drawing.Common / PowerShell 7.

## Global Constraints

- Must target `net7.0-windows` and `Flow.Launcher.Plugin` 4.7.0 for Flow Launcher 1.20.x compatibility.
- Action keyword remains `ag`.
- Icon files must reside in `Images/` as PNGs with transparent backgrounds.
- All tests in `tests/` must pass cleanly without skipping or disabling existing assertions.
- Do not spawn external processes (`where.exe`, `wsl.exe`) on the hot query path.

---

### Task 1: Icon Generation Pipeline & Asset Overhaul

**Files:**
- Modify: `scripts/Generate-Icons.ps1`
- Output: `Images/app.png`, `Images/active.png`, `Images/standby.png`, `Images/exhausted.png`, `Images/next.png`, `Images/refresh.png`, `Images/add.png`, `Images/remove.png`
- Test: `tests/IconAssetTests.cs`

**Interfaces:**
- Produces: 8 valid 128×128 32-bit ARGB PNG icon files in `Images/`.

- [ ] **Step 1: Write the failing test for icon presence and dimensions**

Create `tests/IconAssetTests.cs`:
```csharp
using System.Drawing;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class IconAssetTests
{
    private static readonly string ImagesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Images"));

    [Theory]
    [InlineData("app.png")]
    [InlineData("active.png")]
    [InlineData("standby.png")]
    [InlineData("exhausted.png")]
    [InlineData("next.png")]
    [InlineData("refresh.png")]
    [InlineData("add.png")]
    [InlineData("remove.png")]
    public void Icon_Exists_AndHas128Resolution(string iconFileName)
    {
        var path = Path.Combine(ImagesDir, iconFileName);
        Assert.True(File.Exists(path), $"Icon file {iconFileName} should exist at {path}");

        using var img = Image.FromFile(path);
        Assert.Equal(128, img.Width);
        Assert.Equal(128, img.Height);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AntigravitySwitcher.sln --filter "FullyQualifiedName~IconAssetTests"`
Expected: FAIL because current icons are 64×64.

- [ ] **Step 3: Update `scripts/Generate-Icons.ps1` with 128×128 Fluent vector drawing logic**

Rewrite `scripts/Generate-Icons.ps1` to render high-DPI 128×128 glyphs:
- `app.png`: Dual concentric gravitational rings with indigo-to-cyan linear gradient and central nucleus on transparent canvas.
- `active.png`: Soft emerald rounded badge with clean checkmark lines.
- `standby.png`: Slate pill container with vertical pause bars.
- `exhausted.png`: Rounded amber/crimson triangle with exclamation mark.
- `next.png`: Azure/cyan fast-forward chevron.
- `refresh.png`: Dual-arc circular reload arrows with cyan accent.
- `add.png`: Clean emerald plus glyph.
- `remove.png`: Clean rose minus glyph.

Run: `pwsh ./scripts/Generate-Icons.ps1` to produce the new 128×128 icons.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AntigravitySwitcher.sln --filter "FullyQualifiedName~IconAssetTests"`
Expected: PASS (all 8 icons exist and are 128×128).

- [ ] **Step 5: Commit**

```bash
git add scripts/Generate-Icons.ps1 Images/*.png tests/IconAssetTests.cs
git commit -m "feat(icons): generate high-dpi 128x128 fluent vector icons"
```

---

### Task 2: Performance Optimizations in Core Utilities (`SwitcherLocator`, `CliRunner`, `DashboardClient`)

**Files:**
- Modify: `Cli/SwitcherLocator.cs:70-132`
- Modify: `Cli/CliRunner.cs:39-57`
- Modify: `Cli/DashboardClient.cs:10-35`
- Test: `tests/SwitcherLocatorTests.cs`

**Interfaces:**
- `SwitcherLocator.FindOnPath(string fileName)`: in-memory PATH scanning without `where.exe`.
- `CliRunner.RunAsync`: concurrent stdout/stderr reading with `Task.WhenAll`.
- `DashboardClient.IsReachableAsync(Uri, CancellationToken)`: probe with fast timeout.

- [ ] **Step 1: Write unit tests for in-memory PATH locator**

Create `tests/SwitcherLocatorTests.cs`:
```csharp
using System.IO;
using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class SwitcherLocatorTests
{
    [Fact]
    public void FindOnPath_FindsStandardExecutable_WithoutWhereExe()
    {
        // cmd.exe is guaranteed to exist on Windows PATH
        var path = SwitcherLocator.FindOnPath("cmd.exe");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void FindOnPath_ReturnsNullForNonExistentExecutable()
    {
        var path = SwitcherLocator.FindOnPath("non_existent_binary_xyz_123.exe");
        Assert.Null(path);
    }
}
```

- [ ] **Step 2: Run test to verify initial state or failure**

Run: `dotnet test AntigravitySwitcher.sln --filter "FullyQualifiedName~SwitcherLocatorTests"`

- [ ] **Step 3: Implement in-memory PATH scanning in `SwitcherLocator.cs`**

Modify `Cli/SwitcherLocator.cs`:
```csharp
    internal static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        var extensions = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? (Environment.GetEnvironmentVariable("PATHEXT")?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [".EXE", ".CMD", ".BAT"])
            : [""];

        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                var exact = Path.Combine(dir, fileName);
                if (File.Exists(exact))
                {
                    return exact;
                }

                if (!Path.HasExtension(fileName))
                {
                    foreach (var ext in extensions)
                    {
                        var candidate = Path.Combine(dir, fileName + ext);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
                // Ignore directory permission issues
            }
        }

        return null;
    }
```
Update `HasWsl` to be lazily probed and not block on startup.

- [ ] **Step 4: Fix stream reading deadlock in `Cli/CliRunner.cs`**

Modify `Cli/CliRunner.cs`:
```csharp
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout ?? _defaultTimeout);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(timeoutCts.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"Command '{argumentLine}' timed out.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new CliResult(process.ExitCode, stdout, stderr);
```

- [ ] **Step 5: Run tests to verify all tests pass**

Run: `dotnet test AntigravitySwitcher.sln`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Cli/SwitcherLocator.cs Cli/CliRunner.cs Cli/DashboardClient.cs tests/SwitcherLocatorTests.cs
git commit -m "perf(cli): in-memory path resolution and deadlock-safe stream reading"
```

---

### Task 3: Formatting & Presentation Decoupling (`AccountFormatter` & Visual Quota Bars)

**Files:**
- Create: `Services/AccountFormatter.cs`
- Modify: `Models/AccountInfo.cs`
- Test: `tests/AccountFormatterTests.cs`

**Interfaces:**
- Produces: `AccountFormatter.FormatQuotaBar(double percent)` -> `string`
- Produces: `AccountFormatter.FormatSubtitle(AccountInfo account)` -> `string`
- Produces: `AccountFormatter.FormatTitle(AccountInfo account)` -> `string`

- [ ] **Step 1: Write failing tests for `AccountFormatter`**

Create `tests/AccountFormatterTests.cs`:
```csharp
using Flow.Launcher.Plugin.AntigravitySwitcher.Models;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class AccountFormatterTests
{
    [Theory]
    [InlineData(100, "▰▰▰▰▰")]
    [InlineData(80, "▰▰▰▰▱")]
    [InlineData(60, "▰▰▰▱▱")]
    [InlineData(40, "▰▰▱▱▱")]
    [InlineData(20, "▰▱▱▱▱")]
    [InlineData(0, "▱▱▱▱▱")]
    [InlineData(-1, "")]
    public void FormatQuotaBar_RendersExpectedUnicodeBlocks(double percent, string expected)
    {
        Assert.Equal(expected, AccountFormatter.FormatQuotaBar(percent));
    }

    [Fact]
    public void FormatTitle_IncludesActiveTagAndQuotaBar()
    {
        var account = new AccountInfo
        {
            Email = "dev@example.com",
            IsActive = true,
            FiveHourPercent = 80,
            WeeklyPercent = 90
        };

        var title = AccountFormatter.FormatTitle(account);
        Assert.Contains("dev@example.com", title);
        Assert.Contains("[ACTIVE]", title);
        Assert.Contains("▰▰▰▰▱", title);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AntigravitySwitcher.sln --filter "FullyQualifiedName~AccountFormatterTests"`
Expected: FAIL with "AccountFormatter does not exist".

- [ ] **Step 3: Implement `AccountFormatter.cs`**

Create `Services/AccountFormatter.cs`:
```csharp
using Flow.Launcher.Plugin.AntigravitySwitcher.Models;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

public static class AccountFormatter
{
    public static string FormatQuotaBar(double percent)
    {
        if (percent < 0) return string.Empty;
        var filled = (int)Math.Round(Math.Clamp(percent, 0, 100) / 20.0);
        return new string('▰', filled) + new string('▱', 5 - filled);
    }

    public static string FormatPercent(double percent) => percent < 0 ? "N/A" : $"{percent:0}%";

    public static string FormatTitle(AccountInfo account)
    {
        var baseName = account.DisplayName;
        var activeTag = account.IsActive ? " [ACTIVE]" : "";
        var bar = FormatQuotaBar(account.RemainingPercent);
        var quotaSummary = string.IsNullOrEmpty(bar) ? "" : $"  {bar} {FormatPercent(account.RemainingPercent)}";
        return $"{baseName}{activeTag}{quotaSummary}";
    }

    public static string FormatSubtitle(AccountInfo account)
    {
        var state = account.StateLabel;
        var fiveHour = $"5hr {FormatPercent(account.FiveHourPercent)}{FormatReset(account.FiveHourResetAt)}";
        var weekly = $"week {FormatPercent(account.WeeklyPercent)}{FormatReset(account.WeeklyResetAt)}";
        return $"{state} · {fiveHour} · {weekly}";
    }

    private static string FormatReset(DateTimeOffset? resetAt)
    {
        if (resetAt is null) return string.Empty;
        var remaining = resetAt.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return " (resets now)";
        if (remaining.TotalHours >= 24) return $" (resets {remaining.TotalDays:0}d)";
        if (remaining.TotalHours >= 1) return $" (resets {remaining.Hours}h{remaining.Minutes:00}m)";
        return $" (resets {remaining.Minutes}m)";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AntigravitySwitcher.sln --filter "FullyQualifiedName~AccountFormatterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Services/AccountFormatter.cs tests/AccountFormatterTests.cs
git commit -m "feat(formatting): add AccountFormatter with unicode quota bars"
```

---

### Task 4: Stale-While-Revalidate Caching & Backend Restart Feature

**Files:**
- Modify: `Services/AccountCache.cs`
- Modify: `Services/AccountService.cs`
- Modify: `Services/AntigravityAppController.cs`
- Test: `tests/AccountCacheTests.cs`

**Interfaces:**
- `AccountCache.TryGet(out IReadOnlyList<AccountInfo>, out bool isStale)`
- `AccountService.RestartBackendOnlyAsync(CancellationToken)`

- [ ] **Step 1: Write test for stale-while-revalidate in `AccountCacheTests.cs`**

Modify `tests/AccountCacheTests.cs` to test that stale items can be retrieved even when expired, flagging `isStale = true`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AntigravitySwitcher.sln --filter "FullyQualifiedName~AccountCacheTests"`

- [ ] **Step 3: Implement stale-while-revalidate in `AccountCache.cs` and restart helper in `AccountService.cs`**

Modify `Services/AccountCache.cs`:
```csharp
    public bool TryGet(out IReadOnlyList<AccountInfo> accounts, out bool isStale)
    {
        lock (_gate)
        {
            if (_accounts is not null)
            {
                accounts = _accounts;
                isStale = DateTimeOffset.UtcNow >= _expiresAt;
                return true;
            }
        }

        accounts = [];
        isStale = false;
        return false;
    }
```

Add `RestartBackendOnlyAsync` to `AccountService.cs`:
```csharp
    public async Task<bool> RestartBackendOnlyAsync(CancellationToken cancellationToken = default)
    {
        return await _antigravityApp.RestartForLoginSwapAsync(cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AntigravitySwitcher.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Services/AccountCache.cs Services/AccountService.cs tests/AccountCacheTests.cs
git commit -m "feat(cache): implement stale-while-revalidate and backend restart helper"
```

---

### Task 5: Query Routing & UI Builders (`QueryRouter` & `ResultFactory`)

**Files:**
- Create: `UI/ResultFactory.cs`
- Create: `Routing/QueryRouter.cs`
- Test: `tests/QueryRouterTests.cs`

**Interfaces:**
- Produces: `QueryRouter.RouteAsync(Query query, CancellationToken token)` -> `Task<List<Result>>`
- Produces: `ResultFactory.CreateAccountResult(AccountInfo account, ...)` -> `Result`

- [ ] **Step 1: Write failing tests for command routing in `QueryRouterTests.cs`**

Create `tests/QueryRouterTests.cs`:
```csharp
using Flow.Launcher.Plugin.AntigravitySwitcher.Routing;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class QueryRouterTests
{
    [Theory]
    [InlineData("", CommandKind.ListAccounts)]
    [InlineData("add", CommandKind.Add)]
    [InlineData("remove", CommandKind.Remove)]
    [InlineData("refresh", CommandKind.Refresh)]
    [InlineData("status", CommandKind.Status)]
    [InlineData("restart", CommandKind.Restart)]
    [InlineData("help", CommandKind.Help)]
    [InlineData("google@gmail.com", CommandKind.ListAccounts)]
    public void ParseCommand_IdentifiesCommandKind(string queryText, CommandKind expected)
    {
        var parsed = QueryRouter.ParseCommand(queryText);
        Assert.Equal(expected, parsed.Kind);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AntigravitySwitcher.sln --filter "FullyQualifiedName~QueryRouterTests"`
Expected: FAIL with missing `QueryRouter`.

- [ ] **Step 3: Implement `UI/ResultFactory.cs` and `Routing/QueryRouter.cs`**

Create `UI/ResultFactory.cs` (encapsulating all Result item generation with scores, icons, actions, context data).
Create `Routing/QueryRouter.cs` (handling query dispatch, fuzzy filtering using `StringMatcher.FuzzySearch`, and action execution).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AntigravitySwitcher.sln --filter "FullyQualifiedName~QueryRouterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add UI/ResultFactory.cs Routing/QueryRouter.cs tests/QueryRouterTests.cs
git commit -m "feat(routing): add QueryRouter and ResultFactory with fuzzy search"
```

---

### Task 6: Native `IAsyncPlugin` Integration in `Main.cs` & Quality Verification

**Files:**
- Modify: `Main.cs`
- Modify: `plugin.json`
- Test: Full solution build & test suite

**Interfaces:**
- `Main : IAsyncPlugin, IContextMenu, IReloadable`

- [ ] **Step 1: Refactor `Main.cs` into clean facade implementing `IAsyncPlugin`**

```csharp
public sealed class Main : IAsyncPlugin, IContextMenu, IReloadable
{
    private PluginInitContext _context = null!;
    private AccountService _service = null!;
    private QueryRouter _router = null!;

    public Task InitAsync(PluginInitContext context)
    {
        _context = context;
        var locator = new SwitcherLocator();
        var runner = new CliRunner(locator);
        _service = new AccountService(runner, new DashboardClient(), new AccountCache());
        _router = new QueryRouter(_service, _context);
        return Task.CompletedTask;
    }

    public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        return await _router.RouteAsync(query, token).ConfigureAwait(false);
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        return _router.LoadContextMenus(selectedResult);
    }

    public void ReloadData()
    {
        _service.InvalidateCache();
    }
}
```

- [ ] **Step 2: Run full test suite**

Run: `dotnet test AntigravitySwitcher.sln -c Release`
Expected: 100% PASS, 0 failures.

- [ ] **Step 3: Run build and publish verification script**

Run: `pwsh ./scripts/build.ps1`
Verify that `publish/` contains:
- `Flow.Launcher.Plugin.AntigravitySwitcher.dll`
- `plugin.json`
- `Images/app.png` (128×128)
- All required dependency DLLs.

- [ ] **Step 4: Commit and finalize**

```bash
git add Main.cs plugin.json
git commit -m "feat(plugin): integrate IAsyncPlugin and complete overhaul"
```

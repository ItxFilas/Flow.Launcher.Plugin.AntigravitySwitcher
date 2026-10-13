# Antigravity Switcher Plugin — Overhaul Design Specification

**Date:** 2026-10-13  
**Status:** Approved  
**Author:** Pair Programming Agent & User  
**Target Architecture:** .NET 7.0 (net7.0-windows) / Flow Launcher Plugin v4.7.0  

---

## 1. Executive Summary

The Antigravity Switcher is a Flow Launcher plugin (`Flow.Launcher.Plugin.AntigravitySwitcher`) that manages and switches Google accounts for the Antigravity IDE using the `antigravity-account-switcher` daemon/CLI.

This specification details a complete overhaul addressing four critical areas:
1. **Modern Iconography:** Replacing rudimentary GDI+ flat colored discs with high-resolution (128×128), Fluent-inspired vector glyphs and celestial orbital branding.
2. **Performance & Instant Latency:** Eliminating external process spawning for binary discovery (`where.exe`, `wsl.exe`), speeding up localhost daemon probes (from 3000ms to 350ms), adopting stale-while-revalidate caching, and preventing stream buffer deadlocks.
3. **Clean Codebase Architecture:** Migrating from synchronous `IPlugin` to native `IAsyncPlugin`, decoupling the 518-line `Main.cs` into distinct routers, formatters, and UI builders, and separating presentation logic from data models.
4. **Feature Enhancements:** Adding visual Unicode quota progress bars, Flow Launcher fuzzy matching, and an `ag restart` command to quickly refresh the IDE language server without switching accounts.

---

## 2. Iconography & Visual Asset Pipeline

### 2.1 Problem Statement
The existing icon generation script (`scripts/Generate-Icons.ps1`) produces 64×64 pixel flat discs with solid fill colors (blue, green, red, slate) and simplistic vector primitives drawn via System.Drawing (GDI+). The main app icon is a rounded rectangle with a standard font "A" on a 45-degree gradient. The results look like low-quality clip-art / AI slop.

### 2.2 Design System & Specifications
* **Canvas:** 128×128 pixels, 32-bit ARGB with anti-aliasing and transparent backgrounds.
* **Style:** Modern Fluent 2 / IDE iconography. Balanced stroke widths (5–7px at 128px resolution), round line caps, subtle gradients, and clean geometric curves.
* **Palette:**
  * Primary / Cosmic Brand: Deep Indigo (`#4F46E5`), Electric Cyan (`#06B6D4`), Royal Violet (`#7C3AED`).
  * Active / Success: Emerald Green (`#10B981`).
  * Standby / Neutral: Slate Grey (`#64748B`).
  * Warning / Exhausted: Amber/Rose (`#F43F5E` / `#F59E0B`).
  * Action / Next: Electric Cyan (`#06B6D4`) and Azure (`#3B82F6`).

### 2.3 Asset Inventory
| File | Role | Visual Concept |
| :--- | :--- | :--- |
| `Images/app.png` | Main plugin & toast icon | Stylized orbital ring / gravitational vortex with cyan-to-violet gradient and central core. |
| `Images/active.png` | Currently logged-in account | Clean rounded shield/circle with an emerald checkmark. |
| `Images/standby.png` | Healthy standby account | Minimalist dual vertical pill bars in refined slate. |
| `Images/exhausted.png` | Exhausted quota / error | Modern equilateral rounded hazard triangle with soft crimson exclamation mark. |
| `Images/next.png` | Quick-switch action | Dual fast-forward / chevron arrows with cyan accent. |
| `Images/refresh.png` | Refresh quotas | High-precision dual circular arrows with rounded caps. |
| `Images/add.png` | Add new Google account | Clean balanced plus glyph in emerald. |
| `Images/remove.png` | Remove account | Clean minus / delete badge in soft crimson. |

---

## 3. Performance & Speed Optimizations

### 3.1 In-Memory Path Resolution (`SwitcherLocator`)
* **Current:** Uses `where.exe` via `Process.Start` to find native binaries and `wsl.exe -e true` to test WSL availability. Spawning these processes takes 200ms–2000ms.
* **Optimized:**
  1. Check explicit environment variable `ANTIGRAVITY_SWITCHER_BIN`.
  2. Check known local paths (`~/.local/bin`, `%LOCALAPPDATA%`, `~/go/bin`).
  3. Scan the `PATH` environment variable in-memory via `Environment.GetEnvironmentVariable("PATH").Split(';')` and check `File.Exists` for each directory (< 0.1ms total execution time).
  4. Only test WSL if native binaries are completely absent, caching results with a long TTL (10 minutes) and resolving asynchronously where possible.

### 3.2 Localhost Daemon Health Check Timeout
* **Current:** `HttpClient` uses a 3-second timeout for localhost port probes. Probing ports 8080 and 1831 can block for up to 6 seconds if daemon is down.
* **Optimized:** Daemon probes on `127.0.0.1` use an explicit `CancellationTokenSource` with a **350ms** timeout. Localhost HTTP handshakes take < 5ms when listening; 350ms is more than sufficient and eliminates perceptible latency when falling back to CLI.

### 3.3 Stale-While-Revalidate Caching Pattern
* **Current:** When the 8-second cache expires, queries return a single dummy `"Loading accounts…"` result and queue a background requery.
* **Optimized:**
  * Cache holds previous accounts for up to 5 minutes as fallback data.
  * If cached accounts are older than 10 seconds, `QueryAsync` immediately serves the existing cached accounts (0ms perceived latency) while kicking off a silent background update.
  * Only when the cache is completely empty (e.g., initial startup) is an async load awaited.

### 3.4 Process Stream Deadlock Prevention (`CliRunner`)
* **Current:** `process.WaitForExitAsync(token)` is awaited before reading stdout and stderr, which can cause a buffer deadlock if command output exceeds OS buffer limits (64KB).
* **Optimized:** Launch `ReadToEndAsync` on both streams concurrently, awaiting `Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(token))`.

---

## 4. Codebase Architecture & Refactoring

### 4.1 Native `IAsyncPlugin` Integration
* `Main` implements:
  * `IAsyncPlugin`: `Task InitAsync(PluginInitContext context)`, `Task<List<Result>> QueryAsync(Query query, CancellationToken token)`.
  * `IContextMenu`: `List<Result> LoadContextMenus(Result selectedResult)`.
  * `IReloadable`: `void ReloadData()` to invalidate caches on user request.
* Asynchronous Flow Launcher queries receive a `CancellationToken` on every keystroke, aborting abandoned queries instantly.

### 4.2 Modular Separation of Concerns
The current 518-line `Main.cs` is divided into cohesive classes:
* `Main.cs` (~90 lines): Plugin entrypoint and lifecycle coordinator.
* `Routing/QueryRouter.cs`: Parses commands (`add`, `remove`, `refresh`, `status`, `restart`, `help`, account filtering) and routes to specialized handler methods.
* `UI/ResultFactory.cs`: Factory methods creating `Result` items for accounts, quick switch, status, hints, and errors.
* `Formatting/AccountFormatter.cs`: Pure formatting helper for quota percentages, reset countdowns, and Unicode progress bars.
* `Models/AccountInfo.cs`: Clean domain model free of presentation logic.

### 4.3 Git Debt Reconciliation
* Unify and commit unstaged changes in `DashboardClient.cs`, `tests/DashboardClientTests.cs`, and `tests/AccountServiceTests.cs`.
* Ensure `AntigravityAppController.cs` is properly included in version control and covered by unit tests.
* Consolidate URI resolution into a single canonical `ResolveApiUriAsync`.

---

## 5. Feature Enhancements

### 5.1 Visual Unicode Quota Indicators
Every account result title displays an inline quota bar showing the health of the 5-hour and weekly windows:
```
example@gmail.com [ACTIVE]  ▰▰▰▰▱ 80% (5h) · ▰▰▰▰▰ 95% (wk)
```
Unicode bar helper generates 5-block indicators (`▰` filled, `▱` empty) mapped from quota percentage.

### 5.2 Fuzzy Search Matching
Leverage Flow Launcher's `StringMatcher.FuzzySearch` to score account matches against both email and ID, enabling effortless filtering with abbreviations or prefixes (e.g., `ag wk` matching `work.account@gmail.com`).

### 5.3 Quick IDE Backend Restart (`ag restart`)
Adds an `ag restart` command to Flow Launcher:
* Immediately restarts Antigravity's `language_server` background process without triggering a full account switch.
* Useful for developers when language server indexing freezes or gets stuck.

---

## 6. Testing & Quality Assurance Plan

1. **Unit Tests (`Flow.Launcher.Plugin.AntigravitySwitcher.Tests`):**
   * Update existing tests (55 passing) to reflect new formatters and routers.
   * Add test coverage for `AccountFormatter` (quota bars, reset formatting).
   * Add test coverage for `QueryRouter` command parsing and routing.
   * Add test coverage for `SwitcherLocator` in-memory PATH lookup.
2. **Build & Release Validation:**
   * Build project using `dotnet build AntigravitySwitcher.sln -c Release`.
   * Run full test suite with `dotnet test AntigravitySwitcher.sln -c Release`.
   * Publish using `pwsh ./scripts/build.ps1` and verify publish output matches Flow Launcher folder structure.

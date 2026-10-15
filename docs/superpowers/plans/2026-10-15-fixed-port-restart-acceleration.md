# Fixed Port (JETSKI_LS_PORT) & V8 Code Cache Acceleration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Accelerate Antigravity restart by configuring a persistent fixed port via `JETSKI_LS_PORT`, enabling Chromium to preserve origin-bound V8 bytecode caches and TLS session reuse across backend restarts.

**Architecture:** Antigravity's `app.asar` already checks `process.env.JETSKI_LS_PORT` before falling back to `constants.DYNAMIC_PORT` (0). By configuring a persistent high port (`51950`) in user environment variables and injecting it during process launch, the Go language server repeatedly binds to the exact same HTTPS origin (`https://127.0.0.1:51950/`). This eliminates the 2.8s ephemeral port TLS handshake delay and allows Chromium to immediately reuse its compiled V8 bytecode cache.

**Tech Stack:** C# (.NET 10), Flow Launcher Plugin API, Windows Process Environment API, xUnit.

## Global Constraints

- Never break on Antigravity application updates: all features must function gracefully even if environment variables are unavailable or reverted.
- Non-blocking: background port configuration and process launches must never stall Flow Launcher UI.
- Zero-dependency: Pure .NET BCL and `Flow.Launcher.Plugin` only; no external NuGet packages.
- Strict-mode compliance: Maintain existing test coverage (all 184 tests must pass).

---

### Task 1: Fixed Port Configuration & Environment Injection in AntigravityAppController

**Files:**
- Modify: `Services/AntigravityAppController.cs`
- Modify: `tests/AntigravityAppControllerTests.cs`

**Interfaces:**
- Consumes: `ProcessStartInfo`, `Environment.SetEnvironmentVariable`, `Environment.GetEnvironmentVariable`
- Produces: `AntigravityAppController.EnsurePortConfigured(int? port = null)`, `AntigravityAppController.DefaultJetskiPort`

- [ ] **Step 1: Write the failing tests in `tests/AntigravityAppControllerTests.cs`**

```csharp
[Fact]
public void EnsurePortConfigured_SetsProcessAndUserEnvironmentVariable()
{
    var original = Environment.GetEnvironmentVariable(AntigravityAppController.JetskiPortEnvVar);
    try
    {
        Environment.SetEnvironmentVariable(AntigravityAppController.JetskiPortEnvVar, null);
        var configured = AntigravityAppController.EnsurePortConfigured(51950, setProcessOnly: true);

        Assert.True(configured);
        Assert.Equal("51950", Environment.GetEnvironmentVariable(AntigravityAppController.JetskiPortEnvVar));
    }
    finally
    {
        Environment.SetEnvironmentVariable(AntigravityAppController.JetskiPortEnvVar, original);
    }
}

[Fact]
public async Task LaunchAsync_InjectsJetskiPortIntoProcessStartInfo()
{
    AntigravityAppController.ResetCachedExePath();
    var tempExe = Path.GetTempFileName();
    ProcessStartInfo? capturedStartInfo = null;

    var controller = new AntigravityAppController(
        new CliRunner(new SwitcherLocator()),
        processLauncher: psi => capturedStartInfo = psi);

    try
    {
        Environment.SetEnvironmentVariable("ANTIGRAVITY_BIN", tempExe);
        var result = await controller.RestartAppAsync();

        Assert.True(result);
        Assert.NotNull(capturedStartInfo);
        Assert.True(capturedStartInfo.EnvironmentVariables.ContainsKey(AntigravityAppController.JetskiPortEnvVar));
        Assert.Equal(AntigravityAppController.DefaultJetskiPort.ToString(), capturedStartInfo.EnvironmentVariables[AntigravityAppController.JetskiPortEnvVar]);
    }
    finally
    {
        Environment.SetEnvironmentVariable("ANTIGRAVITY_BIN", null);
        AntigravityAppController.ResetCachedExePath();
        if (File.Exists(tempExe)) File.Delete(tempExe);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityAppControllerTests`
Expected: FAIL due to missing `JetskiPortEnvVar`, `DefaultJetskiPort`, and `EnsurePortConfigured`.

- [ ] **Step 3: Implement port configuration in `Services/AntigravityAppController.cs`**

Add constants and methods:
```csharp
public const string JetskiPortEnvVar = "JETSKI_LS_PORT";
public const int DefaultJetskiPort = 51950;

public static bool EnsurePortConfigured(int? port = null, bool setProcessOnly = false)
{
    try
    {
        var targetPort = port ?? DefaultJetskiPort;
        var portStr = targetPort.ToString();
        var current = Environment.GetEnvironmentVariable(JetskiPortEnvVar);

        if (current == portStr)
        {
            return false;
        }

        Environment.SetEnvironmentVariable(JetskiPortEnvVar, portStr);

        if (!setProcessOnly)
        {
            try
            {
                Environment.SetEnvironmentVariable(JetskiPortEnvVar, portStr, EnvironmentVariableTarget.User);
            }
            catch
            {
                // Non-fatal if user env variable cannot be written
            }
        }

        return true;
    }
    catch
    {
        return false;
    }
}
```

In `LaunchAsync`, populate `startInfo.EnvironmentVariables`:
```csharp
startInfo.EnvironmentVariables[JetskiPortEnvVar] = (port ?? DefaultJetskiPort).ToString();
```

In `RestartForLoginSwapAsync`, ensure port is configured:
```csharp
var portJustConfigured = EnsurePortConfigured();
// If port was just configured for the first time, restart the app once so Electron inherits it
if (wasJustOptimized || isProcessStale || portJustConfigured)
{
    return await RestartAppAsync(savedChatPath, cancellationToken).ConfigureAwait(false);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityAppControllerTests`
Expected: PASS.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test .\AntigravitySwitcher.sln -c Release`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add Services/AntigravityAppController.cs tests/AntigravityAppControllerTests.cs
git commit -m "feat(perf): configure persistent fixed port via JETSKI_LS_PORT for V8 cache reuse"
```

---

### Task 2: End-to-End Build, Test Verification & Deployment

**Files:**
- None (deployment and verification scripts)

**Interfaces:**
- Consumes: `pwsh ./scripts/build.ps1 -Deploy`, `Stop-Process -Name 'Flow.Launcher'`
- Produces: Deployed plugin binary in `%APPDATA%\FlowLauncher\Plugins\AntigravitySwitcher`

- [ ] **Step 1: Run full release build and tests**

Run: `dotnet test .\AntigravitySwitcher.sln -c Release`
Expected: 186+ tests passed, 0 failed.

- [ ] **Step 2: Deploy to Flow Launcher**

Run:
```powershell
Stop-Process -Name 'Flow.Launcher' -Force -ErrorAction SilentlyContinue
pwsh ./scripts/build.ps1 -Deploy
```

- [ ] **Step 3: Relaunch Flow Launcher**

Run:
```powershell
schtasks /create /tn "LaunchFlowLauncherTemp" /tr "C:\Users\dlest\AppData\Local\FlowLauncher\app-1.20.2\Flow.Launcher.exe" /sc ONCE /st 00:00 /f /it
schtasks /run /tn "LaunchFlowLauncherTemp"
Start-Sleep -Seconds 2
schtasks /delete /tn "LaunchFlowLauncherTemp" /f
```

- [ ] **Step 4: Verify deployment and process status**

Run: `Get-Process -Name 'Flow.Launcher'`
Expected: Process running in interactive session.

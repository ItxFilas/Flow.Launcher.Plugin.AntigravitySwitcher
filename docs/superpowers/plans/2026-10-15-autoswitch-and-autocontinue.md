# Auto-Switch Failover Toggle & Post-Restart Auto-Continue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate the 429 failover proxy into the Flow Launcher plugin with an ON/OFF toggle (`ag auto`), and automatically submit "Continue" after account switch/restart so stopped chat sessions immediately resume execution.

**Architecture:** 
1. **Auto-Switch Proxy**: Controlled via `ag auto` (toggle), `ag auto on`, `ag auto off`. When ON, it configures `HTTPS_PROXY=http://127.0.0.1:8080` (and `HTTP_PROXY`, `NO_PROXY`), ensures the background supervisor daemon is running, and injects proxy settings on app launch so the built-in `failoverEngine` intercepts 429 quota exhaustion and rotates accounts in memory.
2. **Auto-Continue via CDP**: `AntigravityCdpClient` gains an `AutoContinueChatAsync` method invoked after chat restoration. It detects if the conversation is idle/waiting, looks for any "Continue"/"Retry"/"Resume" button to click, or focuses `[aria-label="Message input"]`, enters "Continue", and submits it so the stopped task resumes immediately.

**Tech Stack:** C# (.NET 10), Flow Launcher Plugin API, Chrome DevTools Protocol (CDP), WebSocket, xUnit.

## Global Constraints

- Never break on Antigravity updates: selectors and CDP operations must be defensive and non-fatal.
- Non-blocking: background CDP actions and proxy toggles must never block Flow Launcher UI.
- Zero-dependency: Pure .NET BCL and `Flow.Launcher.Plugin` only; no external NuGet packages.
- Strict-mode compliance: Maintain existing test coverage (all 184+ tests must pass).

---

### Task 1: Auto-Continue and Send in AntigravityCdpClient

**Files:**
- Modify: `Services/AntigravityCdpClient.cs`
- Modify: `tests/AntigravityCdpClientTests.cs`

**Interfaces:**
- Consumes: `ClientWebSocket`, `Runtime.evaluate`
- Produces: `IAntigravityCdpClient.AutoContinueChatAsync(CancellationToken cancellationToken = default)`, updated `TriggerPostRestartActionsInBackground(string? savedChatPath = null, bool autoContinue = true)`

- [ ] **Step 1: Write the failing tests in `tests/AntigravityCdpClientTests.cs`**

```csharp
[Fact]
public void AutoContinueScript_ContainsCorrectSelectorsAndEventDispatch()
{
    var script = AntigravityCdpClient.AutoContinueScript;
    Assert.Contains("Message input", script);
    Assert.Contains("Continue", script);
    Assert.Contains("insertText", script);
    Assert.Contains("Enter", script);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityCdpClientTests`
Expected: FAIL due to missing `AutoContinueScript` and `AutoContinueChatAsync`.

- [ ] **Step 3: Implement `AutoContinueScript` and `AutoContinueChatAsync` in `Services/AntigravityCdpClient.cs`**

Add `AutoContinueScript`:
```javascript
(() => {
    // 1. If there's an explicit Continue / Retry / Resume button, click it
    const buttons = Array.from(document.querySelectorAll('button'));
    const retryBtn = buttons.find(b => {
        const t = (b.innerText || b.textContent || b.getAttribute('aria-label') || '').toLowerCase();
        return (t.includes('continue') || t.includes('retry') || t.includes('resume')) && !b.disabled;
    });
    if (retryBtn) {
        retryBtn.click();
        return true;
    }

    // 2. Find message input box
    const input = document.querySelector('[aria-label="Message input"]') ||
                  document.querySelector('[contenteditable="true"]');
    if (!input) return false;

    // Check if agent is currently running (Cancel button present)
    const isRunning = buttons.some(b => {
        const aria = (b.getAttribute('aria-label') || '').toLowerCase();
        return aria.includes('cancel') || aria.includes('stop');
    });
    if (isRunning) return false;

    // 3. Focus, type 'Continue', and submit
    input.focus();
    document.execCommand('insertText', false, 'Continue');

    // 4. Submit via Enter keydown or clicking send button
    const enterEvent = new KeyboardEvent('keydown', {
        key: 'Enter',
        code: 'Enter',
        keyCode: 13,
        which: 13,
        bubbles: true,
        cancelable: true
    });
    input.dispatchEvent(enterEvent);

    // Also look for send button in case Enter is prevented
    const sendBtn = Array.from(input.parentElement?.parentElement?.querySelectorAll('button') || [])
        .find(b => !b.disabled && (b.getAttribute('aria-label') || '').toLowerCase().includes('send'));
    if (sendBtn) {
        sendBtn.click();
    }

    return true;
})()
```

Implement `AutoContinueChatAsync`:
```csharp
public async Task<bool> AutoContinueChatAsync(CancellationToken cancellationToken = default)
{
    var port = ResolveActivePort();
    if (port is null) return false;

    try
    {
        var targetsJson = await _http.GetStringAsync($"http://127.0.0.1:{port}/json", cancellationToken).ConfigureAwait(false);
        var target = ParsePageTarget(targetsJson);
        if (target?.WebSocketDebuggerUrl is null) return false;

        using var ws = new ClientWebSocket();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(TimeSpan.FromSeconds(2));
        await ws.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), connectCts.Token).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(new
        {
            id = 4,
            method = "Runtime.evaluate",
            @params = new
            {
                expression = AutoContinueScript,
                returnByValue = true
            }
        });

        var bytes = Encoding.UTF8.GetBytes(payload);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        return true;
    }
    catch
    {
        return false;
    }
}
```

Update `TriggerPostRestartActionsInBackground`:
After `RestoreActiveChatAsync` and delay, invoke `AutoDismissSecurityNoticeAsync` and `AutoContinueChatAsync`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityCdpClientTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Services/AntigravityCdpClient.cs tests/AntigravityCdpClientTests.cs
git commit -m "feat(cdp): add auto-continue and send post-restart chat recovery"
```

---

### Task 2: Auto-Switch Proxy Manager & Environment Wiring in AntigravityAppController

**Files:**
- Modify: `Services/AntigravityAppController.cs`
- Modify: `tests/AntigravityAppControllerTests.cs`

**Interfaces:**
- Consumes: `Environment.GetEnvironmentVariable`, `Environment.SetEnvironmentVariable`
- Produces: `AntigravityAppController.IsAutoSwitchEnabled()`, `AntigravityAppController.SetAutoSwitch(bool enabled)`, updated `LaunchAsync`

- [ ] **Step 1: Write failing tests in `tests/AntigravityAppControllerTests.cs`**

```csharp
[Fact]
public void SetAutoSwitch_TogglesProxyEnvironmentVariables()
{
    try
    {
        AntigravityAppController.SetAutoSwitch(true, setProcessOnly: true);
        Assert.True(AntigravityAppController.IsAutoSwitchEnabled());
        Assert.Equal("http://127.0.0.1:8080", Environment.GetEnvironmentVariable("https_proxy"));

        AntigravityAppController.SetAutoSwitch(false, setProcessOnly: true);
        Assert.False(AntigravityAppController.IsAutoSwitchEnabled());
        Assert.Null(Environment.GetEnvironmentVariable("https_proxy"));
    }
    finally
    {
        Environment.SetEnvironmentVariable("https_proxy", null);
        Environment.SetEnvironmentVariable("http_proxy", null);
        Environment.SetEnvironmentVariable("no_proxy", null);
        Environment.SetEnvironmentVariable("ANTIGRAVITY_AUTOSWITCH", null);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityAppControllerTests`
Expected: FAIL due to missing `IsAutoSwitchEnabled` and `SetAutoSwitch`.

- [ ] **Step 3: Implement `IsAutoSwitchEnabled` and `SetAutoSwitch` in `Services/AntigravityAppController.cs`**

```csharp
public const string AutoSwitchEnvVar = "ANTIGRAVITY_AUTOSWITCH";
public const string ProxyUrl = "http://127.0.0.1:8080";
public const string NoProxyList = "localhost,127.0.0.1,::1,speech.googleapis.com";

public static bool IsAutoSwitchEnabled()
{
    var val = Environment.GetEnvironmentVariable(AutoSwitchEnvVar, EnvironmentVariableTarget.User)
           ?? Environment.GetEnvironmentVariable(AutoSwitchEnvVar);
    return string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
}

public static void SetAutoSwitch(bool enabled, bool setProcessOnly = false)
{
    var val = enabled ? "true" : "false";
    Environment.SetEnvironmentVariable(AutoSwitchEnvVar, val);

    if (enabled)
    {
        Environment.SetEnvironmentVariable("https_proxy", ProxyUrl);
        Environment.SetEnvironmentVariable("http_proxy", ProxyUrl);
        Environment.SetEnvironmentVariable("no_proxy", NoProxyList);
    }
    else
    {
        Environment.SetEnvironmentVariable("https_proxy", null);
        Environment.SetEnvironmentVariable("http_proxy", null);
        Environment.SetEnvironmentVariable("no_proxy", null);
    }

    if (!setProcessOnly)
    {
        try
        {
            Environment.SetEnvironmentVariable(AutoSwitchEnvVar, val, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("https_proxy", enabled ? ProxyUrl : null, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("http_proxy", enabled ? ProxyUrl : null, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("no_proxy", enabled ? NoProxyList : null, EnvironmentVariableTarget.User);
        }
        catch { }
    }
}
```

In `LaunchAsync`, if `IsAutoSwitchEnabled()` is true:
```csharp
if (IsAutoSwitchEnabled())
{
    startInfo.EnvironmentVariables["https_proxy"] = ProxyUrl;
    startInfo.EnvironmentVariables["http_proxy"] = ProxyUrl;
    startInfo.EnvironmentVariables["no_proxy"] = NoProxyList;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityAppControllerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Services/AntigravityAppController.cs tests/AntigravityAppControllerTests.cs
git commit -m "feat(perf): add proxy environment management for auto-switch failover"
```

---

### Task 3: QueryRouter Command Integration (`ag auto` toggle & status)

**Files:**
- Modify: `Routing/QueryRouter.cs`
- Modify: `tests/QueryRouterTests.cs`

**Interfaces:**
- Consumes: `AntigravityAppController.IsAutoSwitchEnabled`, `AntigravityAppController.SetAutoSwitch`
- Produces: `CommandKind.Auto`, `AutoSwitchResults`, updated `QueryStatusAsync`

- [ ] **Step 1: Write failing tests in `tests/QueryRouterTests.cs`**

```csharp
[Theory]
[InlineData("auto", CommandKind.Auto)]
[InlineData("autoswitch", CommandKind.Auto)]
public void ParseCommand_ParsesAutoCommand(string query, CommandKind expected)
{
    var parsed = QueryRouter.ParseCommand(query);
    Assert.Equal(expected, parsed.Kind);
}

[Fact]
public async Task RouteAsync_AutoCommand_ReturnsBinaryToggleResult()
{
    var service = CreateTestAccountService();
    var router = new QueryRouter(service);

    var results = await router.RouteAsync("auto");
    Assert.Single(results);
    Assert.Contains("Auto-Switch:", results[0].Title);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~QueryRouterTests`
Expected: FAIL due to missing `CommandKind.Auto`.

- [ ] **Step 3: Implement `CommandKind.Auto` and `AutoSwitchResults` in `Routing/QueryRouter.cs`**

Add `CommandKind.Auto`:
```csharp
"auto" or "autoswitch" => new ParsedCommand(CommandKind.Auto, argument),
```

Handle in `RouteAsync`:
```csharp
CommandKind.Auto => AutoSwitchResults(),
```

Implement `AutoSwitchResults`:
```csharp
private List<Result> AutoSwitchResults()
{
    var isEnabled = AntigravityAppController.IsAutoSwitchEnabled();
    var nextState = !isEnabled;
    var currentVal = isEnabled ? "1 (ON)" : "0 (OFF)";
    var nextVal = nextState ? "1 (ON)" : "0 (OFF)";

    return
    [
        new Result
        {
            Title = $"Auto-Switch: {currentVal}",
            SubTitle = $"Press Enter to switch to {nextVal}",
            IcoPath = NotifyIconPath(ResultFactory.AppIcon),
            Action = _ =>
            {
                AntigravityAppController.SetAutoSwitch(nextState);
                _context?.API?.ShowMsg(
                    "Antigravity Switcher",
                    $"Auto-Switch: {(nextState ? "1" : "0")}",
                    NotifyIconPath(ResultFactory.AppIcon));
                return true;
            }
        }
    ];
}
```

Include Auto-Switch status in `ag status` and `ag help`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~QueryRouterTests`
Expected: PASS.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test .\AntigravitySwitcher.sln -c Release`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add Routing/QueryRouter.cs tests/QueryRouterTests.cs
git commit -m "feat(ui): add ag auto toggle command and status reporting in Flow Launcher"
```

---

### Task 4: End-to-End Build, Test Verification & Deployment

**Files:**
- None (deployment and verification scripts)

**Interfaces:**
- Consumes: `pwsh ./scripts/build.ps1 -Deploy`, `Stop-Process -Name 'Flow.Launcher'`
- Produces: Deployed plugin binary in `%APPDATA%\FlowLauncher\Plugins\AntigravitySwitcher`

- [ ] **Step 1: Run full release build and tests**

Run: `dotnet test .\AntigravitySwitcher.sln -c Release`
Expected: 190+ tests passed, 0 failed.

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

# Active Chat Restoration & Immediate Switching Notification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide an immediate "Switching to {account}..." toast in Flow Launcher upon selecting an account, capture the currently active Antigravity chat conversation before restarting, and automatically reopen it via Chrome DevTools Protocol (CDP) upon reload.

**Architecture:** Extend `QueryRouter` to emit the immediate toast, add `CaptureActiveChatPathAsync` and `RestoreActiveChatAsync` to `AntigravityCdpClient`, and wire the captured path through `AccountService.SwitchAsync` into `AntigravityAppController`'s background post-restart worker.

**Tech Stack:** C# 10 / .NET 10, Flow Launcher Plugin API, Chrome DevTools Protocol (`ClientWebSocket`, `HttpClient`), System.Text.Json, xUnit.

## Global Constraints
- Zero-dependency: Pure .NET BCL (`HttpClient`, `ClientWebSocket`, `System.Text.Json`); no external NuGet packages.
- Non-blocking: Background CDP operations must never freeze Flow Launcher UI or delay completion toasts.
- Fault-tolerant: If DevTools is closed, disabled, or navigation fails, account switching still succeeds.
- Strict-mode compliance: Never introduce octal literals; preserve byte lengths when touching asar.

---

### Task 1: Immediate Switching Notification

**Files:**
- Modify: `Routing/QueryRouter.cs:468-498`
- Test: `tests/QueryRouterTests.cs`

**Interfaces:**
- Consumes: `IPublicAPI.ShowMsg(string title, string subTitle, string iconPath)`
- Produces: Immediate toast notification on Enter keypress before async switch begins.

- [ ] **Step 1: Write failing test in `tests/QueryRouterTests.cs`**

```csharp
[Fact]
public void Query_SelectAccount_TriggersImmediateSwitchingNotification()
{
    var account = new AccountInfo("acc-1", "Test User", "test@example.com", "Active", true, false, 0, 0);
    var service = new FakeAccountService(new[] { account });
    var api = new FakePublicApi();
    var context = new PluginInitContext { API = api };
    var router = new QueryRouter(service, context);

    var query = new Query { Search = "test" };
    var results = router.Route(query);
    var target = results.FirstOrDefault(r => r.Title.Contains("Test User"));
    Assert.NotNull(target);

    // Act
    target.Action(new ActionContext());

    // Assert immediate message was sent
    Assert.Contains(api.Messages, m => m.Title == "Antigravity Switcher" && m.SubTitle.Contains("Switching to Test User"));
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~Query_SelectAccount_TriggersImmediateSwitchingNotification`  
Expected: FAIL (assertion does not find "Switching to Test User").

- [ ] **Step 3: Update `Routing/QueryRouter.cs`**

In `TryStartSwitch`:
```csharp
    private bool TryStartSwitch(AccountInfo account)
    {
        if (Interlocked.Exchange(ref _switchInProgress, 1) == 1)
        {
            return true;
        }

        if (account.IsActive)
        {
            Interlocked.Exchange(ref _switchInProgress, 0);
            _context?.API?.ShowMsg("Antigravity Switcher", $"Already signed in as {account.DisplayName}", NotifyIconPath(ResultFactory.AppIcon));
            return true;
        }

        _context?.API?.ShowMsg("Antigravity Switcher", $"Switching to {account.DisplayName}...", NotifyIconPath(ResultFactory.AppIcon));
        _ = SwitchAndNotifyAsync(account);
        return true;
    }
```

- [ ] **Step 4: Run tests to verify pass**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~QueryRouterTests`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Routing/QueryRouter.cs tests/QueryRouterTests.cs
git commit -m "feat(ui): add immediate switching notification on account selection"
```

---

### Task 2: Active Chat Path Capture and Navigation in `AntigravityCdpClient`

**Files:**
- Modify: `Services/AntigravityCdpClient.cs`
- Test: `tests/AntigravityCdpClientTests.cs`

**Interfaces:**
- Consumes: Chrome DevTools Protocol HTTP (`/json`) and WebSocket (`Runtime.evaluate`).
- Produces:
  - `Task<string?> CaptureActiveChatPathAsync(CancellationToken cancellationToken = default);`
  - `Task<bool> RestoreActiveChatAsync(string chatPath, CancellationToken cancellationToken = default);`
  - `void TriggerPostRestartActionsInBackground(string? savedChatPath = null);`

- [ ] **Step 1: Write failing tests in `tests/AntigravityCdpClientTests.cs`**

```csharp
[Fact]
public async Task CaptureActiveChatPathAsync_WithActiveChatTarget_ReturnsChatPath()
{
    var json = @"[
        {
            ""type"": ""page"",
            ""url"": ""https://127.0.0.1:7886/c/c961b41a-e6c1-4c73-98b7-4f5bfa0e399b?section=123"",
            ""webSocketDebuggerUrl"": ""ws://127.0.0.1:1234/page/1""
        }
    ]";
    var handler = new MockHttpMessageHandler(json);
    var tempPortFile = Path.GetTempFileName();
    File.WriteAllText(tempPortFile, "1234\n");

    try
    {
        var client = new AntigravityCdpClient(new HttpClient(handler), tempPortFile);
        var path = await client.CaptureActiveChatPathAsync();

        Assert.Equal("/c/c961b41a-e6c1-4c73-98b7-4f5bfa0e399b?section=123", path);
    }
    finally
    {
        File.Delete(tempPortFile);
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
    var handler = new MockHttpMessageHandler(json);
    var tempPortFile = Path.GetTempFileName();
    File.WriteAllText(tempPortFile, "1234\n");

    try
    {
        var client = new AntigravityCdpClient(new HttpClient(handler), tempPortFile);
        var path = await client.CaptureActiveChatPathAsync();

        Assert.Null(path);
    }
    finally
    {
        File.Delete(tempPortFile);
    }
}

[Fact]
public void BuildRestoreScript_ValidPath_GeneratesLocationAssign()
{
    var script = AntigravityCdpClient.BuildRestoreScript("/c/123-abc?section=456");
    Assert.Contains("window.location.assign('/c/123-abc?section=456')", script);
    Assert.Contains("window.location.pathname", script);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityCdpClientTests`  
Expected: FAIL (missing methods and properties on `AntigravityCdpClient`).

- [ ] **Step 3: Implement `CaptureActiveChatPathAsync` and `RestoreActiveChatAsync` in `AntigravityCdpClient.cs`**

```csharp
public interface IAntigravityCdpClient
{
    int? ResolveActivePort();
    Task<string?> CaptureActiveChatPathAsync(CancellationToken cancellationToken = default);
    Task<bool> RestoreActiveChatAsync(string chatPath, CancellationToken cancellationToken = default);
    Task<bool> AutoDismissSecurityNoticeAsync(CancellationToken cancellationToken = default);
    void TriggerAutoDismissInBackground();
    void TriggerPostRestartActionsInBackground(string? savedChatPath = null);
}
```

Implement in `AntigravityCdpClient`:
- `CaptureActiveChatPathAsync`:
  - Resolves port, fetches `http://127.0.0.1:{port}/json`.
  - Searches for `type == "page"` and `url` containing `/c/`.
  - Parses relative path and query starting at `/c/`.
  - Returns sanitized string or `null`.
- `BuildRestoreScript`:
  - Builds JS:
    ```javascript
    (() => {
        const p = window.location.pathname;
        if (p === '/' || !p.startsWith('/c/')) {
            window.location.assign('{escapedChatPath}');
            return true;
        }
        return false;
    })()
    ```
- `RestoreActiveChatAsync`:
  - Connects to active page WebSocket and evaluates `BuildRestoreScript(chatPath)`.
- `TriggerPostRestartActionsInBackground(string? savedChatPath = null)`:
  - Background task that awaits new port, optionally calls `RestoreActiveChatAsync(savedChatPath)` and `AutoDismissSecurityNoticeAsync()`. Keep `TriggerAutoDismissInBackground()` as alias calling with `savedChatPath = null`.

- [ ] **Step 4: Run tests to verify pass**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityCdpClientTests`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Services/AntigravityCdpClient.cs tests/AntigravityCdpClientTests.cs
git commit -m "feat(cdp): add active chat path capture and restoration"
```

---

### Task 3: Wiring Chat Restoration through Service & App Controller

**Files:**
- Modify: `Services/AccountService.cs`
- Modify: `Services/AntigravityAppController.cs`
- Test: `tests/AccountServiceTests.cs`
- Test: `tests/AntigravityAppControllerTests.cs`

**Interfaces:**
- Consumes: `IAntigravityCdpClient.CaptureActiveChatPathAsync`, `IAntigravityCdpClient.TriggerPostRestartActionsInBackground`.
- Produces: `AccountService.SwitchAsync` captures and forwards `savedChatPath` into `RestartForLoginSwapAsync`.

- [ ] **Step 1: Write failing test in `tests/AccountServiceTests.cs`**

```csharp
[Fact]
public async Task SwitchAsync_WithActiveChat_PassesChatPathToController()
{
    var fakeCdp = new FakeCdpClient { ActiveChatPathToReturn = "/c/test-chat-uuid" };
    var fakeApp = new FakeAppController();
    var service = CreateService(appController: fakeApp, cdpClient: fakeCdp);

    var account = new AccountInfo("acc-1", "Test", "test@test.com", "Active", false, false, 0, 0);
    await service.SwitchAsync(account);

    Assert.Equal("/c/test-chat-uuid", fakeApp.LastSavedChatPath);
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~SwitchAsync_WithActiveChat_PassesChatPathToController`  
Expected: FAIL (parameter or property not yet present).

- [ ] **Step 3: Update `AntigravityAppController` and `AccountService`**

In `Services/AntigravityAppController.cs`:
- Update interface `IAntigravityAppController`:
  ```csharp
  Task<bool> RestartForLoginSwapAsync(string? savedChatPath = null, CancellationToken cancellationToken = default);
  ```
- In `RestartForLoginSwapAsync(string? savedChatPath = null, CancellationToken cancellationToken = default)`:
  - Pass `savedChatPath` to `RestartBackendAsync` and `RestartAppAsync`.
  - In `RestartBackendAsync` and `LaunchAsync`:
    Call `_cdpClient?.TriggerPostRestartActionsInBackground(savedChatPath);`.

In `Services/AccountService.cs`:
- In `SwitchAsync`:
  ```csharp
  var activeChatPath = _cdpClient != null
      ? await _cdpClient.CaptureActiveChatPathAsync(cancellationToken).ConfigureAwait(false)
      : null;

  var swapped = (await _dashboard.LoginSwapAsync(baseUri, account.Id, cancellationToken).ConfigureAwait(false)).Succeeded;
  ...
  AntigravityStorageManager.TryPreAcceptOnboardingTerms(account.Email);
  var applied = await _antigravityApp.RestartForLoginSwapAsync(activeChatPath, cancellationToken).ConfigureAwait(false);
  ```

- [ ] **Step 4: Run tests to verify pass**

Run: `dotnet test .\AntigravitySwitcher.sln -c Release`  
Expected: PASS (all tests pass).

- [ ] **Step 5: Commit**

```bash
git add Services/AccountService.cs Services/AntigravityAppController.cs tests/AccountServiceTests.cs tests/AntigravityAppControllerTests.cs
git commit -m "feat(service): wire active chat capture and restoration into login swap flow"
```

---

### Task 4: End-to-End Build, Test Verification & Deployment

**Files:**
- Output: `publish/`
- Target: `C:\Users\dlest\AppData\Roaming\FlowLauncher\Plugins\AntigravitySwitcher`

- [ ] **Step 1: Execute complete unit test suite**

Run: `dotnet test .\AntigravitySwitcher.sln -c Release`  
Expected: All tests pass (0 failures).

- [ ] **Step 2: Build and deploy to Flow Launcher**

Run:
```powershell
Stop-Process -Name 'Flow.Launcher' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
pwsh ./scripts/build.ps1 -Deploy
```

- [ ] **Step 3: Restart Flow Launcher via Task Scheduler**

Run:
```powershell
schtasks /create /tn "LaunchFlowLauncherTemp" /tr "C:\Users\dlest\AppData\Local\FlowLauncher\app-1.20.2\Flow.Launcher.exe" /sc ONCE /st 00:00 /f /it
schtasks /run /tn "LaunchFlowLauncherTemp"
Start-Sleep -Seconds 2
schtasks /delete /tn "LaunchFlowLauncherTemp" /f
```

- [ ] **Step 4: Verify deployed plugin**

Verify `Flow.Launcher.Plugin.AntigravitySwitcher.dll` timestamp and confirm `Flow.Launcher` process is running.

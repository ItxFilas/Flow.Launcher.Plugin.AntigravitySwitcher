# Active Chat Restoration & Immediate Switching Notification Design

**Date**: 2026-10-14  
**Status**: Approved  
**Topic**: Antigravity 2.0 Account Switch - Immediate Feedback and Active Conversation Preservation  

---

## 1. Overview & Goals

When a user selects an account in Flow Launcher (`ag <account>` -> Enter):
1. **Immediate Feedback**: Show an immediate `"Switching to {account.DisplayName}..."` notification so the user has instant visual confirmation that the switch is underway.
2. **Active Chat Preservation**: Before the backend/app is restarted, detect and record the currently open conversation path (e.g., `/c/c961b41a-e6c1-4c73-98b7-4f5bfa0e399b?section=...`).
3. **Automatic Reopen**: Once the language server restarts and Chromium reloads to root (`/`), automatically navigate the window back to the previous conversation using Chrome DevTools Protocol (CDP).
4. **Completion Notification**: Display `"Switched to {account.DisplayName}"` once the restart sequence is complete.
5. **Non-Blocking & Fault Tolerant**: If DevTools is unavailable, the user was on the welcome screen, or navigation fails, the core account switch proceeds smoothly and never crashes or stalls Flow Launcher.

---

## 2. Architecture & Data Flow

```
+-------------------------------------------------------------+
| User presses Enter on Account in Flow Launcher              |
+-------------------------------------------------------------+
                              |
                              v
   [QueryRouter.TryStartSwitch] -> Show immediate toast: "Switching to {DisplayName}..."
                              |
                              v
   [AccountService.SwitchAsync]
       |
       +--> 1. [AntigravityCdpClient.CaptureActiveChatPathAsync]
       |       - Connects to DevTools port (HTTP GET /json)
       |       - Reads open page targets
       |       - If URL matches /c/[a-f0-9\-]+, extracts relative path & query
       |       - Returns savedChatPath (or null)
       |
       +--> 2. [DashboardClient.LoginSwapAsync]
       |       - Swaps token in Windows Credential Manager
       |
       +--> 3. [AntigravityStorageManager.TryPreAcceptOnboardingTerms]
       |       - Writes lastLoginUsername to app_storage.json (modal prevention)
       |
       +--> 4. [AntigravityAppController.RestartForLoginSwapAsync(savedChatPath)]
               - Restarts backend (or full app if needed)
               - Triggers background post-restart worker with savedChatPath
                              |
                              v
   [Background Post-Restart Worker] (AntigravityCdpClient)
       |
       +--> Polls for ready DevTools port (< 6s)
       +--> If savedChatPath is present:
       |       - Evaluates navigation script via CDP:
       |         if (window.location.pathname === '/' || !window.location.pathname.startsWith('/c/')) {
       |             window.location.assign('${savedChatPath}');
       |         }
       +--> Simultaneously runs auto-dismiss check for security notice dialog
                              |
                              v
   [QueryRouter.SwitchAndNotifyAsync] -> Show completion toast: "Switched to {DisplayName}"
```

---

## 3. Detailed Component Specifications

### 3.1. `Routing/QueryRouter.cs`
- In `TryStartSwitch(AccountInfo account)`:
  - Immediately before launching `SwitchAndNotifyAsync`, invoke:
    ```csharp
    _context?.API?.ShowMsg("Antigravity Switcher", $"Switching to {account.DisplayName}...", NotifyIconPath(ResultFactory.AppIcon));
    ```
  - Prevents the user from wondering if the Enter keypress was registered during the 3.8s reload window.
- In `SwitchAndNotifyAsync(AccountInfo account)`:
  - On completion:
    ```csharp
    _context?.API?.ShowMsg("Antigravity Switcher", $"Switched to {account.DisplayName}", NotifyIconPath(ResultFactory.AppIcon));
    ```
  - On error:
    ```csharp
    _context?.API?.ShowMsgError("Antigravity Switcher", ex.Message);
    ```

### 3.2. `Services/AntigravityCdpClient.cs`
- Extend interface `IAntigravityCdpClient`:
  ```csharp
  public interface IAntigravityCdpClient
  {
      int? ResolveActivePort();
      Task<string?> CaptureActiveChatPathAsync(CancellationToken cancellationToken = default);
      Task<bool> RestoreActiveChatAsync(string chatPath, CancellationToken cancellationToken = default);
      Task<bool> AutoDismissSecurityNoticeAsync(CancellationToken cancellationToken = default);
      void TriggerPostRestartActionsInBackground(string? savedChatPath = null);
  }
  ```
- **`CaptureActiveChatPathAsync`**:
  - Resolves active port from `DevToolsActivePort`. If null, returns `null`.
  - Sends `GET http://127.0.0.1:{port}/json` with a 1.5s timeout.
  - Enumerates targets where `type == "page"`.
  - Searches for `url` containing `/c/`.
  - Extracts the path starting from `/c/` including query parameters (e.g., `/c/c961b41a-e6c1-4c73-98b7-4f5bfa0e399b?section=...`).
  - Returns the extracted path or `null`.
  - Catches all exceptions and returns `null` (resilient to closed app or missing port).
- **`RestoreActiveChatAsync`**:
  - Connects to the newly restarted window target via WebSocket.
  - Executes:
    ```javascript
    (() => {
        const path = window.location.pathname;
        if (path === '/' || !path.startsWith('/c/')) {
            window.location.assign('${chatPath}');
            return true;
        }
        return false;
    })()
    ```
  - Verifies that the evaluation was sent.
- **`TriggerPostRestartActionsInBackground`**:
  - Replaces `TriggerAutoDismissInBackground()`.
  - Spawns a background task with a 15-second timeout that:
    1. Awaits DevTools port readiness.
    2. Runs `RestoreActiveChatAsync(savedChatPath)` if `savedChatPath` is not null.
    3. Runs `AutoDismissSecurityNoticeAsync()`.

### 3.3. `Services/AccountService.cs`
- In `SwitchAsync(AccountInfo account, CancellationToken cancellationToken)`:
  1. Capture active chat path:
     ```csharp
     var activeChatPath = _cdpClient != null
         ? await _cdpClient.CaptureActiveChatPathAsync(cancellationToken).ConfigureAwait(false)
         : null;
     ```
  2. Perform login swap via `_dashboard.LoginSwapAsync`.
  3. Pre-accept onboarding terms in `app_storage.json`.
  4. Call `_antigravityApp.RestartForLoginSwapAsync(activeChatPath, cancellationToken)`.

### 3.4. `Services/AntigravityAppController.cs`
- Update `RestartForLoginSwapAsync(string? savedChatPath = null, CancellationToken cancellationToken = default)`.
- Pass `savedChatPath` to `RestartBackendAsync(savedChatPath)` and `RestartAppAsync(savedChatPath)`.
- When invoking CDP post-restart actions, call:
  ```csharp
  _cdpClient?.TriggerPostRestartActionsInBackground(savedChatPath);
  ```

---

## 4. Error Handling & Edge Cases

1. **User Was Not in Any Chat**:
   - `CaptureActiveChatPathAsync` finds only root (`/`) or welcome targets.
   - Returns `null`. `RestoreActiveChatAsync` is skipped; window remains at root.
2. **Cold Launch (Antigravity was not running)**:
   - Port file does not exist; capture returns `null`.
   - App boots to default workspace without error.
3. **DevTools Port Delay / Reconnect**:
   - Post-restart worker retries every 150ms for up to 6 seconds to ensure the new DevTools port is written and reachable before attempting navigation.
4. **Invalid or Malformed Chat URLs**:
   - Regex validation verifies path matches `^/c/[a-zA-Z0-9_\-]+(\?.*)?$`. Invalid or malicious strings are rejected.
5. **Multiple Open Windows**:
   - If multiple windows exist, targets matching the chat route take precedence.

---

## 5. Testing & Verification Plan

1. **Unit Tests (`AntigravityCdpClientTests.cs`)**:
   - Test `CaptureActiveChatPath` with mock HTTP responses:
     - URL with `/c/<uuid>` -> extracts `/c/<uuid>`.
     - URL with `/c/<uuid>?section=<id>` -> extracts full path and query.
     - Root URL `/` -> returns `null`.
     - Non-page targets (service workers, iframes) -> ignored.
     - HTTP failure or connection timeout -> returns `null`.
   - Test `RestoreActiveChatAsync` script construction and parameter escaping.
2. **Unit Tests (`QueryRouterTests.cs`)**:
   - Verify `TryStartSwitch` displays `ShowMsg` with `"Switching to {DisplayName}..."` immediately.
   - Verify `SwitchAndNotifyAsync` displays `ShowMsg` with `"Switched to {DisplayName}"` on success.
3. **Unit Tests (`AccountServiceTests.cs`)**:
   - Verify `SwitchAsync` passes captured chat path into `RestartForLoginSwapAsync`.
4. **Live Verification**:
   - Test account switch in Flow Launcher while inside an active chat.
   - Confirm immediate "Switching..." notification appears.
   - Confirm Antigravity automatically re-opens the exact conversation after reload.

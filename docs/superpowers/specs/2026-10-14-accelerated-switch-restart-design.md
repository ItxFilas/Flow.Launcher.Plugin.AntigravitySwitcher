# Design Specification: Accelerated Account Switch Restart & Auto-Dismissal

**Date**: 2026-10-14  
**Topic**: Antigravity 2.0 Electron App Switch-and-Restart Acceleration & Security Notice Auto-Dismissal  
**Status**: Approved for Implementation

---

## 1. Problem Statement

When switching accounts in the Antigravity Switcher Flow Launcher plugin (`ag <account>` -> Enter):
1. **The Restart Takes ~7 Seconds**: After writing credentials to Windows Credential Manager (`gemini:antigravity`), applying the login to the Antigravity 2.0 Electron desktop app takes approximately 7 seconds from the user's perspective.
   - Electron's `languageServer.js` has a hardcoded crash cooldown: `RESTART_COOLDOWN_MS = 2000` (2 full seconds of deliberate sleep before respawning `language_server.exe`).
   - `MAX_RESTARTS = 3` imposes a crash threshold that halts automatic backend respawns if accounts are switched 3 times within 60 seconds.
   - The Go backend (`language_server.exe`, 169 MB) and Chromium webview cold reloads take an additional 4–5 seconds.
   - Process termination in `AntigravityAppController` previously used `Process.GetProcessesByName()` in a tight loop, enumerating the entire OS process table twice every 15 ms.
   - App launches used `UseShellExecute = true`, incurring Explorer COM shell overhead instead of direct Win32 `CreateProcessW`.
2. **"Security Notice & Data Use" Modal Interruption**: On fresh login sessions or account switches, Antigravity 2.0 frequently prompts the user with a modal dialog requiring them to manually check *"Yes, I agree to help improve Antigravity by allowing Google to collect and use my Interactions data..."* and click *"Accept and continue"*.

---

## 2. Goals & Success Criteria

1. **Substantially Faster Account Switch Restart**:
   - Eliminate the 2-second artificial sleep in Antigravity's crash monitor safely and self-healingly.
   - Replace OS process table polling with native Windows asynchronous kernel process handles (`WaitForExitAsync`).
   - Launch app processes directly via Win32 `CreateProcessW` (`UseShellExecute = false`).
2. **Automatic Security Notice Dismissal**:
   - Automatically detect the "Security Notice & Data Use" modal via Chromium DevTools Protocol (CDP) on the active local debugging port.
   - Check the agreement checkbox and click "Accept and continue" within milliseconds of it appearing in the DOM.
3. **100% Update-Proof & Resilient**:
   - If Google auto-updates Antigravity 2.0 and replaces `app.asar`, account switching must never fail.
   - In-place optimization must be self-healing, running only when files are unlocked, and falling back gracefully if locked or skipped.
   - Single-instance lock collisions must be completely eliminated.

---

## 3. Architecture & Components

```
+-------------------------------------------------------------+
|                      Flow Launcher UI                       |
|           ag <account> -> Enter / Switch to Next            |
+------------------------------+------------------------------+
                               |
                               v
+-------------------------------------------------------------+
|                     AccountService.cs                       |
|  1. LoginSwapAsync (Writes gemini:antigravity in CredMgr)   |
|  2. RestartForLoginSwapAsync                                |
+------------------------------+------------------------------+
                               |
                               v
+-------------------------------------------------------------+
|               AntigravityAppController.cs                   |
|  - RestartBackendAsync:                                     |
|      * Kill language_server.exe                             |
|      * await Process.WaitForExitAsync (Kernel handles, <5ms)|
|  - RestartAppAsync / LaunchAsync:                           |
|      * EnsureAsarOptimized (Patch 2000ms -> 0000ms in asar) |
|      * Direct Win32 CreateProcessW (UseShellExecute = false)|
|  - TriggerAutoDismissNoticeAsync (Background CDP Worker)     |
+------------------------------+------------------------------+
                               |
                               v
+-------------------------------------------------------------+
|                 AntigravityCdpClient.cs                     |
|  - Reads %APPDATA%\Antigravity\DevToolsActivePort           |
|  - Connects to ws://127.0.0.1:<port>/devtools/page/...      |
|  - Evaluates DOM query: check agreement + click Accept      |
+-------------------------------------------------------------+
```

### Component 1: `AntigravityCdpClient` (`Services/AntigravityCdpClient.cs`)
A lightweight, zero-dependency .NET service communicating over local WebSockets (`System.Net.WebSockets.ClientWebSocket`) and HTTP (`HttpClient`):
- **Port Resolution**: Reads line 1 of `%APPDATA%\Antigravity\DevToolsActivePort`.
- **Target Discovery**: Queries `http://127.0.0.1:<port>/json` to find the active page target.
- **Evaluation**: Sends `Runtime.evaluate` JSON-RPC command to inspect and click the agreement dialog:
  ```javascript
  (() => {
    const text = document.body.innerText;
    if (text && text.includes("Security Notice")) {
      const cb = document.querySelector("input[type=checkbox]");
      if (cb && !cb.checked) cb.click();
      const btn = Array.from(document.querySelectorAll("button"))
        .find(b => b.textContent && b.textContent.includes("Accept and continue"));
      if (btn) { btn.click(); return true; }
    }
    return false;
  })()
  ```
- **Liveness Loop**: Polls for up to 3 seconds after an account switch/launch with 100ms intervals, exiting immediately once dismissed or when timeout expires.

### Component 2: `AntigravityAppController` Optimization
- **`RestartBackendAsync`**:
  ```csharp
  var processes = Process.GetProcessesByName(LanguageServerProcessName);
  if (processes.Length == 0) return false;

  foreach (var p in processes) {
      try { p.Kill(entireProcessTree: true); } catch { }
  }

  // Await kernel process exit handles instead of polling process tables
  using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
  cts.CancelAfter(TimeSpan.FromMilliseconds(500));
  await Task.WhenAll(processes.Select(p => p.WaitForExitAsync(cts.Token))).ConfigureAwait(false);
  ```
- **`EnsureAsarOptimized`**:
  - Locates `%LOCALAPPDATA%\Programs\Antigravity\resources\app.asar`.
  - When the app is stopped or being relaunched, checks if the file contains `RESTART_COOLDOWN_MS = 2000` (ASCII/UTF8 byte sequence).
  - If found and file is writable, replaces `2000;` with `0000;` and `MAX_RESTARTS = 3;` with `MAX_RESTARTS = 999999;` in-place without altering byte offsets or file sizes.
  - If locked (`IOException`) or read-only, catches the exception and proceeds silently without disrupting app launch.
- **`LaunchAsync`**:
  - Sets `UseShellExecute = false`.
  - Passes pre-resolved directory and starts process directly via `CreateProcessW`.

### Component 3: `AccountService.SwitchAsync` Integration
- After successfully applying `RestartForLoginSwapAsync`, fires `_cdpClient.AutoDismissNoticeInBackground()` asynchronously so Flow Launcher remains completely non-blocking while Antigravity's UI automatically clears the modal in the background.

---

## 4. Error Handling & Reliability

1. **CDP Unavailability**: If Antigravity was launched with remote debugging disabled or the port file is missing, the CDP client silently skips auto-dismissal without failing the account switch.
2. **App Updates**: When Google updates Antigravity:
   - `app.asar` is replaced with the stock version.
   - The plugin's core restart remains fully functional via kernel handles and Win32 direct process launch.
   - On the next app restart when the file is unlocked, `EnsureAsarOptimized` cleanly re-applies the 0ms cooldown.
3. **File Lock Contention**: `EnsureAsarOptimized` uses non-exclusive sharing or catch-and-continue semantics so it will never crash if Antigravity or an antivirus process holds a read handle.

---

## 5. Verification & Testing

1. **Unit Tests**:
   - `AntigravityCdpClientTests`: Tests port parsing from `DevToolsActivePort`, JSON target parsing, and WebSocket request serialization.
   - `AntigravityAppControllerTests`: Verifies `WaitForExitAsync` handle synchronization, `EnsureAsarOptimized` byte replacement logic, and graceful fallback when files are missing or locked.
   - `AccountServiceTests`: Verifies `SwitchAsync` delegates to the enhanced controller and dispatches CDP dismissal.
2. **Integration Verification**:
   - Validate `dotnet test .\AntigravitySwitcher.sln -c Release` passes cleanly.
   - Validate plugin build via `pwsh ./scripts/build.ps1`.

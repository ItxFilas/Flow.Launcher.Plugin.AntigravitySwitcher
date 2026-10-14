# Accelerated Switch Restart & Modal Auto-Dismissal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the ~7-second account switch delay in Antigravity 2.0 and automatically dismiss the "Security Notice & Data Use" modal.

**Architecture:** Accelerate `AntigravityAppController` by replacing OS process enumeration polling with asynchronous Windows kernel wait handles (`Process.WaitForExitAsync`) and direct `CreateProcessW` launches. Provide self-healing in-place asar optimization removing the 2-second crash cooldown. Introduce `AntigravityCdpClient` to communicate via Chrome DevTools Protocol (`DevToolsActivePort`) and auto-accept the data use modal without user intervention.

**Tech Stack:** C# (.NET 7/8/9/10), `System.Net.WebSockets.ClientWebSocket`, `System.Diagnostics.Process`, System.Text.Json, xUnit.

## Global Constraints

- Never break on Antigravity application updates: all core features must function even if `app.asar` is unpatched or locked.
- Non-blocking: Flow Launcher UI and notifications must never freeze or wait on background CDP retries.
- Zero-dependency: Use standard .NET libraries (`ClientWebSocket`, `HttpClient`, `Process`) without introducing external NuGet packages.
- Byte-length preservation: In-place asar patch replaces `2000;` with `0000;` and `MAX_RESTARTS = 3;` with `MAX_RESTARTS = 999999;` to maintain exact file offsets.

---

### Task 1: Chrome DevTools Protocol (CDP) Client & Auto-Dismissal

**Files:**
- Create: `Services/AntigravityCdpClient.cs`
- Test: `tests/AntigravityCdpClientTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IAntigravityCdpClient
  {
      int? ResolveActivePort();
      Task<bool> AutoDismissSecurityNoticeAsync(CancellationToken cancellationToken = default);
      void TriggerAutoDismissInBackground();
  }
  ```

- [ ] **Step 1: Write the failing tests for `AntigravityCdpClient`**

```csharp
namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using System.IO;
using Xunit;

public sealed class AntigravityCdpClientTests
{
    [Fact]
    public void ResolveActivePort_ParsesFirstLineOfPortFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "47171\n/devtools/browser/xyz\n");
            var port = AntigravityCdpClient.ParsePortFromFile(tempFile);
            Assert.Equal(47171, port);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ResolveActivePort_ReturnsNullWhenFileMissingOrInvalid()
    {
        Assert.Null(AntigravityCdpClient.ParsePortFromFile("C:\\non_existent_file_path_12345.txt"));

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "not_a_number\n");
            Assert.Null(AntigravityCdpClient.ParsePortFromFile(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void BuildDismissScript_ContainsRequiredDomChecks()
    {
        var script = AntigravityCdpClient.DismissNoticeScript;
        Assert.Contains("Security Notice", script);
        Assert.Contains("Accept and continue", script);
        Assert.Contains("checkbox", script);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityCdpClientTests`
Expected: FAIL (compilation error, type not yet created).

- [ ] **Step 3: Implement `AntigravityCdpClient`**

Create `Services/AntigravityCdpClient.cs`:
```csharp
namespace Flow.Launcher.Plugin.AntigravitySwitcher.Services;

using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

public interface IAntigravityCdpClient
{
    int? ResolveActivePort();
    Task<bool> AutoDismissSecurityNoticeAsync(CancellationToken cancellationToken = default);
    void TriggerAutoDismissInBackground();
}

public sealed class AntigravityCdpClient : IAntigravityCdpClient
{
    public const string DismissNoticeScript = @"(() => {
        const text = document.body ? document.body.innerText : '';
        if (text && text.includes('Security Notice')) {
            const cb = document.querySelector('input[type=checkbox]');
            if (cb && !cb.checked) { cb.click(); }
            const btn = Array.from(document.querySelectorAll('button'))
                .find(b => b.textContent && b.textContent.includes('Accept and continue'));
            if (btn) {
                btn.click();
                return true;
            }
        }
        return false;
    })()";

    private readonly HttpClient _http;
    private readonly string _portFilePath;

    public AntigravityCdpClient(HttpClient? http = null, string? portFilePath = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        _portFilePath = portFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Antigravity",
            "DevToolsActivePort");
    }

    public int? ResolveActivePort() => ParsePortFromFile(_portFilePath);

    public static int? ParsePortFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            using var reader = new StreamReader(filePath);
            var firstLine = reader.ReadLine()?.Trim();
            return int.TryParse(firstLine, out var port) && port is > 0 and <= 65535 ? port : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> AutoDismissSecurityNoticeAsync(CancellationToken cancellationToken = default)
    {
        var port = ResolveActivePort();
        if (port is null) return false;

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await _http.GetStringAsync($"http://127.0.0.1:{port}/json", cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(response);
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page" &&
                        element.TryGetProperty("webSocketDebuggerUrl", out var wsProp) && wsProp.GetString() is { } wsUrl)
                    {
                        var dismissed = await TryEvaluateDismissAsync(wsUrl, cancellationToken).ConfigureAwait(false);
                        if (dismissed) return true;
                    }
                }
            }
            catch
            {
            }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    public void TriggerAutoDismissInBackground()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await AutoDismissSecurityNoticeAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
            }
        });
    }

    private static async Task<bool> TryEvaluateDismissAsync(string wsUrl, CancellationToken cancellationToken)
    {
        using var ws = new ClientWebSocket();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(TimeSpan.FromSeconds(2));

        await ws.ConnectAsync(new Uri(wsUrl), connectCts.Token).ConfigureAwait(false);

        var req = JsonSerializer.Serialize(new
        {
            id = 1,
            method = "Runtime.evaluate",
            @params = new
            {
                expression = DismissNoticeScript,
                returnByValue = true
            }
        });

        var bytes = Encoding.UTF8.GetBytes(req);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
        if (result.Count > 0)
        {
            using var resDoc = JsonDocument.Parse(new ReadOnlyMemory<byte>(buffer, 0, result.Count));
            if (resDoc.RootElement.TryGetProperty("result", out var r) &&
                r.TryGetProperty("result", out var inner) &&
                inner.TryGetProperty("value", out var val) &&
                val.GetBoolean())
            {
                return true;
            }
        }

        return false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityCdpClientTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Services/AntigravityCdpClient.cs tests/AntigravityCdpClientTests.cs
git commit -m "feat(cdp): add AntigravityCdpClient with DevTools port parsing and notice dismissal"
```

---

### Task 2: High-Performance Process Controller (`AntigravityAppController`)

**Files:**
- Modify: `Services/AntigravityAppController.cs`
- Test: `tests/AntigravityAppControllerTests.cs`

**Interfaces:**
- Consumes: `IAntigravityCdpClient`
- Produces:
  ```csharp
  public interface IAntigravityAppController
  {
      Task<bool> RestartForLoginSwapAsync(CancellationToken cancellationToken = default);
      Task<bool> RestartAppAsync(CancellationToken cancellationToken = default);
      bool EnsureAsarOptimized(string? asarPath = null);
  }
  ```

- [ ] **Step 1: Write tests for asar optimization and kernel handle exit logic**

Create `tests/AntigravityAppControllerTests.cs`:
```csharp
namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Flow.Launcher.Plugin.AntigravitySwitcher.Services;
using System.IO;
using System.Text;
using Xunit;

public sealed class AntigravityAppControllerTests
{
    [Fact]
    public void EnsureAsarOptimized_ReplacesCooldownAndMaxRestartsInPlace()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "const RESTART_COOLDOWN_MS = 2000; const MAX_RESTARTS = 3; const OTHER = 1;";
            File.WriteAllText(tempFile, content, Encoding.UTF8);

            var patched = AntigravityAppController.OptimizeAsarBytes(tempFile);
            Assert.True(patched);

            var result = File.ReadAllText(tempFile, Encoding.UTF8);
            Assert.Contains("RESTART_COOLDOWN_MS = 0000;", result);
            Assert.Contains("MAX_RESTARTS = 999999;", result);
            Assert.Equal(content.Length, result.Length);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void EnsureAsarOptimized_ReturnsFalseWhenAlreadyOptimizedOrMissing()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "const RESTART_COOLDOWN_MS = 0000; const MAX_RESTARTS = 999999;";
            File.WriteAllText(tempFile, content, Encoding.UTF8);

            Assert.False(AntigravityAppController.OptimizeAsarBytes(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }

        Assert.False(AntigravityAppController.OptimizeAsarBytes("C:\\non_existent_asar_file.asar"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityAppControllerTests`
Expected: FAIL (method `OptimizeAsarBytes` does not exist).

- [ ] **Step 3: Update `AntigravityAppController` with kernel wait handles and Win32 direct launch**

Update `Services/AntigravityAppController.cs`:
1. Inject optional `IAntigravityCdpClient` into `AntigravityAppController`.
2. Add `EnsureAsarOptimized` / `OptimizeAsarBytes`:
   - Scans for ASCII byte sequences `RESTART_COOLDOWN_MS = 2000;` and `MAX_RESTARTS = 3;`.
   - Replaces with exact-length `RESTART_COOLDOWN_MS = 0000;` and `MAX_RESTARTS = 999999;` via `FileStream` with `FileShare.Read`.
3. In `RestartBackendAsync`:
   - Concurrently terminate `language_server` processes.
   - Use `Task.WhenAll(processes.Select(p => p.WaitForExitAsync(cts.Token)))` with 400ms timeout instead of `GetProcessesByName` loop.
   - Trigger `_cdpClient.TriggerAutoDismissInBackground()`.
4. In `CloseAsync`:
   - Concurrently terminate app and backend processes.
   - Use `Task.WhenAll(allProcesses.Select(p => p.WaitForExitAsync(cts.Token)))` with 600ms timeout.
   - Attempt `EnsureAsarOptimized` immediately after processes terminate and release lock.
5. In `LaunchAsync`:
   - Set `UseShellExecute = false`.
   - Start process directly via Win32 `CreateProcessW`.
   - Trigger `_cdpClient.TriggerAutoDismissInBackground()`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\AntigravitySwitcher.sln --filter FullyQualifiedName~AntigravityAppControllerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Services/AntigravityAppController.cs tests/AntigravityAppControllerTests.cs
git commit -m "perf(controller): accelerate process lifecycle with kernel wait handles and asar cooldown optimization"
```

---

### Task 3: Integrate with `AccountService.SwitchAsync` & Wiring

**Files:**
- Modify: `Services/AccountService.cs`
- Modify: `Main.cs`
- Modify: `tests/AccountServiceTests.cs`

**Interfaces:**
- Consumes: `IAntigravityAppController`, `IAntigravityCdpClient`
- Produces: Seamless, fast account switch execution in Flow Launcher with auto-dismissal.

- [ ] **Step 1: Write test verifying `SwitchAsync` coordinates with accelerated controller**

Update `tests/AccountServiceTests.cs` to verify:
- `SwitchAsync` invokes `RestartForLoginSwapAsync`.
- Stub controller reflects invocation.

- [ ] **Step 2: Run test to verify existing tests pass**

Run: `dotnet test .\AntigravitySwitcher.sln`
Expected: PASS.

- [ ] **Step 3: Update `Main.cs` and `AccountService.cs`**

Ensure `AntigravityCdpClient` is instantiated and provided to `AntigravityAppController` in `Main.cs` and `AccountService.cs`.

- [ ] **Step 4: Run full test suite to verify everything passes**

Run: `dotnet test .\AntigravitySwitcher.sln -c Release`
Expected: PASS (all tests pass).

- [ ] **Step 5: Commit**

```powershell
git add Services/AccountService.cs Main.cs tests/AccountServiceTests.cs
git commit -m "feat(service): wire accelerated restart and CDP auto-dismissal into AccountService"
```

---

### Task 4: Publish & Verification

- [ ] **Step 1: Compile and publish the plugin**

Run: `pwsh ./scripts/build.ps1`
Expected: Successful publish to `./publish/`.

- [ ] **Step 2: Verify git working directory and commit status**

Run: `git status --short`
Expected: Clean working tree.

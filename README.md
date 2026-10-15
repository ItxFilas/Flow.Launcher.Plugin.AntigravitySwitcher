# Antigravity Switcher — a Flow Launcher plugin

A [Flow Launcher](https://www.flowlauncher.com/) plugin (C#/.NET) for managing and switching
[Antigravity Account Switcher](https://github.com/Muriel-Gasparini/antigravity-account-switcher)
accounts.

Action keyword: **`ag`**

---

## How it works

Switching an account is a **real login swap**: the switcher exchanges the target account's
refresh token for fresh Google credentials, rewrites the Antigravity login stored in the
Windows Credential Manager (`gemini:antigravity`), marks the account active, and the plugin
then applies it to the running app. The switch is applied by restarting only Antigravity's
`language_server` backend — the Electron frontend respawns it automatically and the new
process picks up the swapped login **without closing the app window**. If the backend does
not recover its chat interface within 10 s, the plugin falls back once to closing and relaunching the app. No
proxy environment variables are involved.

After a switch, recovery checks run every 100 ms and proceed immediately when the new
backend's chat composer is ready. Chat restoration waits for Antigravity's own port change
and page reload. Security notices are checked during recovery without a fixed wait.
The plugin sends `Continue` through CDP only into an empty, idle composer, and avoids
retrying after Enter to prevent duplicate messages. Recovery diagnostics are written to
`%TEMP%\AntigravitySwitcher.log`.

| Operation        | REST call                              | CLI fallback              |
| :--------------- | :------------------------------------- | :------------------------ |
| List accounts    | `GET /api/accounts`                    | `list-accounts`           |
| Switch account   | `POST /api/accounts/{id}/login-swap`   | — (daemon required)       |
| Remove account   | `DELETE /api/accounts/{id}`            | `remove-account` (compat) |
| Refresh quotas   | `POST /api/quota/refresh`              | `refresh-quotas`          |
| Status           | —                                      | `status`                  |

> **Important:** the dashboard API (including the login swap) is only available while the
> switcher runs as a daemon (`antigravity-account-switcher serve` or `launch`). The login
> swap itself is **Windows-only**, because Antigravity stores its login in the Windows
> Credential Manager. Reading the account list alone works with the CLI.

---

## Requirements

- Windows with the [.NET SDK](https://dotnet.microsoft.com/download) (8.0 or newer) to build.
- Flow Launcher **1.20.x** (running on .NET 7). The plugin therefore targets `net7.0-windows` and
  `Flow.Launcher.Plugin` **4.7.0**. For a .NET 9 build of Flow Launcher, change `TargetFramework`
  in the `.csproj` to `net9.0-windows` and the package to `5.x`.
- An installed and configured `antigravity-account-switcher`
  (binary in `PATH`, in `%LOCALAPPDATA%\antigravity-account-switcher\`, or in `~/.local/bin`).
- For WSL mode: `wsl.exe` with the switcher installed inside the distribution.

The plugin automatically detects whether the switcher runs natively on Windows or inside WSL
(`wsl.exe -e antigravity-account-switcher`). The path can be forced with the
`ANTIGRAVITY_SWITCHER_BIN` environment variable.

---

## Build

```powershell
# Build into .\publish only
pwsh ./scripts/build.ps1

# Or directly:
dotnet publish .\Flow.Launcher.Plugin.AntigravitySwitcher.csproj -c Release -o .\publish
```

## Deploy to Flow Launcher

> **Close Flow Launcher before deploying** — it keeps the loaded plugin DLL locked and the copy
> would fail. Then just start Flow Launcher again (it loads the plugin on startup).

```powershell
# Builds and copies the plugin into %APPDATA%\FlowLauncher\Plugins\AntigravitySwitcher
pwsh ./scripts/build.ps1 -Deploy
```

Manual equivalent:

1. Build the plugin: `dotnet publish -c Release -o .\publish`
2. Copy the **entire contents** of `.\publish` into
   `%APPDATA%\FlowLauncher\Plugins\AntigravitySwitcher`
   (the folder must contain `plugin.json` and
   `Flow.Launcher.Plugin.AntigravitySwitcher.dll` at its root).
3. Restart Flow Launcher (or use *Reload Plugin Data* in its settings).
4. Type `ag`.

---

## Usage

| Command             | Description |
| :------------------ | :---- |
| `ag`                | Account list. The first row, **"Switch to next account"**, signs the IDE into the account with the most free quota. |
| `ag <filter>`       | Filters accounts by email / ID. |
| `ag add`            | Adds a new Google account (`add-account`, OAuth in your browser). |
| `ag remove [filter]`| Offers accounts for removal; Enter confirms the deletion. |
| `ag refresh`        | Forces an immediate quota refresh. |

| `ag status`         | Shows the switcher status (copyable). |
| `ag help`           | Command overview. |

**Enter** on an account row (or **Sign into Antigravity as this account** in its context
menu) swaps the Antigravity login to that account and applies it by restarting the app's
backend — the window stays open. Flow Launcher closes immediately and the switch runs in
the background; a NoSleep-style notification reports the result (`Switched to …`). The
`[ACTIVE]` marker follows the account the IDE is running as.

Each account row shows `[ACTIVE]` in the title, and a subtitle with the state
(Active/Standby/Exhausted/Error/Disabled), the remaining quota per window
(`5hr` and `week`) and the time until each window resets, e.g.:

```
Active · 5hr 87% (resets 3h12m) · week 68% (resets 5d)
```

### Context menu (`Shift + Enter` / right click)

- **Sign into Antigravity as this account**
- **Remove this account**
- **Copy email to clipboard**
- **Open in browser**

---

## Performance and reliability

- All CLI/HTTP calls are **asynchronous** with a timeout — Flow Launcher never freezes.
- The account list is **cached** (8 s) and refreshed in the background, so the UI stays responsive.
- Status icons: active / standby / exhausted account (see `Images/`).

### Environment variables

| Variable                     | Meaning |
| :--------------------------- | :----- |
| `ANTIGRAVITY_SWITCHER_BIN`   | Explicit path to the switcher binary. |
| `ANTIGRAVITY_PORT`           | REST API/proxy port (takes precedence over the config file). |

---

## Development and tests

```powershell
dotnet build .\AntigravitySwitcher.sln -c Release
dotnet test  .\AntigravitySwitcher.sln -c Release
```

The test project (`tests/`) covers the CLI/API response parsers, account formatting,
the cache, and the next-account selection logic.

### Structure

```
Flow.Launcher.Plugin.AntigravitySwitcher.csproj   # main plugin (net7.0-windows)
plugin.json                                       # Flow Launcher manifest (keyword: ag)
Main.cs                                           # IPlugin + IContextMenu
Cli/                                              # binary localization, CLI runner, dashboard API, parsers
Models/AccountInfo.cs                             # account model + formatting
Services/                                         # AccountService, cache
Images/                                           # icons (generated by scripts/Generate-Icons.ps1)
scripts/build.ps1                                 # build + deploy
scripts/Generate-Icons.ps1                        # regenerate PNG icons
```

### Regenerating icons

```powershell
pwsh ./scripts/Generate-Icons.ps1
```

---

## Troubleshooting

- **"The antigravity-account-switcher tool was not found"** — add the binary to `PATH`,
  set `ANTIGRAVITY_SWITCHER_BIN`, or make sure it is available inside WSL.
- **Switching fails** — run the switcher as a daemon
  (`antigravity-account-switcher serve` or `launch`); without the running switcher the login
  cannot be swapped. The login swap itself is Windows-only.
- **"Login swapped, but Antigravity was not found"** — the binary could not be located. Set
  `ANTIGRAVITY_BIN` or configure the switcher with
  `antigravity-account-switcher config set antigravity_bin <path>`.
- **I don't see the changes** — use `ag refresh`, or restart Flow Launcher.

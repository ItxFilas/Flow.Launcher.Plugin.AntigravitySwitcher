# Antigravity Switcher — a Flow Launcher plugin

A [Flow Launcher](https://www.flowlauncher.com/) plugin (C#/.NET) for managing and switching
[Antigravity Account Switcher](https://github.com/Muriel-Gasparini/antigravity-account-switcher)
accounts.

Action keyword: **`ag`**

---

## How it works

The switcher (v1.1.0) **does not** expose a CLI `switch` or `remove-account` command. The plugin
therefore performs switching and removal through the local REST API of the switcher's web
dashboard:

| Operation       | REST call                              | CLI fallback                |
| :-------------- | :------------------------------------- | :-------------------------- |
| List accounts   | `GET /api/accounts`                    | `list-accounts`             |
| Switch account  | `POST /api/accounts/{id}/select`       | `switch <id>` (compat)      |
| Remove account  | `DELETE /api/accounts/{id}`            | `remove-account` (compat)   |
| Refresh quotas  | `POST /api/quota/refresh`              | `refresh-quotas`            |
| Status          | —                                      | `status`                    |

> **Important:** the dashboard API is only available while the switcher runs as a daemon
> (`antigravity-account-switcher serve` or `launch`). Reading the account list alone works with the CLI.

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
| `ag`                | Account list. The first row, **"Switch to next account"**, switches to the account with the most free quota. |
| `ag <filter>`       | Filters accounts by email / ID. |
| `ag add`            | Adds a new Google account (`add-account`, OAuth in your browser). |
| `ag remove [filter]`| Offers accounts for removal; Enter confirms the deletion. |
| `ag refresh`        | Forces an immediate quota refresh. |
| `ag web`            | Opens the web dashboard (`http://127.0.0.1:8080`, or the configured port). |
| `ag status`         | Shows the switcher status (copyable). |
| `ag help`           | Command overview. |

Each account row shows `[ACTIVE]` in the title, and a subtitle with the state
(Active/Standby/Exhausted/Error/Disabled), the remaining quota per window
(`5hr` and `week`) and the time until each window resets, e.g.:

```
Active · 5hr 87% (resets 3h12m) · week 68% (resets 5d)
```

### Context menu (`Shift + Enter` / right click)

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
| `ANTIGRAVITY_PORT`           | Web dashboard port (takes precedence over the config file). |

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
  (`antigravity-account-switcher serve` or `launch`); without a running dashboard the account
  cannot be switched.
- **I don't see the changes** — use `ag refresh`, or restart Flow Launcher.

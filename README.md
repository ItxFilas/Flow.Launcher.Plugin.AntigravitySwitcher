# Antigravity Switcher — plugin pro Flow Launcher

Plugin pro [Flow Launcher](https://www.flowlauncher.com/) (C#/.NET) pro kompletní správu a
přepínání účtů nástroje [Antigravity Account Switcher](https://github.com/Muriel-Gasparini/antigravity-account-switcher).

Klíčové slovo: **`ag`**

---

## Jak to funguje

Switcher (v1.1.0) **neposkytuje** CLI příkaz `switch` ani `remove-account`. Přepínání a odebírání
účtů proto plugin provádí přes lokální REST API webového dashboardu switcheru:

| Operace        | REST volání                            | Fallback CLI                |
| :------------- | :------------------------------------- | :-------------------------- |
| Seznam účtů    | `GET /api/accounts`                    | `list-accounts`             |
| Přepnutí účtu  | `POST /api/accounts/{id}/select`       | `switch <id>` (kompatibilita) |
| Odebrání účtu  | `DELETE /api/accounts/{id}`            | `remove-account` (kompatibilita) |
| Obnovení kvót  | `POST /api/quota/refresh`              | `refresh-quotas`            |
| Stav           | —                                      | `status`                    |

> **Důležité:** Dashboard API je dostupné pouze když běží switcher jako daemon
> (`antigravity-account-switcher serve` nebo `launch`). Pro samotné čtení seznamu účtů stačí CLI.

---

## Požadavky

- Windows s [.NET SDK 9.0+](https://dotnet.microsoft.com/download) (pro build).
- Flow Launcher **1.20.x** (běží na .NET 7). Plugin proto cílí `net7.0-windows` a
  `Flow.Launcher.Plugin` **4.7.0**. Pro Flow Launcher postavený na .NET 9 změň v `.csproj`
  `TargetFramework` na `net9.0-windows` a balíček na `5.x`.
- Nainstalovaný a nakonfigurovaný `antigravity-account-switcher`
  (binárka v `PATH`, v `%LOCALAPPDATA%\antigravity-account-switcher\` nebo v `~/.local/bin`).
- Pro WSL režim: `wsl.exe` s nainstalovaným switcherem v distribuci.

Plugin automaticky rozpozná, zda switcher běží nativně na Windows nebo ve WSL
(`wsl.exe -e antigravity-account-switcher`). Cestu lze vynutit proměnnou prostředí
`ANTIGRAVITY_SWITCHER_BIN`.

---

## Sestavení

```powershell
# Pouze build do .\publish
pwsh ./scripts/build.ps1

# Nebo přímo:
dotnet publish .\Flow.Launcher.Plugin.AntigravitySwitcher.csproj -c Release -o .\publish
```

## Nasazení do Flow Launcheru

> **Než nasadíš plugin, zavři Flow Launcher** — drží načtené DLL pluginu zamčené a kopírování by selhalo.
> Následně stačí Flow Launcher znovu spustit (načte plugin při startu).

```powershell
# Sestaví a zkopíruje plugin do %APPDATA%\FlowLauncher\Plugins\AntigravitySwitcher
pwsh ./scripts/build.ps1 -Deploy
```

Ruční ekvivalent:

1. Sestav plugin: `dotnet publish -c Release -o .\publish`
2. Zkopíruj **celý obsah** složky `.\publish` do
   `%APPDATA%\FlowLauncher\Plugins\AntigravitySwitcher`
   (na úrovni složky musí ležet `plugin.json` a
   `Flow.Launcher.Plugin.AntigravitySwitcher.dll`).
3. Restartuj Flow Launcher (nebo v nastavení použij *Reload Plugin Data*).
4. Napiš `ag`.

---

## Použití

| Příkaz              | Popis |
| :------------------ | :---- |
| `ag`                | Seznam účtů. První položka **„Rychlé přepnutí na další účet“** přepne na účet s nejlepší volnou kvótou. |
| `ag <filtr>`        | Vyfiltruje účty podle emailu / ID. |
| `ag add`            | Přidá nový Google účet (`add-account`, OAuth v prohlížeči). |
| `ag remove [filtr]` | Nabídne účty k odebrání; Enter potvrdí smazání. |
| `ag refresh`        | Vynutí okamžitou aktualizaci kvót. |
| `ag web`            | Otevře webový dashboard (`http://127.0.0.1:8080`, případně port z konfigurace). |
| `ag status`         | Zobrazí stav switcheru (kopírovatelné). |
| `ag help`           | Nápověda s přehledem příkazů. |

U každého účtu je v titulku označení `[AKTIVNÍ]`, v podtitulku stav
(Active/Standby/Exhausted/Error/Disabled), zbývající kvóta v % a čas do resetu limitu.

### Kontextové menu (`Shift + Enter` / pravé tlačítko)

- **Odebrat tento účet**
- **Zkopírovat email do schránky**
- **Otevřít v prohlížeči**

---

## Výkon a spolehlivost

- Všechna volání CLI/HTTP jsou **asynchronní** s timeoutem — Flow Launcher nezamrzne.
- Seznam účtů se **cachuje** (8 s) a obnovuje na pozadí, takže je UI okamžitě responzivní.
- Ikony stavu: aktivní / standby / vyčerpaný účet (viz `Images/`).

### Proměnné prostředí

| Proměnná                     | Význam |
| :--------------------------- | :----- |
| `ANTIGRAVITY_SWITCHER_BIN`   | Explicitní cesta k binárce switcheru. |
| `ANTIGRAVITY_PORT`           | Port webového dashboardu (přednost před konfigurací). |

---

## Vývoj a testy

```powershell
dotnet build .\AntigravitySwitcher.sln -c Release
dotnet test  .\AntigravitySwitcher.sln -c Release
```

Projekt testů (`tests/`) ověřuje parsery CLI/API odpovědí, formátování účtů,
cache a logiku výběru dalšího účtu.

### Struktura

```
Flow.Launcher.Plugin.AntigravitySwitcher.csproj   # hlavní plugin (net7.0-windows)
plugin.json                                       # manifest Flow Launcheru (keyword: ag)
Main.cs                                           # IPlugin + IContextMenu
Cli/                                              # lokalizace binárky, spouštění CLI, dashboard API, parsery
Models/AccountInfo.cs                             # model účtu + formátování
Services/                                         # AccountService, cache
Images/                                           # ikony (generuje scripts/Generate-Icons.ps1)
scripts/build.ps1                                 # build + deploy
scripts/Generate-Icons.ps1                        # regenerace PNG ikon
```

### Regenerace ikon

```powershell
pwsh ./scripts/Generate-Icons.ps1
```

---

## Řešení problémů

- **„Nástroj antigravity-account-switcher nebyl nalezen“** — přidej binárku do `PATH`,
  nastav `ANTIGRAVITY_SWITCHER_BIN`, nebo zajisti, že je dostupná ve WSL.
- **Přepnutí účtu selže** — spusť switcher jako daemon
  (`antigravity-account-switcher serve` nebo `launch`); bez běžícího dashboardu nelze účet přepnout.
- **Nevidím změny** — použij `ag refresh`, případně restartuj Flow Launcher.

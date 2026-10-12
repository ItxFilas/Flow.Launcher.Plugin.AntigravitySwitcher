# Builds (and optionally deploys) the Antigravity Switcher Flow Launcher plugin.
#
# Usage:
#   pwsh ./scripts/build.ps1                 # publish Release build to ./publish
#   pwsh ./scripts/build.ps1 -Deploy         # publish and copy into %APPDATA%\FlowLauncher\Plugins\AntigravitySwitcher
#   pwsh ./scripts/build.ps1 -Configuration Debug -Deploy
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\publish'),
    [string]$PluginFolderName = 'AntigravitySwitcher',
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'Flow.Launcher.Plugin.AntigravitySwitcher.csproj'
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

function Resolve-DotNet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidate = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $candidate) { return $candidate }

    throw ".NET SDK ('dotnet') was not found on PATH. Install the .NET SDK from https://dotnet.microsoft.com/download."
}

$dotnet = Resolve-DotNet

Write-Host "Publishing $project ($Configuration) -> $OutputDirectory"
& $dotnet publish $project -c $Configuration -o $OutputDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host "Publish completed: $OutputDirectory"

if ($Deploy) {
    $target = Join-Path $env:APPDATA "FlowLauncher\Plugins\$PluginFolderName"
    Write-Host "Deploying to $target"

    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }

    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -Path (Join-Path $OutputDirectory '*') -Destination $target -Recurse -Force

    Write-Host "Deployed to $target"
    Write-Host "Restart Flow Launcher (or use 'Reload Plugin Data' in its settings) and type 'ag'."
}

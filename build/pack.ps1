# SPDX-License-Identifier: GPL-3.0-or-later
#
# Builds the installer (section 11): a self-contained, per-user ThemeLens-Setup.exe made with Velopack,
# plus a portable zip.
#
# STATUS: written for milestone M5 and not yet exercised end to end. Signing is not wired up; see
# build/signing-notes.md once it exists. Until then the installer is unsigned and Windows SmartScreen will warn.
#
#   pwsh build/pack.ps1 -Version 0.1.0
#
# Requires the Velopack command-line tool:  dotnet tool install -g vpk

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Runtime = "win-x64",
    [string]$Output = "Releases"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts/publish/$Runtime"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "The Velopack tool 'vpk' is not installed. Run: dotnet tool install -g vpk"
}

Write-Host "Running the tests that do not need a desktop session..."
dotnet test --solution (Join-Path $root "ThemeLens.slnx") -c Release --filter-not-trait "Category=Ui"
if ($LASTEXITCODE -ne 0) { throw "Tests failed; not packing." }

Write-Host "Checking dependency licences..."
dotnet run (Join-Path $root "build/check-licences.cs") -- `
    (Join-Path $root "src/ThemeLens.Core") `
    (Join-Path $root "src/ThemeLens.Engine") `
    (Join-Path $root "src/ThemeLens.Platform.Windows") `
    (Join-Path $root "src/ThemeLens.App")
if ($LASTEXITCODE -ne 0) { throw "Licence check failed; not packing." }

Write-Host "Publishing self-contained for $Runtime..."
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish (Join-Path $root "src/ThemeLens.App/ThemeLens.App.csproj") `
    -c Release -r $Runtime --self-contained true `
    -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host "Packing with Velopack..."
vpk pack `
    --packId ThemeLens `
    --packVersion $Version `
    --packDir $publish `
    --mainExe ThemeLens.App.exe `
    --packTitle "ThemeLens" `
    --outputDir (Join-Path $root $Output)
if ($LASTEXITCODE -ne 0) { throw "Velopack failed." }

Write-Host "Done. Installer and update packages are in $Output."

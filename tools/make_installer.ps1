#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes DefaultMonitorSwitcher and compiles the Inno Setup installer.
.DESCRIPTION
    1. dotnet publish using the Release publish profile
    2. Compiles installer/DefaultMonitorSwitcher.iss with ISCC.exe
    Output: installer-output\DefaultMonitorSwitcher-Setup-<version>.exe
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent

# ── Locate ISCC ───────────────────────────────────────────────────────────────

$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Error "ISCC.exe not found. Install Inno Setup 6 or add its location to `$isccCandidates in this script."
}

# ── Publish ───────────────────────────────────────────────────────────────────

Write-Host "Publishing..." -ForegroundColor Cyan
& dotnet publish "$root\DefaultMonitorSwitcher.csproj" -p:PublishProfile=Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# ── Compile installer ─────────────────────────────────────────────────────────

Write-Host "Compiling installer..." -ForegroundColor Cyan
& $iscc "$root\installer\DefaultMonitorSwitcher.iss"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# ── Report output ─────────────────────────────────────────────────────────────

$output = Get-ChildItem "$root\installer-output\*.exe" |
          Sort-Object LastWriteTime -Descending |
          Select-Object -First 1

if ($output) {
    $size = "{0:N1} MB" -f ($output.Length / 1MB)
    Write-Host "Done: $($output.FullName) ($size)" -ForegroundColor Green
}

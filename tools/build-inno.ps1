<#
.SYNOPSIS
  Compiles packaging/inno/barbican.iss with Inno Setup (the release workflow, CI and a local build
  all use this). Output: <Out>\Barbican-Setup-<Version>.exe.

.EXAMPLE
  cargo build --release
  pwsh tools/build-inno.ps1 -Version 0.1.0                 # dist\Barbican-Setup-0.1.0.exe
  pwsh tools/build-inno.ps1 -Version 0.1.0-beta.1 -Out target\inno-scratch

  Looks for ISCC.exe on PATH, then under Program Files (Inno Setup 5, 6, 7). When none is there and
  this runs in CI ($env:CI), installs it with Chocolatey; a local run never installs anything.
  -Exe names another barbican.exe (default target\release\barbican.exe); a stand-in file is enough
  to check that the script compiles.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Exe = '',
    [string]$Out = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$') { throw "version '$Version' is not X.Y.Z[-pre]" }
if ($Exe -eq '') { $Exe = Join-Path $root 'target\release\barbican.exe' }
if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "$Exe does not exist (cargo build --release first)" }
if ($Out -eq '') { $Out = Join-Path $root 'dist' }
New-Item -ItemType Directory -Force $Out | Out-Null

function Find-Iscc {
    $c = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $pf = @(${env:ProgramFiles(x86)}, $env:ProgramFiles, "$env:LOCALAPPDATA\Programs") | Where-Object { $_ }
    foreach ($d in $pf) {
        $f = Get-ChildItem -Path (Join-Path $d 'Inno Setup *\ISCC.exe') -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -Last 1
        if ($f) { return $f.FullName }
    }
    $null
}
$iscc = Find-Iscc
if (-not $iscc -and $env:CI) {
    choco install innosetup -y --no-progress
    $iscc = Find-Iscc
}
if (-not $iscc) { throw 'ISCC.exe (Inno Setup) not found; install Inno Setup, or run this in CI' }

$exeFull = (Resolve-Path -LiteralPath $Exe).Path
$outFull = (Resolve-Path -LiteralPath $Out).Path
& $iscc /Q "/DAppVersion=$Version" "/DExeSource=$exeFull" "/O$outFull" (Join-Path $root 'packaging\inno\barbican.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }
$setup = Join-Path $outFull "Barbican-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "$setup was not produced" }
Write-Host "build-inno: $setup"

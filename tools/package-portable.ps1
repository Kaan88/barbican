#Requires -Version 5.1
<#
.SYNOPSIS
  Zips the win-x64 publish into artifacts/portable/Barbican-<sha>-win-x64.zip (docs/milestones/M1-plan.md
  §5 F1 step 5): the portable build a tester unpacks and runs; no installer, no MSIX.

.DESCRIPTION
  Run tools/build.ps1 publish -Head win first (Release, self-contained, unpackaged; the publish
  check there already refused automation content). This script checks again before zipping, so a
  stale or hand-copied folder cannot ship FlaUI, Win2D or Barbican.Automation.

.EXAMPLE
  tools/build.ps1 publish -Head win
  tools/package-portable.ps1
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$Rid = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Source) { $Source = Join-Path $Root "artifacts/publish/win/$Rid" }
if (-not (Test-Path (Join-Path $Source 'Barbican.exe'))) { throw "[package-portable] no Barbican.exe in $Source (run tools/build.ps1 publish -Head win)" }

$bad = @(Get-ChildItem -Path $Source -Recurse -File -Include 'FlaUI*.dll', 'Microsoft.Graphics.Canvas*.dll', 'Barbican.Automation.dll', 'Interop.UIAutomationClient.dll')
if ($bad.Count -gt 0) { $bad | ForEach-Object { Write-Host "  $($_.FullName)" }; throw '[package-portable] automation content in the publish folder' }
$text = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes((Join-Path $Source 'Barbican.dll')))
if ($text.Contains('BarbicanAutomationBuild')) { throw '[package-portable] the App assembly is an AUTOMATION build' }

$sha = (& git -C $Root rev-parse --short=12 HEAD).Trim()
$dirty = (& git -C $Root status --porcelain --untracked-files=no)
if ($dirty) { $sha += '-dirty' }
$outDir = Join-Path $Root 'artifacts/portable'
New-Item -ItemType Directory -Force $outDir | Out-Null
$zip = Join-Path $outDir "Barbican-$sha-$Rid.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$sw = [System.Diagnostics.Stopwatch]::StartNew()
[System.IO.Compression.ZipFile]::CreateFromDirectory($Source, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
$sw.Stop()
$mb = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
$srcMb = [Math]::Round(((Get-ChildItem $Source -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "[package-portable] $zip ($mb MB from $srcMb MB, $([int]$sw.Elapsed.TotalSeconds) s)"

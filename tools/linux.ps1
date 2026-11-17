#Requires -Version 5.1
<#
.SYNOPSIS
  Builds and tests a pushed branch on the user's Linux VM (there is no GitHub CI; see docs/adr/0002).

.DESCRIPTION
  The VM keeps a clone of the public repo in ~/barbican. This script checks that the branch is
  pushed, then over SSH (key auth only, BatchMode: it never asks for a password) fetches that
  exact commit there and runs tools/build.sh at nice 10 with one build slot and -m:2. One VM job
  runs at a time; a second caller waits.

  The VM address comes from $env:BARBICAN_VM or the first line of .local/linux-vm.txt
  (user@host, gitignored); the key from $env:BARBICAN_VM_KEY or ~/.ssh/barbican_vm.

  "Green" for a change = tools/build.ps1 test passes locally AND tools/linux.ps1 test passes.

.EXAMPLE
  tools/linux.ps1 setup                  # once: .NET SDK into ~/.dotnet on the VM, clone the repo
  tools/linux.ps1 test                   # current branch, all tests
  tools/linux.ps1 test -Branch m1/jmap -Class "*SessionTests"
  tools/linux.ps1 build
  tools/linux.ps1 build -Project spikes/BodyShell/Barbican.Spike.BodyShell.csproj
  tools/linux.ps1 setup-ui               # once: Xvfb + WebKitGTK for running the app head on the VM
  tools/linux.ps1 xrun -Project spikes/BodyShell/Barbican.Spike.BodyShell.csproj -Out s2
                                         # build for net10.0-desktop and run under the VM's own Xvfb;
                                         # results copied to artifacts/linux/<Out>/
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('setup', 'setup-ui', 'build', 'test', 'gen-tokens', 'ping', 'xrun')]
    [string]$Target = 'test',
    [string]$Branch,
    [string]$Class,
    [string]$Method,
    [string]$Project,
    [string]$Out = 'run',
    [int]$TimeoutSeconds = 300,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Split-Path -Parent $PSScriptRoot

$vm = $env:BARBICAN_VM
if (-not $vm) {
    # .local/ is gitignored, so a worktree has none of its own: fall back to the main checkout's copy
    # (the parent of the shared git dir).
    $candidates = @(Join-Path $Root '.local/linux-vm.txt')
    $common = (& git -C $Root rev-parse --path-format=absolute --git-common-dir 2>$null)
    if ($common) { $candidates += Join-Path (Split-Path -Parent $common) '.local/linux-vm.txt' }
    foreach ($file in $candidates) {
        if (-not $vm -and (Test-Path $file)) { $vm = (Get-Content $file | Where-Object { $_ -match '\S' -and $_ -notmatch '^\s*#' } | Select-Object -First 1).Trim() }
    }
}
if (-not $vm) { throw '[linux.ps1] no VM address: set BARBICAN_VM=user@host or write it to .local/linux-vm.txt' }
$key = $env:BARBICAN_VM_KEY
if (-not $key) { $key = Join-Path $HOME '.ssh/barbican_vm' }

$sshArgs = @('-i', $key, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15', '-o', 'ServerAliveInterval=30', $vm)

function Invoke-Vm([string[]]$RemoteArgs) {
    # The remote script travels base64-encoded on the command line (no CRLF or quoting trouble).
    $script = (Get-Content -Raw (Join-Path $PSScriptRoot 'linux-remote.sh')).Replace("`r`n", "`n")
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script))
    $quoted = ($RemoteArgs | ForEach-Object { "'" + ($_ -replace "'", "'\''") + "'" }) -join ' '
    $remote = "echo $b64 | base64 -d > /tmp/barbican-linux-remote.sh && bash /tmp/barbican-linux-remote.sh $quoted"
    & ssh @sshArgs $remote
    if ($LASTEXITCODE -ne 0) { throw "[linux.ps1] VM job failed (exit $LASTEXITCODE)" }
}

if ($Target -eq 'ping') {
    & ssh @sshArgs 'echo ok; uname -sr; nproc'
    if ($LASTEXITCODE -ne 0) { throw "[linux.ps1] cannot reach $vm with key $key (is the key authorized on the VM?)" }
    exit 0
}

if ($Target -eq 'setup') { Invoke-Vm @('setup'); exit 0 }
if ($Target -eq 'setup-ui') { Invoke-Vm @('setup-ui'); exit 0 }

if (-not $Branch) { $Branch = (& git -C $Root rev-parse --abbrev-ref HEAD).Trim() }
$sha = (& git -C $Root rev-parse "refs/heads/$Branch").Trim()
$remoteLine = & git -C $Root ls-remote origin "refs/heads/$Branch"
$remoteSha = if ($remoteLine) { ($remoteLine -split '\s+')[0] } else { '' }
if ($remoteSha -ne $sha) {
    throw "[linux.ps1] $Branch at $($sha.Substring(0, 7)) is not pushed (origin has '$remoteSha'). Push first: every commit is pushed right away."
}

if ($Target -eq 'xrun') {
    if (-not $Project) { throw '[linux.ps1] xrun needs -Project' }
    Write-Host "[linux.ps1] xrun $Project $Branch@$($sha.Substring(0, 7)) on the VM"
    Invoke-Vm @('xrun', $Branch, $sha, $Project.Replace('\', '/'), $Out, "$TimeoutSeconds")
    $local = Join-Path $Root "artifacts/linux/$Out"
    New-Item -ItemType Directory -Force $local | Out-Null
    $scpArgs = @('-i', $key, '-o', 'BatchMode=yes', '-r', "${vm}:barbican-out/$Out/*", $local)
    & scp @scpArgs
    if ($LASTEXITCODE -ne 0) { Write-Host "[linux.ps1] could not copy results back" } else { Write-Host "[linux.ps1] results in $local" }
    exit 0
}

$buildArgs = @('-c', $Configuration)
if ($Project) { $buildArgs += @('--project', $Project.Replace('\', '/')) }
if ($Class) { $buildArgs += @('--class', $Class) }
if ($Method) { $buildArgs += @('--method', $Method) }
Write-Host "[linux.ps1] $Target $Branch@$($sha.Substring(0, 7)) on the VM"
Invoke-Vm (@('run', $Branch, $sha, $Target) + $buildArgs)

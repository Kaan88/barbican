#Requires -Version 5.1
<#
.SYNOPSIS
  The only way agents build, test or run tools in this repo on Windows (PORT-PLAN.md §6.3).

.DESCRIPTION
  The machine froze under parallel builds, so every dotnet invocation here:
    - takes one of N machine-wide build slots (BARBICAN_BUILD_SLOTS, default 2). A slot is the named
      mutex Global\BarbicanDotnetBuild-<i>; a mutex (not a semaphore) so a crashed or killed build
      frees its slot automatically (the next taker gets it as "abandoned");
    - runs at BelowNormal priority (set on this process, inherited by dotnet, MSBuild nodes and
      test hosts; MSBuild also gets -lowPriority; the compiler runs in-process, not in a shared
      VBCSCompiler server that may run at Normal priority);
    - passes -maxCpuCount:4 (BARBICAN_MSBUILD_CPU) explicitly: the dotnet CLI adds a bare
      -maxcpucount that overrides Directory.Build.rsp, which still supplies -nodeReuse:false.
  MSBuild verbosity: pass -verbosity:<level> (-v collides with PowerShell's -Verbose).
  Linux builds run on the Linux VM through tools/linux.ps1, which calls tools/build.sh there.

.EXAMPLE
  tools/build.ps1                     # build Barbican.slnx (Debug)
  tools/build.ps1 test                # rule check, then build and run every test project
  tools/build.ps1 test -Class "*TokenTests"            # xUnit v3 filters (Microsoft.Testing.Platform)
  tools/build.ps1 test -Method "*Palette*"
  tools/build.ps1 test -Project tests/Barbican.Core.Tests/Barbican.Core.Tests.csproj
  tools/build.ps1 build -Project tests/Barbican.Core.Tests/Barbican.Core.Tests.csproj
  tools/build.ps1 gen-tokens          # regenerate design tokens from design/legacy-slint/theme.slint
  tools/build.ps1 gen-tokens -Check   # fail if the committed generated files are stale
  tools/build.ps1 build -Head win     # App head only for one TFM (inner loop): win = WinUI, desktop = Uno Skia
  tools/build.ps1 publish -Head win   # self-contained, unpackaged App into artifacts/publish/<head>/<rid>/ (Release)
  tools/build.ps1 publish -Head desktop -Rid linux-x64
  tools/build.ps1 build -Configuration Automation -Head win   # the hidden-automation build (docs/adr/0007)
  tools/build.ps1 build -Configuration Automation -Writes -Head win   # plus the write-capable test commands (AUTOMATION_WRITES; <test-account> only, M2 plan 3.2)
  tools/build.ps1 shutdown            # stop the compiler server when you finish (only if no build runs)
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'test', 'restore', 'clean', 'gen-tokens', 'rules', 'shutdown', 'publish')]
    [string]$Target = 'build',
    [string]$Project,
    [ValidateSet('Debug', 'Release', 'Automation')]
    [string]$Configuration = 'Debug',
    [ValidateSet('', 'win', 'desktop', 'both')]
    [string]$Head = '',
    [string]$Class,
    [string]$Method,
    [switch]$Check,
    [switch]$Writes,
    [string]$Rid,
    [int]$WaitMinutes = 90,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $PSScriptRoot
if (-not $Project) { $Project = Join-Path $Root 'Barbican.slnx' }
elseif (-not [System.IO.Path]::IsPathRooted($Project)) { $Project = Join-Path $Root $Project }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

function Get-SlotCount {
    $n = 2
    if ($env:BARBICAN_BUILD_SLOTS) { $n = [int]$env:BARBICAN_BUILD_SLOTS }
    if ($n -lt 1) { $n = 1 }
    return $n
}

function Test-TakeMutex([System.Threading.Mutex]$Mutex) {
    try { return $Mutex.WaitOne(0) }
    catch {
        $e = $_.Exception
        while ($e) {
            if ($e -is [System.Threading.AbandonedMutexException]) { return $true }  # previous holder died: the slot is ours
            $e = $e.InnerException
        }
        throw
    }
}

function Enter-BuildSlot {
    $count = Get-SlotCount
    $mutexes = @(0..($count - 1) | ForEach-Object { New-Object System.Threading.Mutex($false, "Global\BarbicanDotnetBuild-$_") })
    $deadline = (Get-Date).AddMinutes($WaitMinutes)
    $said = $false
    while ($true) {
        for ($i = 0; $i -lt $count; $i++) {
            if (Test-TakeMutex $mutexes[$i]) {
                for ($j = 0; $j -lt $count; $j++) { if ($j -ne $i) { $mutexes[$j].Dispose() } }
                Write-Host "[build.ps1] build slot $($i + 1)/$count taken"
                return $mutexes[$i]
            }
        }
        if (-not $said) { Write-Host "[build.ps1] all $count build slots busy; waiting (BARBICAN_BUILD_SLOTS)..."; $said = $true }
        if ((Get-Date) -gt $deadline) { throw "[build.ps1] no build slot free after $WaitMinutes minutes" }
        Start-Sleep -Milliseconds 1500
    }
}

function Exit-BuildSlot([System.Threading.Mutex]$Mutex) {
    if ($Mutex) {
        try { $Mutex.ReleaseMutex() } catch { }
        $Mutex.Dispose()
    }
}

# Real input on the user's machine is forbidden (PORT-PLAN.md §4.4). The banned-API analyzer covers
# managed APIs; this catches P/Invoke declarations and scripts.
function Invoke-RuleCheck {
    $pattern = '\b(SendInput|keybd_event|mouse_event|SetCursorPos|InputInjector)\b|FlaUI\.Core\.Input\.(Mouse|Keyboard)|xdotool'
    $dirs = @('src', 'tests', 'tools', 'spikes', 'prototype') | ForEach-Object { Join-Path $Root $_ } | Where-Object { Test-Path $_ }
    $hits = Get-ChildItem -Path $dirs -Recurse -File -Include *.cs, *.xaml, *.ps1, *.sh, *.py, *.csproj |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Name -notin @('build.ps1', 'build.sh') } |
        Select-String -Pattern $pattern
    if ($hits) {
        $hits | ForEach-Object { Write-Host "  $($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
        throw '[build.ps1] real-input API found (forbidden on the user''s machine; see CLAUDE.md)'
    }
    Write-Host '[build.ps1] rules: no real-input APIs'
}

function Invoke-Dotnet([string[]]$Arguments) {
    Write-Host "[build.ps1] dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "[build.ps1] dotnet exited with $LASTEXITCODE" }
}

# The hidden automation (src/Barbican.Automation, FlaUI, Win2D) must never reach a Debug or Release
# output (docs/adr/0007 "M0 review", docs/milestones/M1-plan.md §5 F1). Checked on every publish: no
# such DLL in the folder and no BarbicanAutomationBuild marker in the App assembly.
function Assert-NoAutomationContent([string]$Dir) {
    $bad = @(Get-ChildItem -Path $Dir -Recurse -File -Include 'FlaUI*.dll', 'Microsoft.Graphics.Canvas*.dll', 'Barbican.Automation.dll', 'Interop.UIAutomationClient.dll' -ErrorAction SilentlyContinue)
    foreach ($f in $bad) { Write-Host "  $($f.FullName)" }
    if ($bad.Count -gt 0) { throw '[build.ps1] automation content in a non-Automation publish' }
    $app = Join-Path $Dir 'Barbican.dll'
    if (-not (Test-Path $app)) { throw "[build.ps1] no Barbican.dll in $Dir" }
    $bytes = [System.IO.File]::ReadAllBytes($app)
    $text = [System.Text.Encoding]::ASCII.GetString($bytes)
    if ($text.Contains('BarbicanAutomationBuild')) { throw '[build.ps1] BarbicanAutomationBuild marker in a non-Automation App assembly' }
    Write-Host '[build.ps1] publish check: no automation content'
}

function Get-HeadArgs {
    # The App head (Uno single project, spike S1) reads BarbicanHead to build one TFM in the inner loop.
    switch ($Head) {
        'win' { return @('-p:BarbicanHead=win') }
        'desktop' { return @('-p:BarbicanHead=desktop') }
        'both' { return @('-p:BarbicanHead=both') }
        default { return @() }
    }
}

if ($Target -eq 'rules') { Invoke-RuleCheck; exit 0 }

$me = [System.Diagnostics.Process]::GetCurrentProcess()
$oldPriority = $me.PriorityClass
$slot = $null
try {
    if ($Target -eq 'shutdown') {
        # Only when nobody builds: take every slot without waiting, else leave the server alone.
        $count = Get-SlotCount
        $held = @()
        foreach ($i in 0..($count - 1)) {
            $m = New-Object System.Threading.Mutex($false, "Global\BarbicanDotnetBuild-$i")
            if (Test-TakeMutex $m) { $held += $m } else { $m.Dispose() }
        }
        try {
            if ($held.Count -eq $count) { Invoke-Dotnet @('build-server', 'shutdown') }
            else { Write-Host '[build.ps1] a build is running elsewhere; compiler server left running' }
        }
        finally { foreach ($m in $held) { Exit-BuildSlot $m } }
        exit 0
    }

    if ($Target -eq 'test') { Invoke-RuleCheck }

    $slot = Enter-BuildSlot
    $me.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::BelowNormal

    # In-process compiler: a shared VBCSCompiler server may have been started by someone else at
    # Normal priority and would compile our code there (and linger). BARBICAN_SHARED_COMPILER=1 opts out.
    # The dotnet CLI passes a bare -maxcpucount (= all 28 cores) that overrides Directory.Build.rsp,
    # so the node cap must be explicit here (measured: MSBuildNodeCount 28 without it).
    $cpu = 4
    if ($env:BARBICAN_MSBUILD_CPU) { $cpu = [int]$env:BARBICAN_MSBUILD_CPU }
    if ($Writes -and $Configuration -ne 'Automation') { throw '[build.ps1] -Writes needs -Configuration Automation (AUTOMATION_WRITES exists only together with AUTOMATION)' }
    $common = @('-c', $Configuration, '-lowPriority', "-maxCpuCount:$cpu") + (Get-HeadArgs)
    if ($Writes) { $common += '-p:AutomationWrites=true' }
    if ($env:BARBICAN_SHARED_COMPILER -ne '1') { $common += '-p:UseSharedCompilation=false' }
    if ($Rest) { $extra = $Rest } else { $extra = @() }
    switch ($Target) {
        'restore' { Invoke-Dotnet (@('restore', $Project, '-lowPriority', "-maxCpuCount:$cpu") + $extra) }
        'build' { Invoke-Dotnet (@('build', $Project) + $common + $extra) }
        'clean' { Invoke-Dotnet (@('clean', $Project, '-c', $Configuration, "-maxCpuCount:$cpu") + $extra) }
        'test' {
            # global.json opts into Microsoft.Testing.Platform: projects are passed with --project/--solution.
            # Build first (MSBuild switches such as -lowPriority are not test-app options), then run.
            Invoke-Dotnet (@('build', $Project) + $common)
            if ($Project -like '*.slnx' -or $Project -like '*.sln') { $testArgs = @('test', '--solution', $Project) }
            else { $testArgs = @('test', '--project', $Project) }
            $testArgs += @('-c', $Configuration, '--no-build')
            if ($Class) { $testArgs += @('--filter-class', $Class) }
            if ($Method) { $testArgs += @('--filter-method', $Method) }
            Invoke-Dotnet ($testArgs + $extra)
        }
        'publish' {
            # The App head, one TFM, self-contained and unpackaged (spike S1, docs/adr/0004).
            if ($Head -notin @('win', 'desktop')) { throw '[build.ps1] publish needs -Head win or -Head desktop' }
            if ($PSBoundParameters.ContainsKey('Project') -eq $false) { $Project = Join-Path $Root 'src/Barbican.App/Barbican.App.csproj' }
            if (-not $Rid) { $Rid = if ($Head -eq 'win') { 'win-x64' } else { 'linux-x64' } }
            $tfm = if ($Head -eq 'win') { 'net10.0-windows10.0.26100.0' } else { 'net10.0-desktop' }
            $out = Join-Path $Root "artifacts/publish/$Head/$Rid"
            $pubConfig = if ($PSBoundParameters.ContainsKey('Configuration')) { $Configuration } else { 'Release' }
            $pub = @('publish', $Project, '-c', $pubConfig, '-f', $tfm, '-r', $Rid, '--self-contained', '-o', $out,
                '-lowPriority', "-maxCpuCount:$cpu") + (Get-HeadArgs)
            # The Windows App SDK picks its publish profile by Platform (win-<Platform>.pubxml): without
            # it publish fails with NETSDK1198 (win-AnyCPU.pubxml).
            if ($Head -eq 'win') { $pub += "-p:Platform=$($Rid.Split('-')[-1])" }
            if ($env:BARBICAN_SHARED_COMPILER -ne '1') { $pub += '-p:UseSharedCompilation=false' }
            Invoke-Dotnet ($pub + $extra)
            if ($pubConfig -ne 'Automation') { Assert-NoAutomationContent $out }
            Write-Host "[build.ps1] published to $out"
        }
        'gen-tokens' {
            $genProject = Join-Path $Root 'tools/Barbican.GenTokens/Barbican.GenTokens.csproj'
            Invoke-Dotnet (@('build', $genProject) + $common)
            $genArgs = @('run', '--project', $genProject, '-c', $Configuration, '--no-build', '--', '--root', $Root)
            if ($Check) { $genArgs += '--check' }
            Invoke-Dotnet ($genArgs + $extra)
        }
    }
}
finally {
    $me.PriorityClass = $oldPriority
    Exit-BuildSlot $slot
}

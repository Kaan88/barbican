#Requires -Version 5.1
<#
.SYNOPSIS
  Runs the App hidden on Windows (docs/adr/0007): a --smoke run, or the AUTOMATION build with its
  loopback command server. Never shows a window on the user's screen and never sends real input.

.DESCRIPTION
  The App decides the hidden mode itself (HiddenWindowOptions: an AUTOMATION build or --smoke is
  off-screen at (-12000,200), WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, never activated). This script
  only launches it with SW_HIDE in STARTUPINFO (so even a first ShowWindow cannot activate), its own
  port and profile, records the foreground window and cursor before and after (the no-focus proof),
  and stops only the process it started (pid file plus start time).

.EXAMPLE
  tools/auto-run.ps1 smoke -Exe artifacts/publish/win/win-x64/Barbican.exe
  tools/auto-run.ps1 start -Port 8801          # the Automation build (tools/build.ps1 build -Configuration Automation -Head win)
  tools/auto-run.ps1 send -Port 8801 -Command 'wshot artifacts/auto/8801/w.png'
  tools/auto-run.ps1 scenario -Port 8801       # wshot, probe-start wgc, 2 s idle, probe-stop (0 frames), close
  tools/auto-run.ps1 stop -Port 8801
  tools/auto-run.ps1 start -Port 8802 -PrivateDesktop
                     # on a desktop of its own (CreateDesktop, never switched to): nothing it opens,
                     # not even a windowed popup, can appear on the user's screen; for popup-probe

  -PrivateDesktop: CreateProcess with lpDesktop "WinSta0\barbican-auto-<port>". The desktop is a
  session object that disappears with the last process on it; SwitchDesktop is never called.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('smoke', 'start', 'send', 'stop', 'scenario')]
    [string]$Target = 'smoke',
    [string]$Exe,
    [int]$Port = 8801,
    [string]$ProfileName,
    [string]$Report,
    [string]$Command,
    [int]$TimeoutSeconds = 90,
    [switch]$PrivateDesktop,
    [switch]$CaptureOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Split-Path -Parent $PSScriptRoot
$State = Join-Path $Root "artifacts/auto/$Port"
New-Item -ItemType Directory -Force $State | Out-Null

Add-Type -Namespace BarbicanAuto -Name Focus -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
'@

Add-Type -Namespace BarbicanAuto -Name Desk -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public struct STARTUPINFO { public int cb; public string lpReserved; public string lpDesktop; public string lpTitle; public int dwX; public int dwY; public int dwXSize; public int dwYSize; public int dwXCountChars; public int dwYCountChars; public int dwFillAttribute; public int dwFlags; public short wShowWindow; public short cbReserved2; public System.IntPtr lpReserved2; public System.IntPtr hStdInput; public System.IntPtr hStdOutput; public System.IntPtr hStdError; }
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct PROCESS_INFORMATION { public System.IntPtr hProcess; public System.IntPtr hThread; public int dwProcessId; public int dwThreadId; }
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
public static extern System.IntPtr CreateDesktopW(string name, System.IntPtr device, System.IntPtr mode, int flags, uint access, System.IntPtr attributes);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
public static extern bool CloseDesktop(System.IntPtr desktop);
[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
public static extern bool CreateProcessW(string app, System.Text.StringBuilder cmd, System.IntPtr pa, System.IntPtr ta, bool inherit, int flags, System.IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern bool CloseHandle(System.IntPtr h);
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct SECURITY_ATTRIBUTES { public int nLength; public System.IntPtr lpSecurityDescriptor; public int bInheritHandle; }
[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
public static extern System.IntPtr CreateFileW(string name, uint access, uint share, ref SECURITY_ATTRIBUTES sa, uint disposition, uint flags, System.IntPtr template);
'@

# Launches the App on a desktop of its own (never the input desktop); returns the process.
# -CaptureOutput (diagnosis only) writes the App's stdout and stderr to the state folder; the handles
# are inherited, so a calling shell that waits on a pipe also waits for the App.
function Start-OnPrivateDesktop([string]$Path, [hashtable]$Environment) {
    $name = "barbican-auto-$Port"
    $desk = [BarbicanAuto.Desk]::CreateDesktopW($name, [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x10000000, [IntPtr]::Zero)
    if ($desk -eq [IntPtr]::Zero) { throw "[auto-run] CreateDesktop failed ($([Runtime.InteropServices.Marshal]::GetLastWin32Error()))" }
    $saved = @{}
    foreach ($k in $Environment.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $Environment[$k]) }
    try {
        $si = New-Object BarbicanAuto.Desk+STARTUPINFO
        $si.cb = [Runtime.InteropServices.Marshal]::SizeOf($si)
        $si.lpDesktop = "WinSta0\$name"
        $si.dwFlags = 1          # STARTF_USESHOWWINDOW
        $si.wShowWindow = 0      # SW_HIDE
        $inherit = $false
        $files = @()
        if ($CaptureOutput) {
            $sa = New-Object BarbicanAuto.Desk+SECURITY_ATTRIBUTES
            $sa.nLength = [Runtime.InteropServices.Marshal]::SizeOf($sa); $sa.bInheritHandle = 1
            foreach ($n in 'stdout.txt', 'stderr.txt') { $files += [BarbicanAuto.Desk]::CreateFileW((Join-Path $State $n), 0x40000000, 1, [ref]$sa, 2, 0x80, [IntPtr]::Zero) }
            $si.dwFlags = $si.dwFlags -bor 0x100   # STARTF_USESTDHANDLES
            $si.hStdOutput = $files[0]; $si.hStdError = $files[1]
            $inherit = $true
        }
        $pi = New-Object BarbicanAuto.Desk+PROCESS_INFORMATION
        $cmd = New-Object System.Text.StringBuilder("`"$Path`"")
        if (-not [BarbicanAuto.Desk]::CreateProcessW($Path, $cmd, [IntPtr]::Zero, [IntPtr]::Zero, $inherit, 0, [IntPtr]::Zero, (Split-Path -Parent $Path), [ref]$si, [ref]$pi)) {
            throw "[auto-run] CreateProcess on the private desktop failed ($([Runtime.InteropServices.Marshal]::GetLastWin32Error()))"
        }
        foreach ($f in $files) { [void][BarbicanAuto.Desk]::CloseHandle($f) }
        [void][BarbicanAuto.Desk]::CloseHandle($pi.hThread)
        [void][BarbicanAuto.Desk]::CloseHandle($pi.hProcess)
        $p = Get-Process -Id $pi.dwProcessId
        [void]$p.Handle
        $script:DesktopHandle = $desk
        return $p
    }
    catch { [void][BarbicanAuto.Desk]::CloseDesktop($desk); throw }
    finally { foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
}

# Our handle keeps the desktop alive while the App attaches to it; once the App has windows on it,
# its threads keep it, and it disappears with the App.
function Close-PrivateDesktop {
    if ($script:DesktopHandle) { [void][BarbicanAuto.Desk]::CloseDesktop($script:DesktopHandle); $script:DesktopHandle = $null }
}
$script:DesktopHandle = $null

function Get-FocusState {
    $p = New-Object BarbicanAuto.Focus+POINT
    [void][BarbicanAuto.Focus]::GetCursorPos([ref]$p)
    return "fg=$([BarbicanAuto.Focus]::GetForegroundWindow()) cursor=$($p.X),$($p.Y)"
}

function Resolve-Exe([string]$Default) {
    $path = if ($Exe) { $Exe } else { $Default }
    if (-not [System.IO.Path]::IsPathRooted($path)) { $path = Join-Path $Root $path }
    if (-not (Test-Path $path)) { throw "[auto-run] no App at $path" }
    return (Resolve-Path $path).Path
}

# Starts the App with SW_HIDE and the given environment; returns the process.
# Output is redirected only for runs this script waits for (smoke): a redirected child inherits the
# caller's handles and would hold a pipe of the calling shell open while it runs.
function Start-Hidden([string]$Path, [string[]]$Arguments, [hashtable]$Environment, [switch]$Capture) {
    $saved = @{}
    foreach ($k in $Environment.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $Environment[$k]) }
    try {
        $start = @{ FilePath = $Path; PassThru = $true; WindowStyle = 'Hidden'; WorkingDirectory = (Split-Path -Parent $Path) }
        if ($Capture) { $start.RedirectStandardOutput = (Join-Path $State 'stdout.txt'); $start.RedirectStandardError = (Join-Path $State 'stderr.txt') }
        if ($Arguments) { $start.ArgumentList = $Arguments }
        $p = Start-Process @start
        [void]$p.Handle   # keeps the handle so ExitCode is readable after exit
        return $p
    }
    finally { foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
}

function Send-Line([string]$Line, [int]$TimeoutMs = 30000) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Port)
        $client.ReceiveTimeout = $TimeoutMs
        $stream = $client.GetStream()
        $writer = New-Object System.IO.StreamWriter($stream, (New-Object System.Text.UTF8Encoding($false)))
        $writer.NewLine = "`n"; $writer.AutoFlush = $true
        $reader = New-Object System.IO.StreamReader($stream, (New-Object System.Text.UTF8Encoding($false)))
        $writer.WriteLine($Line)
        return $reader.ReadLine()
    }
    finally { $client.Dispose() }
}

function Wait-Port([System.Diagnostics.Process]$Process) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) { throw "[auto-run] the App exited with $($Process.ExitCode) before listening" }
        try { $r = Send-Line 'title' 2000; if ($r) { return } } catch { Start-Sleep -Milliseconds 250 }
    }
    throw "[auto-run] no automation server on port $Port after $TimeoutSeconds s"
}

function Stop-Own {
    $pidFile = Join-Path $State 'pid.txt'
    if (-not (Test-Path $pidFile)) { Write-Host '[auto-run] nothing started on this port'; return }
    $id, $started = (Get-Content $pidFile -Raw).Trim() -split '\|'
    $p = Get-Process -Id ([int]$id) -ErrorAction SilentlyContinue
    if ($p -and $p.StartTime.ToString('o') -eq $started) {
        try { Write-Host "[auto-run] close: $(Send-Line 'close' 5000)" } catch { }
        if (-not $p.WaitForExit(10000)) { Write-Host "[auto-run] stopping our own process $id"; Stop-Process -Id $p.Id -Force }
    }
    Remove-Item $pidFile -Force
}

switch ($Target) {
    'smoke' {
        $path = Resolve-Exe 'artifacts/publish/win/win-x64/Barbican.exe'
        if (-not $Report) { $Report = Join-Path $State 'smoke.json' }
        if (Test-Path $Report) { Remove-Item $Report -Force }
        $before = Get-FocusState
        $env_ = @{ BARBICAN_PROFILE = $(if ($ProfileName) { $ProfileName } else { 'smoke' }) }
        $p = Start-Hidden $path @('--smoke', "`"$Report`"") $env_ -Capture
        if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { Stop-Process -Id $p.Id -Force; throw '[auto-run] smoke run did not exit' }
        $after = Get-FocusState
        Write-Host "[auto-run] smoke exit $($p.ExitCode); focus before: $before; after: $after"
        if (Test-Path $Report) { Get-Content $Report -Raw | Write-Host } else { throw '[auto-run] no smoke report' }
        # The cursor moves with the user's own hand; only a changed foreground window would point at the App.
        if (($before -split ' ')[0] -ne ($after -split ' ')[0]) { Write-Host '[auto-run] WARNING: the foreground window changed during the run' }
        exit $p.ExitCode
    }
    'start' {
        Stop-Own
        $path = Resolve-Exe 'src/Barbican.App/bin/Automation/net10.0-windows10.0.26100.0/win-x64/Barbican.exe'
        $before = Get-FocusState
        $envAuto = @{ BARBICAN_AUTOMATION_PORT = "$Port"; BARBICAN_PROFILE = $(if ($ProfileName) { $ProfileName } else { "auto-$Port" }) }
        if ($PrivateDesktop) { $p = Start-OnPrivateDesktop $path $envAuto } else { $p = Start-Hidden $path @() $envAuto }
        Set-Content (Join-Path $State 'pid.txt') "$($p.Id)|$($p.StartTime.ToString('o'))"
        Set-Content (Join-Path $State 'focus-before.txt') $before
        try { Wait-Port $p } finally { Close-PrivateDesktop }
        Write-Host "[auto-run] Automation build listening on 127.0.0.1:$Port (pid $($p.Id)); focus $before -> $(Get-FocusState)"
    }
    'send' { Send-Line $Command }
    'stop' { Stop-Own }
    'scenario' {
        # docs/milestones/M1-plan.md §5 F1 exit: the hidden window composes no frame while idle.
        $shot = Join-Path $State 'wshot.png'
        Write-Host "wshot: $(Send-Line "wshot $shot")"
        Write-Host "probe-start: $(Send-Line 'probe-start wgc')"
        Start-Sleep -Seconds 2
        $probe = Send-Line 'probe-stop'
        Set-Content (Join-Path $State 'idle-probe.json') $probe
        $frames = ($probe | ConvertFrom-Json).frames.Count
        Write-Host "probe-stop: $frames frames in 2 s idle"
        Write-Host "popup-guard: $(Send-Line 'popup-guard')"
        Write-Host "focus-guard: $(Send-Line 'focus-guard')"
        $before = Get-Content (Join-Path $State 'focus-before.txt') -ErrorAction SilentlyContinue
        Write-Host "focus before start: $before; now: $(Get-FocusState)"
        Stop-Own
        if ($frames -ne 0) { throw "[auto-run] idle window composed $frames frames (expected 0)" }
    }
}

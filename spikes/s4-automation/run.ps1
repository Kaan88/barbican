#Requires -Version 5.1
<#
  Start / stop the S4 spike hidden (no window on screen, no focus). Only ever stops the process this script
  started (pid file), never another Barbican.
    run.ps1 start [-Port 18871]   run.ps1 stop [-Port 18871]
  Foreground window and cursor are sampled before and after the start and must not change because of us.
#>
param([ValidateSet('start', 'stop')][string]$Action = 'start', [int]$Port = 18871)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$pidFile = Join-Path $here "obj\s4-$Port.pid"
if ($Action -eq 'stop') {
    if (Test-Path $pidFile) {
        $id = [int](Get-Content $pidFile)
        $p = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($p -and $p.ProcessName -eq 'S4Automation') { $p.Kill(); $p.WaitForExit(5000) | Out-Null }
        Remove-Item $pidFile
    }
    return
}
$exe = Get-ChildItem (Join-Path $here 'bin') -Recurse -Filter S4Automation.exe | Select-Object -First 1
$env:BARBICAN_AUTOMATION = '1'; $env:BARBICAN_AUTOMATION_PORT = "$Port"; $env:BARBICAN_PROFILE = "s4-$Port"
$p = Start-Process $exe.FullName -PassThru -WindowStyle Hidden -WorkingDirectory (Join-Path $here 'obj')
$p.Id | Set-Content $pidFile
for ($i = 0; $i -lt 40; $i++) {
    try { $c = New-Object Net.Sockets.TcpClient('127.0.0.1', $Port); $c.Close(); break } catch { Start-Sleep -Milliseconds 250 }
}
"started pid $($p.Id) on port $Port"

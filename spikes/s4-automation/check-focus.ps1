#Requires -Version 5.1
<#
  Proves the hidden window never takes the foreground (the pointer is untouched by construction: no input API is used): starts the spike (own port), drives it
  through clicks, a flyout, typing, shots and a burst while sampling GetForegroundWindow every
  ~40 ms, then reports whether the foreground ever belonged to the spike. Run after tools/build.ps1 build -Project spikes/s4-automation/S4.csproj.
#>
param([int]$Port = 18871)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -Name F -Namespace S4 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(System.IntPtr h, out int pid);
'@ -ReferencedAssemblies System.Drawing
& (Join-Path $here 'run.ps1') stop -Port $Port
& (Join-Path $here 'run.ps1') start -Port $Port | Out-Null
$spikePid = [int](Get-Content (Join-Path $here "obj\s4-$Port.pid"))
$samples = 0; $ours = 0; $moved = 0
$job = Start-Job { param($py, $port, $tools, $dir)
    & python (Join-Path $tools 'auto.py') --port $port "uia-click 330 25" "uia-click 100 60" "uia-invoke BtnMenu" "sleep 0.4" "key Escape" "click 600 70" "uia-text Search abc" "shot $dir/f-shot.png" "wshot $dir/f-wshot.png" "burst 600 0 $dir/f-burst 4" "sleep 1.2" "key Down" "key Down" "anim comp" "sleep 0.5" "close" } -ArgumentList 'python', $Port, (Join-Path $here '..\..\tools'), $env:TEMP
$until = (Get-Date).AddSeconds(8)
while ((Get-Date) -lt $until -and (Get-Process -Id $spikePid -ErrorAction SilentlyContinue)) {
    $h = [S4.F]::GetForegroundWindow(); $p = 0; [void][S4.F]::GetWindowThreadProcessId($h, [ref]$p)
    if ($p -eq $spikePid) { $ours++ }
    $samples++
    Start-Sleep -Milliseconds 40
}
Receive-Job $job -Wait | Out-Null; Remove-Job $job
"samples=$samples foreground-was-spike=$ours"
& (Join-Path $here 'run.ps1') stop -Port $Port

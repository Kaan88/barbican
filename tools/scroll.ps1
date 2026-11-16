param([int]$x, [int]$y, [int]$ticks)
Add-Type @"
using System; using System.Runtime.InteropServices;
public class S { [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint a,uint b,int d,IntPtr e);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r); [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
public struct R { public int L,T,Ri,B; } }
"@
[S]::SetProcessDPIAware() | Out-Null
$p = Get-Process barbican | ? { $_.MainWindowHandle -ne 0 } | select -First 1
$r = New-Object S+R; [S]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
[S]::SetCursorPos($r.L + $x, $r.T + $y) | Out-Null
for ($i=0; $i -lt [Math]::Abs($ticks); $i++) { [S]::mouse_event(0x800,0,0,[Math]::Sign($ticks)*-120,[IntPtr]::Zero); Start-Sleep -Milliseconds 20 }

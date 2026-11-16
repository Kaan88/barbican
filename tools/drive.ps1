# Drive the running Barbican window for testing.
#   drive.ps1 shot  <out.png>            screenshot of the Barbican window
#   drive.ps1 click <x> <y>              left click at window-relative pixel
#   drive.ps1 dclick <x> <y>             double click
#   drive.ps1 type  <text>               type text (SendKeys syntax)
#   drive.ps1 rect                       print window rect
param([string]$cmd, [string]$a1, [string]$a2, [string]$a3)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
}
"@
[W]::SetProcessDPIAware() | Out-Null
$p = Get-Process barbican -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "barbican is not running"; exit 1 }
$h = $p.MainWindowHandle
$r = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$r) | Out-Null
[W]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 150
function Click($x, $y, $n) {
  [W]::SetCursorPos($r.L + [int]$x, $r.T + [int]$y) | Out-Null
  for ($i = 0; $i -lt $n; $i++) { [W]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [W]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 60 }
}
switch ($cmd) {
  "rect"   { Write-Output "$($r.L) $($r.T) $($r.R - $r.L) $($r.B - $r.T)" }
  "shot"   {
    $w = $r.R - $r.L; $hh = $r.B - $r.T
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $bmp.Save($a1, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "saved $w x $hh"
  }
  "click"  { Click $a1 $a2 1 }
  "dclick" { Click $a1 $a2 2 }
  "type"   { [System.Windows.Forms.SendKeys]::SendWait($a1) }
  "clicktype" { Click $a1 $a2 1; Start-Sleep -Milliseconds 250; [System.Windows.Forms.SendKeys]::SendWait($a3) }
}

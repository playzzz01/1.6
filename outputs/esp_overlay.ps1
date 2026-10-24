Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class EspWin32 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll", EntryPoint="GetWindowLongW")] public static extern int GetWindowLong(IntPtr h, int n);
  [DllImport("user32.dll", EntryPoint="SetWindowLongW")] public static extern int SetWindowLong(IntPtr h, int n, int v);
}
'@

$root = 'D:/CS/Counter Strike/cstrike/addons/amxmodx/configs'
$telemetry = Join-Path $root 'offline_trainer.telemetry'
$control = Join-Path $root 'offline_trainer.ctrl'
$script:state = $null
$script:gameHandle = [IntPtr]::Zero
$script:boxPen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(255, 250, 70, 70), 2)
$script:textFont = New-Object Drawing.Font('Segoe UI', 10, [Drawing.FontStyle]::Bold)
$script:textBrush = New-Object Drawing.SolidBrush([Drawing.Color]::Yellow)
$screen = [Windows.Forms.SystemInformation]::VirtualScreen
$form = New-Object Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.Bounds = New-Object Drawing.Rectangle($screen.Left,$screen.Top,$screen.Width,$screen.Height)
$form.BackColor = [Drawing.Color]::Magenta
$form.TransparencyKey = [Drawing.Color]::Magenta
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.StartPosition = 'Manual'
$form.Add_Shown({
  $style = [EspWin32]::GetWindowLong($form.Handle, -20)
  [void][EspWin32]::SetWindowLong($form.Handle, -20, ($style -bor 0x20 -bor 0x80))
})

function Project($xyz, $camera, $rect, $yawRad, $pitchRad, $scale) {
  $dx = $xyz[0] - $camera[0]; $dy = $xyz[1] - $camera[1]; $dz = $xyz[2] - $camera[2]
  $cy = [Math]::Cos($yawRad); $sy = [Math]::Sin($yawRad)
  $cp = [Math]::Cos($pitchRad); $sp = [Math]::Sin($pitchRad)
  $forward = $dx*$cp*$cy + $dy*$cp*$sy - $dz*$sp
  if ($forward -le 8) { return $null }
  $right = $dx*$sy - $dy*$cy
  $up = -$dx*$sp*$cy - $dy*$sp*$sy + $dz*$cp
  $sx = ($rect.Left + $rect.Right)/2 + $right*$scale/$forward - $screen.Left
  $syScreen = ($rect.Top + $rect.Bottom)/2 - $up*$scale/$forward - $screen.Top
  return [Drawing.PointF]::new([float]$sx, [float]$syScreen)
}

$form.Add_Paint({
  param($sender, $e)
  if (-not $script:state -or $script:gameHandle -eq [IntPtr]::Zero) { return }
  if ([EspWin32]::GetForegroundWindow() -ne $script:gameHandle) { return }
  $r = New-Object EspWin32+RECT
  if (-not [EspWin32]::GetClientRect($script:gameHandle, [ref]$r)) { return }
  $p = New-Object EspWin32+POINT
  if (-not [EspWin32]::ClientToScreen($script:gameHandle, [ref]$p)) { return }
  $r.Right = $p.X + $r.Right; $r.Bottom = $p.Y + $r.Bottom
  $r.Left = $p.X; $r.Top = $p.Y
  $camera = $script:state.camera
  $yaw = $script:state.yaw * [Math]::PI/180
  $pitch = $script:state.pitch * [Math]::PI/180
  $scale = ($r.Right - $r.Left)/2 / [Math]::Tan($script:state.fov*[Math]::PI/360)
  foreach ($enemy in $script:state.enemies) {
    $foot = Project $enemy.xyz $camera $r $yaw $pitch $scale
    $headPos = @($enemy.xyz[0],$enemy.xyz[1],($enemy.xyz[2]+70.0))
    $head = Project $headPos $camera $r $yaw $pitch $scale
    if ($null -eq $foot -or $null -eq $head) { continue }
    $height = [Math]::Abs($foot.Y-$head.Y)
    if ($height -lt 5 -or $height -gt 1200) { continue }
    $width = [Math]::Max(12,$height*0.38)
    $x = [float]($head.X-$width/2); $y=[float][Math]::Min($head.Y,$foot.Y)
    $e.Graphics.DrawRectangle($script:boxPen,$x,$y,[float]$width,[float]$height)
    $e.Graphics.DrawString(('Enemy '+$enemy.id+' HP '+$enemy.hp),$script:textFont,$script:textBrush,$x,[float]($y-20))
  }
})

$timer = New-Object Windows.Forms.Timer
$timer.Interval = 80
$timer.Add_Tick({
  try {
    $game = Get-Process -Name hl -ErrorAction SilentlyContinue | Where-Object MainWindowTitle -eq 'Counter-Strike' | Select-Object -First 1
    if (-not $game) { $script:state = $null; $form.Invalidate(); return }
    $script:gameHandle = $game.MainWindowHandle
    $settings = ([IO.File]::ReadAllText($control).Trim() -split '[ ]+')
    if ($settings.Length -lt 7 -or $settings[3] -ne '1') { $script:state = $null; $form.Invalidate(); return }
    $lines = [IO.File]::ReadAllLines($telemetry)
    if ($lines.Count -lt 1) { return }
    $fields = ($lines[0].Trim() -split '[ ]+')
    if ($fields.Length -lt 12 -or $fields[0] -ne '1') { $script:state = $null; $form.Invalidate(); return }
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $parse = { param($v) [double]::Parse($v,$culture) }
    $camera = @( (& $parse $fields[1]),(& $parse $fields[2]),(& $parse $fields[3]) )
    $enemies = @()
    foreach ($line in $lines | Select-Object -Skip 1) {
      $f = ($line.Trim() -split '[ ]+')
      if ($f.Length -ne 5) { continue }
      $enemies += [pscustomobject]@{ id=$f[0]; xyz=@((& $parse $f[1]),(& $parse $f[2]),(& $parse $f[3])); hp=$f[4] }
    }
    $script:state = @{camera=$camera; pitch=(& $parse $fields[4]); yaw=(& $parse $fields[5]); fov=(& $parse $fields[6]); enemies=$enemies}
    $form.Invalidate()
  } catch { }
})
$form.Show()
$style = [EspWin32]::GetWindowLong($form.Handle, -20)
[void][EspWin32]::SetWindowLong($form.Handle, -20, ($style -bor 0x20 -bor 0x80))
$timer.Start()
[Windows.Forms.Application]::Run($form)

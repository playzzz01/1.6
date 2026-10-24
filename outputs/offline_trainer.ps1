param([switch]$NoGui)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$script:gameDir = 'D:/CS/Counter Strike'
$script:control = Join-Path $gameDir 'cstrike/addons/amxmodx/configs/offline_trainer.ctrl'
$script:seq = [int64][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

function Send-State([int]$reset = 0) {
    $script:seq = [int64][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    if (Test-Path -LiteralPath $script:control) {
        $old = ([IO.File]::ReadAllText($script:control).Trim() -split '[ ]+')
        if ($old.Length -ge 1 -and [int64]$old[0] -ge $script:seq) { $script:seq = [int64]$old[0] }
    }
    $script:seq++
    $h = [int]$health.Checked
    $a = [int]$ammo.Checked
    $g = [int]$glow.Checked
    $aimValue = [int]$aim.Checked
    $min = [int]$minutes.Value
    $record = "$($script:seq) $h $a $g $aimValue $min $reset"
    if (-not (Test-Path -LiteralPath (Split-Path -Parent $script:control))) { throw "Game plugin config directory not found: $script:control" }
    [System.IO.File]::WriteAllText($script:control, $record + [Environment]::NewLine, [System.Text.Encoding]::ASCII)
    $status.Text = "已发送：$record"
}

$form = New-Object Windows.Forms.Form
$form.Text = 'CS 1.6 单机修改器'
$form.Size = New-Object Drawing.Size(380, 360)
$form.StartPosition = 'CenterScreen'
$form.TopMost = $true
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false

function New-Check([string]$text, [int]$y) {
    $c = New-Object Windows.Forms.CheckBox
    $c.Text = $text
    $c.Location = New-Object Drawing.Point(24, $y)
    $c.AutoSize = $true
    $form.Controls.Add($c)
    return $c
}

$health = New-Check '无限生命（玩家）' 20
$ammo = New-Check '无限弹药（玩家）' 56
$glow = New-Check '队伍发光（透墙效果需画面验证）' 92
$aim = New-Check '自动瞄准（视角内最近敌人）' 128
if (Test-Path -LiteralPath $script:control) {
    $parts = ([IO.File]::ReadAllText($script:control).Trim() -split '[ ]+')
    if ($parts.Length -ge 7) {
        $health.Checked = [bool][int]$parts[1]
        $ammo.Checked = [bool][int]$parts[2]
        $glow.Checked = [bool][int]$parts[3]
        $aim.Checked = [bool][int]$parts[4]
    }
}
$label = New-Object Windows.Forms.Label
$label.Text = '回合时间（分钟）：'
$label.Location = New-Object Drawing.Point(24, 170)
$label.AutoSize = $true
$form.Controls.Add($label)
$minutes = New-Object Windows.Forms.NumericUpDown
$minutes.Minimum = 1
$minutes.Maximum = 60
$minutes.Value = 3
if ($parts -and $parts.Length -ge 7 -and [int]$parts[5] -ge 1 -and [int]$parts[5] -le 60) { $minutes.Value = [int]$parts[5] }
$minutes.Location = New-Object Drawing.Point(160, 167)
$form.Controls.Add($minutes)
$apply = New-Object Windows.Forms.Button
$apply.Text = '应用设置'
$apply.Location = New-Object Drawing.Point(25, 212)
$apply.Size = New-Object Drawing.Size(140, 38)
$apply.Add_Click({ Send-State })
$form.Controls.Add($apply)
$restart = New-Object Windows.Forms.Button
$restart.Text = '重置回合'
$restart.Location = New-Object Drawing.Point(183, 212)
$restart.Size = New-Object Drawing.Size(140, 38)
$restart.Add_Click({ Send-State 1; Start-Sleep -Milliseconds 500; Send-State 0 })
$form.Controls.Add($restart)
$status = New-Object Windows.Forms.Label
$status.Text = '启动游戏并加载插件后点击应用设置'
$status.Location = New-Object Drawing.Point(24, 270)
$status.Size = New-Object Drawing.Size(320, 44)
$form.Controls.Add($status)

if ($NoGui) { Send-State; exit }
[void]$form.ShowDialog()

param(
    [string]$OutputDir = "",
    [string]$ExePath = ""
)

if (-not $OutputDir) {
    $OutputDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

if (-not $ExePath) {
    $ExePath = Join-Path $OutputDir "Ink Canvas Ultra.exe"
}

if (-not (Test-Path $ExePath)) {
    Write-Host "Error: Exe not found at $ExePath"
    exit 1
}

$WshShell = New-Object -ComObject WScript.Shell

$ShortcutPath = Join-Path $OutputDir "视频展台.lnk"
$TempShortcutPath = Join-Path $OutputDir "_vp_temp.lnk"

if (Test-Path $ShortcutPath) {
    Remove-Item $ShortcutPath -Force
}
if (Test-Path $TempShortcutPath) {
    Remove-Item $TempShortcutPath -Force
}

$Shortcut = $WshShell.CreateShortcut($TempShortcutPath)
# 使用相对路径，使快捷方式在任意位置都能正确找到同目录下的 exe
$Shortcut.TargetPath = "Ink Canvas Ultra.exe"
$Shortcut.Arguments = "--video-presenter"
$Shortcut.WorkingDirectory = ""
$Shortcut.Description = "视频展台模式"
$Shortcut.IconLocation = "Ink Canvas Ultra.exe,0"
$Shortcut.Save()

[System.IO.File]::Move($TempShortcutPath, $ShortcutPath)

Write-Host "Shortcut created: $ShortcutPath"
Write-Host "  Target: Ink Canvas Ultra.exe (relative, same folder as shortcut)"
Write-Host "  Arguments: --video-presenter"
Write-Host "  WorkingDir: (relative, same folder as shortcut)"
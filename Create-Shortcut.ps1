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
$Shortcut.TargetPath = $ExePath
$Shortcut.Arguments = "--video-presenter"
$Shortcut.WorkingDirectory = $OutputDir
$Shortcut.Description = "视频展台模式"
$Shortcut.IconLocation = "$ExePath,0"
$Shortcut.Save()

[System.IO.File]::Move($TempShortcutPath, $ShortcutPath)

Write-Host "Shortcut created: $ShortcutPath"
Write-Host "  Target: $ExePath"
Write-Host "  Arguments: --video-presenter"
Write-Host "  WorkingDir: $OutputDir"
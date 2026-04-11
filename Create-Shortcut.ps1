# 创建视频展台快捷方式的 PowerShell 脚本
param(
    [string]$ExePath,
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

if (-not $ExePath) {
    $ExePath = Join-Path $PSScriptRoot "Ink Canvas\bin\Release\Ink Canvas Ultra.exe"
}

if (-not $OutputDir) {
    $OutputDir = Join-Path $PSScriptRoot "Ink Canvas\bin\Release"
}

Write-Host "ExePath: $ExePath"
Write-Host "OutputDir: $OutputDir"

if (-not (Test-Path $ExePath)) {
    Write-Error "Executable not found: $ExePath"
    exit 1
}

$WshShell = New-Object -ComObject WScript.Shell
$ShortcutPath = Join-Path $OutputDir "视频展台.lnk"

Write-Host "Creating shortcut at: $ShortcutPath"

$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = $ExePath
$Shortcut.Arguments = "--whiteboard-camera"
$Shortcut.WorkingDirectory = Split-Path -Parent $ExePath
$Shortcut.Description = "视频展台模式"
$Shortcut.IconLocation = $ExePath
$Shortcut.Save()

Write-Host "Shortcut created successfully!"

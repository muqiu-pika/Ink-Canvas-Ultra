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
# WScript.Shell COM 对象的 Save() 内部使用 ANSI API 写入文件路径，
# 在非中文 locale 系统（如 GitHub Actions windows-2022 runner）上会把中文字符转成 "?" 导致保存失败。
# 规避方案：先保存为纯 ASCII 临时文件名，再用 PowerShell 的 Move-Item（基于 .NET Unicode API）重命名。
$TempShortcutPath = Join-Path $OutputDir "_VideoPresenter_Temp.lnk"

Write-Host "Creating shortcut at: $ShortcutPath"

$Shortcut = $WshShell.CreateShortcut($TempShortcutPath)
$Shortcut.TargetPath = $ExePath
$Shortcut.Arguments = "--whiteboard-camera"
$Shortcut.WorkingDirectory = Split-Path -Parent $ExePath
$Shortcut.Description = "视频展台模式"
$Shortcut.IconLocation = $ExePath
$Shortcut.Save()

# 重命名为正确的中文文件名（.NET / PowerShell 的 Move-Item 使用 Unicode API，不受系统 ANSI 代码页限制）
if (Test-Path $TempShortcutPath) {
    if (Test-Path $ShortcutPath) {
        Remove-Item $ShortcutPath -Force
    }
    Move-Item -Path $TempShortcutPath -Destination $ShortcutPath -Force
} else {
    Write-Error "Temporary shortcut file was not created: $TempShortcutPath"
    exit 1
}

Write-Host "Shortcut created successfully!"

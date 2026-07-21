# visualpresenter plugin 构建脚本
#
# 用法：
#   pwsh -File build.ps1
#   或在 PowerShell 7+ 中直接运行：
#   .\build.ps1
#
# 说明：
#   - 调用 dotnet build 编译 VisualPresenterPlugin.csproj
#   - 编译产物（VisualPresenterPlugin.dll）会被 csproj 的 AfterBuild target
#     自动复制到主程序 bin\Debug\Plugins\visualpresenter\ 目录
#   - 同时把 plugin.icplugin 与 icon.png 也复制过去
#   - 构建完成后即可启动主程序调试 plugin

param(
    [string]$Configuration = "Debug",
    [string]$HostExePath
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==> 构建 visualpresenter plugin ($Configuration)" -ForegroundColor Cyan

# 1) 编译 plugin 程序集
Push-Location $here
try {
    & dotnet build VisualPresenterPlugin.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build 失败 (exit $LASTEXITCODE)"
    }
}
finally {
    Pop-Location
}

Write-Host "==> plugin 构建完成" -ForegroundColor Green

# 2) 可选：直接启动主程序调试
if ($HostExePath -and (Test-Path $HostExePath)) {
    Write-Host "==> 启动主程序: $HostExePath" -ForegroundColor Cyan
    Start-Process -FilePath $HostExePath
}

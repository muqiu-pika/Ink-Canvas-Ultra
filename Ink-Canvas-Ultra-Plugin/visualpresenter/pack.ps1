# visualpresenter plugin pack script
#
# Usage:
#   pwsh -File pack.ps1
#   or run directly in PowerShell:
#   .\pack.ps1
#
# Description:
#   - Calls dotnet build to compile VisualPresenterPlugin.csproj (produces DLL)
#   - Packs plugin.icplugin + DLL + resources into visualpresenter.icplugin (ZIP)
#   - Output file is at parent directory Ink-Canvas-Ultra-Plugin\visualpresenter.icplugin
#   - The .icplugin file can be installed via Plugin Workshop -> Install from Local

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Resolve script directory (compatible with Windows PowerShell 5.1 and PowerShell 7+)
$scriptPath = $MyInvocation.MyCommand.Path
if (-not $scriptPath) { $scriptPath = $PSCommandPath }
$script:here = Split-Path -Parent $scriptPath
$script:pluginRoot = Split-Path -Parent $script:here
$script:outputFile = Join-Path $script:pluginRoot "visualpresenter.icplugin"

Write-Host "==> Script dir: $script:here" -ForegroundColor DarkGray
Write-Host "==> Output file: $script:outputFile" -ForegroundColor DarkGray
Write-Host "==> Build visualpresenter plugin ($Configuration)" -ForegroundColor Cyan

# 1) Build plugin assembly (use absolute path to avoid working directory issues)
$csprojPath = Join-Path $script:here "VisualPresenterPlugin.csproj"
Write-Host "    csproj: $csprojPath"
$buildLog = Join-Path ([System.IO.Path]::GetTempPath()) ("icplugin-build-" + [System.Guid]::NewGuid().ToString("N") + ".log")
$buildErrLog = "$buildLog.err"
$buildProc = Start-Process -FilePath "dotnet" -ArgumentList @("build", "`"$csprojPath`"", "-c", $Configuration) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $buildLog -RedirectStandardError $buildErrLog
Get-Content $buildLog -ErrorAction SilentlyContinue | Write-Host
Get-Content $buildErrLog -ErrorAction SilentlyContinue | Write-Host
if ($buildProc.ExitCode -ne 0) {
    throw "dotnet build failed (exit $($buildProc.ExitCode))"
}
Remove-Item $buildLog, $buildErrLog -ErrorAction SilentlyContinue

# 2) Collect files to stage directory
$tempBase = [System.IO.Path]::GetTempPath()
$stagingDir = Join-Path $tempBase ("icplugin-staging-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
Write-Host "    Stage dir: $stagingDir" -ForegroundColor DarkGray

try {
    # Copy manifest
    $manifestPath = Join-Path $script:here "plugin.icplugin"
    Write-Host "    manifest: $manifestPath" -ForegroundColor DarkGray
    Copy-Item $manifestPath -Destination $stagingDir -Force

    # Copy icon if it exists
    $iconPath = Join-Path $script:here "icon.png"
    Write-Host "    icon path: $iconPath" -ForegroundColor DarkGray
    if ($iconPath -and (Test-Path $iconPath)) {
        Copy-Item $iconPath -Destination $stagingDir -Force
    }

    # Copy build output (DLL)
    # csproj has OutputPath=bin\, AppendTargetFrameworkToOutputPath=false,
    # so output goes directly under bin\ regardless of Configuration
    $binDir = Join-Path $script:here "bin"
    Write-Host "    bin dir: $binDir" -ForegroundColor DarkGray
    if (Test-Path $binDir) {
        Get-ChildItem $binDir -Filter "*.dll" | ForEach-Object {
            Copy-Item $_.FullName -Destination $stagingDir -Force
        }
        Get-ChildItem $binDir -Filter "*.pdb" | ForEach-Object {
            Copy-Item $_.FullName -Destination $stagingDir -Force
        }
    } else {
        Write-Warning "Build output directory not found: $binDir (will only pack manifest)"
    }

    # 3) Pack into .icplugin (ZIP)
    if (Test-Path $script:outputFile) {
        Remove-Item $script:outputFile -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $script:outputFile)

    Write-Host ""
    Write-Host "==> Pack complete: $script:outputFile" -ForegroundColor Green
    Write-Host "    Contents:" -ForegroundColor Gray
    Get-ChildItem $stagingDir | ForEach-Object {
        Write-Host "      $($_.Name)" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "Install via Plugin Workshop -> Install from Local -> select visualpresenter.icplugin" -ForegroundColor Cyan
}
finally {
    # Clean up stage directory
    if ($stagingDir -and (Test-Path $stagingDir)) {
        Remove-Item $stagingDir -Recurse -Force
    }
}

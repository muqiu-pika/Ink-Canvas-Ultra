#Requires -Version 5.1
<#
.SYNOPSIS
    Package DocumentToImagePlugin as an .icplugin installer.
.DESCRIPTION
    Collects the plugin DLL, manifest and pdfium native dependency into a
    single .icplugin archive. The archive can be placed into ICU's Plugins
    folder or installed through the Plugin Workshop.
.PARAMETER Configuration
    Build configuration to package. Default: Release.
.PARAMETER OutputDir
    Output directory for the package. Default: ..\..\Releases\Plugins.
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "..\..\Releases\Plugins"
)

$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$BuildDir = Join-Path $ProjectDir "..\..\Ink Canvas\Plugins\DocumentToImagePlugin" | Resolve-Path
if (![System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = [System.IO.Path]::GetFullPath((Join-Path $ProjectDir $OutputDir))
}
$PluginName = "DocumentToImagePlugin"
$PackageFile = Join-Path $OutputDir "$PluginName-$Configuration.icplugin"

function New-TemporaryDirectory {
    $temp = Join-Path $env:TEMP ([Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $temp | Out-Null
    return $temp
}

if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

if (!(Test-Path $BuildDir)) {
    throw "Build directory not found: $BuildDir. Compile the project first."
}

$required = @(
    "plugin.icplugin",
    "DocumentToImagePlugin.dll",
    "PdfiumViewer.dll",
    "NPOI.dll",
    "NPOI.OOXML.dll",
    "NPOI.OpenXml4Net.dll",
    "NPOI.OpenXmlFormats.dll",
    "BouncyCastle.Crypto.dll",
    "ICSharpCode.SharpZipLib.dll",
    "x86\pdfium.dll"
)

foreach ($file in $required) {
    $fullPath = Join-Path $BuildDir $file
    if (!(Test-Path $fullPath)) {
        throw "Missing required file: $fullPath"
    }
}

$stage = New-TemporaryDirectory
try {
    Copy-Item (Join-Path $BuildDir "plugin.icplugin") $stage
    Copy-Item (Join-Path $BuildDir "DocumentToImagePlugin.dll") $stage
    Copy-Item (Join-Path $BuildDir "PdfiumViewer.dll") $stage
    Copy-Item (Join-Path $BuildDir "x86\pdfium.dll") (Join-Path $stage "pdfium.dll")

    $npoiFiles = @(
        "NPOI.dll",
        "NPOI.OOXML.dll",
        "NPOI.OpenXml4Net.dll",
        "NPOI.OpenXmlFormats.dll",
        "BouncyCastle.Crypto.dll",
        "ICSharpCode.SharpZipLib.dll"
    )
    foreach ($file in $npoiFiles) {
        Copy-Item (Join-Path $BuildDir $file) $stage
    }

    $optional = @(
        "ReachFramework.dll",
        "PresentationFramework.dll",
        "PresentationCore.dll",
        "WindowsBase.dll"
    )
    foreach ($file in $optional) {
        $src = Join-Path $BuildDir $file
        if (Test-Path $src) {
            Copy-Item $src $stage
        }
    }

    if (Test-Path $PackageFile) {
        Remove-Item $PackageFile -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $PackageFile, "Optimal", $false)

    Write-Host "Package created: $PackageFile" -ForegroundColor Green
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

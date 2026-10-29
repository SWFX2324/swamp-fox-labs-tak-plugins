<#
.SYNOPSIS
    Builds the OpenMANET WinTAK plugin and packages it as a .wpk.
.EXAMPLE
    .\build.ps1
    .\build.ps1 -WinTakDir "D:\WinTAK"
#>
param(
    [string]$Configuration = "Release",
    # Releases build against the oldest supported WinTAK, kept unpacked under C:\WinTAK-SDK; otherwise the installed one.
    [string]$WinTakDir = $(if (Test-Path "C:\WinTAK-SDK\5.7.0.149\WinTak.Common.dll") { "C:\WinTAK-SDK\5.7.0.149" } else { "C:\Program Files\WinTAK" })
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$src = Join-Path $root "src"
$dist = Join-Path $root "dist"

Write-Host "Building against the WinTAK SDK in $WinTakDir"
dotnet build (Join-Path $src "WinTAK_OpenMANET_Plugin.csproj") -c $Configuration "-p:WinTakDir=$WinTakDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# Versions come from AssemblyInfo.cs so there is a single source of truth.
$assemblyInfo = Get-Content (Join-Path $src "Properties\AssemblyInfo.cs") -Raw
$version = [regex]::Match($assemblyInfo, 'AssemblyVersion\("([^"]+)"\)').Groups[1].Value
$sdkVersion = [regex]::Match($assemblyInfo, 'TakSdkVersion\("([^"]+)"\)').Groups[1].Value
if (-not $version -or -not $sdkVersion) { throw "Could not read versions from AssemblyInfo.cs." }

[xml]$manifest = Get-Content (Join-Path $root "package\manifest.xml")
$manifest.wintakPluginPackage.metadata.version = $version
$manifest.wintakPluginPackage.metadata.sdkVersion = $sdkVersion

New-Item -ItemType Directory -Force $dist | Out-Null
$wpk = Join-Path $dist "OpenMANET-$version-$sdkVersion.wpk"
if (Test-Path $wpk) { Remove-Item $wpk -Confirm:$false }

# Build the zip by hand: Compress-Archive on Windows PowerShell 5.1 writes
# backslash entry names, which other zip readers don't treat as folders.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($wpk, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $entry = $zip.CreateEntry("manifest.xml")
    $stream = $entry.Open()
    try {
        $writer = New-Object System.IO.StreamWriter($stream, (New-Object System.Text.UTF8Encoding($true)))
        $manifest.Save($writer)
        $writer.Flush()
    } finally { $stream.Dispose() }

    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root "package\icon.png"), "icon.png") | Out-Null
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $src "bin\$Configuration\WinTAK_OpenMANET_Plugin.dll"), "plugin/x64/WinTAK_OpenMANET_Plugin.dll") | Out-Null
} finally { $zip.Dispose() }

Write-Host "Packaged $wpk"

<#
.SYNOPSIS
    Runs the OpenMANET plugin's offline tests against a built DLL.
.DESCRIPTION
    The tests load the plugin and WinTAK's SDK assemblies into PowerShell and exercise them
    without WinTAK running or a mesh attached; a small fake OpenMANET server stands in for a
    node where needed. Build first with ..\build.ps1. Exits 1 if any check fails.
.EXAMPLE
    .\tests\Run-Tests.ps1
#>
param(
    [string]$Dll = (Join-Path $PSScriptRoot "..\src\bin\Release\WinTAK_OpenMANET_Plugin.dll"),
    # Same SDK as build.ps1: the oldest supported WinTAK when it's unpacked under C:\WinTAK-SDK.
    [string]$WinTakDir = $(if (Test-Path "C:\WinTAK-SDK\5.7.0.149\WinTak.Common.dll") { "C:\WinTAK-SDK\5.7.0.149" } else { "C:\Program Files\WinTAK" })
)

$Dll = (Resolve-Path $Dll).Path
$passed = 0
$failed = 0
foreach ($test in Get-ChildItem $PSScriptRoot -Filter "Test-*.ps1" | Sort-Object Name) {
    Write-Host ""
    Write-Host "== $($test.BaseName)"
    # Each file runs in its own process: types added with Add-Type can't be unloaded or redefined.
    $output = & powershell.exe -NoProfile -NonInteractive -STA -File $test.FullName -Dll $Dll -WinTakDir $WinTakDir 2>&1
    $exitCode = $LASTEXITCODE
    foreach ($line in $output) {
        Write-Host "$line"
        if ("$line" -match "\sPASS$") { $passed++ } elseif ("$line" -match "\sFAIL$") { $failed++ }
    }
    if ($exitCode -ne 0) {
        Write-Host "   $($test.Name) stopped with an error (exit code $exitCode)"
        $failed++
    }
}
Write-Host ""
Write-Host "$passed passed, $failed failed"
exit $(if ($failed -gt 0) { 1 } else { 0 })

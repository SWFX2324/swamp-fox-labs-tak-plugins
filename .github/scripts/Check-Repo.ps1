<#
.SYNOPSIS
    Checks that need no WinTAK SDK: nothing that shouldn't be public is committed, and every
    PowerShell script parses and is saved so Windows PowerShell reads it correctly. Run from the
    repository root. Exits 1 if anything fails.
#>
$ErrorActionPreference = "Stop"
$problems = New-Object System.Collections.ArrayList
$tracked = @(& git -c core.quotepath=off ls-files)
if ($LASTEXITCODE -ne 0) { throw "git ls-files failed." }

# WinTAK's DLLs are licensed separately, packages belong on the Releases page, and keys never belong here.
$forbidden = '\.(dll|exe|msi|msix|pdb|wpk|apk|aab|p12|pfx|snk|keystore|jks)$'
foreach ($file in $tracked) {
    if ($file -match $forbidden) { [void]$problems.Add("${file}: this file type must not be committed") }
}

$scripts = @($tracked | Where-Object { $_ -match '\.ps1$' })
foreach ($file in $scripts) {
    $parseErrors = $null
    $tokens = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $file).Path, [ref]$tokens, [ref]$parseErrors)
    foreach ($parseError in $parseErrors) {
        [void]$problems.Add("${file}:$($parseError.Extent.StartLineNumber): $($parseError.Message)")
    }
    # Without a byte-order mark, Windows PowerShell reads non-ASCII characters in the wrong encoding.
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $file).Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    if (-not $hasBom -and @($bytes | Where-Object { $_ -gt 127 }).Count -gt 0) {
        [void]$problems.Add("${file}: has non-ASCII characters but no UTF-8 byte-order mark")
    }
}

Write-Host "Checked $($tracked.Count) committed files and $($scripts.Count) PowerShell scripts."
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "FAIL $_" }
    exit 1
}
Write-Host "All good."

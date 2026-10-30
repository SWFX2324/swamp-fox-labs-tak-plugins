# Mesh utilization is judged on throughput averaged over 30 s, so bursty traffic doesn't make its alerts flap (2.2.2).
param([Parameter(Mandatory = $true)][string]$Dll, [string]$WinTakDir = "C:\Program Files\WinTAK")
. (Join-Path $PSScriptRoot "Common.ps1") -Dll $Dll -WinTakDir $WinTakDir

$tmp = New-TempDir
$plugin = Use-TempPluginSettings $tmp

$ctlType = $asm.GetType($ns + "OpenMANET"); $ctl = [Activator]::CreateInstance($ctlType)
function F($name) { return ,($ctlType.GetField($name, $any).GetValue($ctl)) }
function SetF($name, $v) { $ctlType.GetField($name, $any).SetValue($ctl, $v) }
$sampleType = $ctlType.GetNestedType("ThroughputSample", [System.Reflection.BindingFlags]::NonPublic)
$samples = F "_throughputSamples"
$start = [DateTimeOffset]::UtcNow
$script:t = 0
function Sample([double]$mbps) {
    $script:t += 5
    $s = [Activator]::CreateInstance($sampleType, $true)
    $sampleType.GetProperty("CapturedAtUtc").SetValue($s, $start.AddSeconds($script:t))
    $sampleType.GetProperty("RxMegabitsPerSecond").SetValue($s, $mbps)
    $sampleType.GetProperty("TxMegabitsPerSecond").SetValue($s, [double]0)
    [void]$samples.Add($s)
    while ($samples.Count -gt 0 -and $sampleType.GetProperty("CapturedAtUtc").GetValue($samples[0]) -lt $start.AddSeconds($script:t - 60)) { $samples.RemoveAt(0) }
    SetF "_currentThroughputMbps" ([Nullable[double]]$mbps)
    $ctlType.GetMethod("UpdateUtilization", $any).Invoke($ctl, @())
}
function UtilizationEvents { @(F "_meshEvents" | Where-Object { $_.Message -like "Mesh utilization*" }).Count }

SetF "_isConnected" $true
SetF "_currentBandwidthMbps" ([Nullable[double]]32.2)
# What Node0's mesh did: single 5 s samples swinging between 52% and 62% of 32.2 Mbps.
foreach ($mbps in 19.6, 16.8, 20.0, 16.7, 19.5, 17.0, 20.1, 16.9, 19.8, 16.6, 19.9, 17.1) { Sample $mbps }
$percent = (F "_currentUtilizationPercent")
Check "Bursty traffic: settles instead of flapping (at most one change)" ((UtilizationEvents) -le 1)
Check "Utilization is the 30 s average, not the last sample" ([Math]::Abs($percent - 56.6) -lt 0.5)

SetF "_currentBandwidthMbps" ([Nullable[double]]10.0)
foreach ($i in 1..7) { Sample 9.0 }
Check "Sustained load still raises the alert" ((F "_utilizationState") -eq "Critical")
foreach ($i in 1..7) { Sample 2.0 }
Check "And it clears once the whole window is quiet" ((F "_utilizationState") -eq "Normal")

Remove-Item $tmp -Recurse -Force

# GPS quality on the connected node and the "WinTAK is using another node's GPS" warning (2.2.2).
param([Parameter(Mandatory = $true)][string]$Dll, [string]$WinTakDir = "C:\Program Files\WinTAK")
. (Join-Path $PSScriptRoot "Common.ps1") -Dll $Dll -WinTakDir $WinTakDir

$tmp = New-TempDir
$plugin = Use-TempPluginSettings $tmp

Add-Type -ReferencedAssemblies "System.Xml" -TypeDefinition @"
using System.Xml;
public static class GpsSelfPosition {
  public static XmlDocument Doc;
  public static XmlDocument Get() { return Doc; }
}
"@
$opsType = $asm.GetType($ns + "OpenMANET_Operators")
$opsType.GetMethod("SetSelfPositionProvider").Invoke($null, @([Func[System.Xml.XmlDocument]][GpsSelfPosition].GetMethod("Get").CreateDelegate([Func[System.Xml.XmlDocument]]))) | Out-Null
function Set-WinTakPosition([double]$lat, [double]$lon) {
    $now = [DateTime]::UtcNow; $d = New-Object System.Xml.XmlDocument
    $d.LoadXml(("<event version=""2.0"" uid=""S-1-5-21-SELF"" type=""a-f-G-U-C-I"" how=""m-g"" time=""{0}"" start=""{0}"" stale=""{1}""><point lat=""{2}"" lon=""{3}"" hae=""500"" ce=""9"" le=""9""/><detail/></event>" -f $now.ToString("o"), $now.AddMinutes(1).ToString("o"), $lat.ToString([Globalization.CultureInfo]::InvariantCulture), $lon.ToString([Globalization.CultureInfo]::InvariantCulture)))
    [GpsSelfPosition]::Doc = $d
}
$metersPerDegree = 111195.0
$lat0 = 34.2457; $lon0 = -116.0890      # Node0
$lat1 = 34.2445; $lon1 = -116.0905      # Node1, about 190 m away

# --- Parsing the connected node's GNSS status
$clientType = $asm.GetType($ns + "OpenMANET_Client")
$gnssType = $clientType.GetNestedType("GnssStatusResponse", [System.Reflection.BindingFlags]::NonPublic)
$deser = $clientType.GetMethod("DeserializeJson", $any)
$g = $deser.MakeGenericMethod($gnssType).Invoke($null, @(,[Text.Encoding]::UTF8.GetBytes('{"position":{"fixType":"FIX_TYPE_3D","latitude":34.2457,"longitude":-116.089,"altitude":530,"pdop":6.1,"hdop":5.0},"satelliteStatus":{"satellitesUsed":5,"satellitesInView":11}}')))
Check "GNSS: HDOP and satellite counts parsed" ($g.Position.Hdop -eq 5.0 -and $g.SatelliteStatus.SatellitesUsed -eq 5 -and $g.SatelliteStatus.SatellitesInView -eq 11)

$topoType = $clientType.GetNestedType("MeshTopology", [System.Reflection.BindingFlags]::NonPublic)
$selfTopo = $deser.MakeGenericMethod($topoType).Invoke($null, @(,[Text.Encoding]::UTF8.GetBytes('{"selfMac":"02:00:5e:10:00:00","nodes":[{"mac":"02:00:5e:10:00:00","isSelf":true,"hopsFromSelf":0}]}')))
$client = [Activator]::CreateInstance($clientType, $true)
$clientType.GetField("_selfPosition", $any).SetValue($client, $g.Position)
$clientType.GetField("_selfSatellites", $any).SetValue($client, $g.SatelliteStatus)
$self = $clientType.GetMethod("CreateSelfNode", $any).Invoke($client, @($selfTopo, (New-NodeList), [DateTimeOffset]::UtcNow, 1))
Check "Connected node carries its fix quality" ($self.GpsHdop -eq 5.0 -and $self.GpsSatellitesUsed -eq 5 -and $self.GpsSatellitesInView -eq 11)
Check "HDOP 5.0 is a poor fix, shown in node details" ($self.HasPoorGpsFix -and $self.GpsQualityDisplay -eq "Poor $([char]0xB7) HDOP 5.0 $([char]0xB7) 5 of 11 satellites")

function GpsNode([hashtable]$p) { New-Node $p }
$good = GpsNode @{ Name = "N"; IsSelf = $true; Latitude = [Nullable[double]]$lat0; Longitude = [Nullable[double]]$lon0; GpsHdop = [Nullable[double]]1.2; GpsSatellitesUsed = [Nullable[int]]9; GpsSatellitesInView = [Nullable[int]]12 }
$moderate = GpsNode @{ Name = "N"; IsSelf = $true; Latitude = [Nullable[double]]$lat0; Longitude = [Nullable[double]]$lon0; GpsHdop = [Nullable[double]]3.4 }
$nofix = GpsNode @{ Name = "N"; IsSelf = $true; GpsSatellitesInView = [Nullable[int]]3 }
$other = GpsNode @{ Name = "N"; Latitude = [Nullable[double]]$lat0; Longitude = [Nullable[double]]$lon0 }
Check "Quality labels: good, moderate, no fix" ($good.GpsQualityDisplay -eq "Good $([char]0xB7) HDOP 1.2 $([char]0xB7) 9 of 12 satellites" -and -not $good.HasPoorGpsFix -and $moderate.GpsQualityDisplay -eq "Moderate $([char]0xB7) HDOP 3.4" -and $nofix.GpsQualityDisplay -eq "No fix $([char]0xB7) 3 satellites in view")
$zeroCounts = GpsNode @{ Name = "Node0"; IsSelf = $true; Latitude = [Nullable[double]]$lat0; Longitude = [Nullable[double]]$lon0; GpsHdop = [Nullable[double]]11.9; GpsSatellitesUsed = [Nullable[int]]0; GpsSatellitesInView = [Nullable[int]]0 }
Check "0 of 0 satellites alongside a fix reads as not reported (Node0 firmware)" ($zeroCounts.GpsQualityDisplay -eq "Poor $([char]0xB7) HDOP 11.9")
Check "Other nodes: quality isn't reported over the mesh" ($other.GpsQualityDisplay -eq "Reported for the connected node only" -and -not $other.HasPoorGpsFix)

# --- Which node's GPS is WinTAK using?
$checkType = $asm.GetType($ns + "OpenMANET_GpsCheck")
function Mesh([bool]$connectedHasFix) {
    $list = New-NodeList
    if ($connectedHasFix) { $list.Add((New-Node @{ Name = "Node1"; MacAddress = "02:00:5E:10:00:01"; IsSelf = $true; Latitude = [Nullable[double]]$lat1; Longitude = [Nullable[double]]$lon1 })) }
    else { $list.Add((New-Node @{ Name = "Node1"; MacAddress = "02:00:5E:10:00:01"; IsSelf = $true })) }
    $list.Add((New-Node @{ Name = "Node0"; MacAddress = "02:00:5E:10:00:00"; Latitude = [Nullable[double]]$lat0; Longitude = [Nullable[double]]$lon0 }))
    return ,$list
}
function Borrowed($nodes) { $checkType.GetMethod("FindBorrowedGpsSource").Invoke($null, @($nodes, $opsType.GetMethod("Self").Invoke($null, @()))) }

Set-WinTakPosition ($lat0 + 8 / $metersPerDegree) $lon0
Check "Field case: Node1 has no fix and WinTAK sits on Node0's GPS" ((Borrowed (Mesh $false)).Name -eq "Node0")
Check "Connected node has a fix far from WinTAK, which matches Node0" ((Borrowed (Mesh $true)).Name -eq "Node0")
Set-WinTakPosition ($lat1 + 5 / $metersPerDegree) $lon1
Check "WinTAK near its own node's fix: no warning" ($null -eq (Borrowed (Mesh $true)))
Set-WinTakPosition ($lat0 + 60 / $metersPerDegree) $lon0
Check "WinTAK not at any node: no warning" ($null -eq (Borrowed (Mesh $false)))
[GpsSelfPosition]::Doc = $null
Check "WinTAK without a position: no warning" ($null -eq (Borrowed (Mesh $false)))

# --- The pane: reported only after it holds, then in health findings and the event log
$ctlType = $asm.GetType($ns + "OpenMANET"); $ctl = [Activator]::CreateInstance($ctlType)
function F($name) { return ,($ctlType.GetField($name, $any).GetValue($ctl)) }
function SetF($name, $v) { $ctlType.GetField($name, $any).SetValue($ctl, $v) }
function M($name, [object[]]$a) { $ctlType.GetMethod($name, $any).Invoke($ctl, $a) }
SetF "_isConnected" $true
$events = F "_meshEvents"
Set-WinTakPosition ($lat0 + 8 / $metersPerDegree) $lon0
$mesh = Mesh $false; SetF "_lastNodes" $mesh
M "UpdateGpsChecks" @(, $mesh); M "UpdateGpsChecks" @(, $mesh)
Check "Pane: not reported before it holds for 3 polls" ($null -eq (F "_borrowedGpsSource") -and -not (@($events) | Where-Object { $_.Message -like "WinTAK's position matches*" }))
M "UpdateGpsChecks" @(, $mesh)
M "UpdateHealthFindings" @()
$findings = @(F "_healthFindings" | ForEach-Object { $_.Message })
Check "Pane: after 3 polls, logged once and listed in health findings" (@($events | Where-Object { $_.Message -like "WinTAK's position matches Node0's GPS, not Node1's (Node1 has no fix)*" }).Count -eq 1 -and @($findings | Where-Object { $_ -like "WinTAK's position matches Node0's GPS*" }).Count -eq 1)
Check "Pane: GPS findings don't change mesh health" ((F "HealthSummaryText").Text -ne "Offline" -and @($findings | Where-Object { $_ -like "*GPS*" }).Count -ge 1)
M "UpdateGpsChecks" @(, $mesh)
Check "Pane: not logged again while it continues" (@($events | Where-Object { $_.Message -like "WinTAK's position matches*" }).Count -eq 1)
Set-WinTakPosition ($lat0 + 60 / $metersPerDegree) $lon0
M "UpdateGpsChecks" @(, $mesh); M "UpdateHealthFindings" @()
Check "Pane: clears as soon as WinTAK's position stops matching" ($null -eq (F "_borrowedGpsSource") -and -not (@(F "_healthFindings" | ForEach-Object { $_.Message }) | Where-Object { $_ -like "WinTAK's position matches*" }))

$poorMesh = New-NodeList; $poorMesh.Add((New-Node @{ Name = "Node0"; IsSelf = $true; Latitude = [Nullable[double]]$lat0; Longitude = [Nullable[double]]$lon0; GpsHdop = [Nullable[double]]5.0; GpsSatellitesUsed = [Nullable[int]]5; GpsSatellitesInView = [Nullable[int]]11 }))
SetF "_lastNodes" $poorMesh
M "UpdateGpsChecks" @(, $poorMesh); M "UpdateGpsChecks" @(, $poorMesh); M "UpdateHealthFindings" @()
$poorText = "Node0's GPS fix is poor (HDOP 5.0 $([char]0xB7) 5 of 11 satellites)*"
Check "Pane: poor fix logged once and listed in health findings" (@($events | Where-Object { $_.Message -like $poorText }).Count -eq 1 -and @(F "_healthFindings" | Where-Object { $_.Message -like $poorText }).Count -eq 1)

Remove-Item $tmp -Recurse -Force

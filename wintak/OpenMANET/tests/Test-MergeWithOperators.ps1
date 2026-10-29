# One icon per place: a node with a TAK user standing at it is drawn as that user (2.1.3.0).
param([Parameter(Mandatory = $true)][string]$Dll, [string]$WinTakDir = "C:\Program Files\WinTAK")
. (Join-Path $PSScriptRoot "Common.ps1") -Dll $Dll -WinTakDir $WinTakDir

$tmp = New-TempDir
$plugin = Use-TempPluginSettings $tmp

$refs = @((Join-Path $wt "WinTak.CursorOnTarget.dll"), (Join-Path $wt "WinTak.Common.dll"), (Join-Path $wt "WinTak.Graphics.dll"), (Join-Path $wt "WinTak.Framework.dll"), "System.Xml", "WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml")
Add-Type -ReferencedAssemblies $refs -TypeDefinition @"
using System; using System.Collections.Generic; using System.Threading.Tasks; using System.Xml;
using WinTak.CursorOnTarget.Services; using WinTak.Common.CoT; using WinTak.Graphics;
public class RecordingCotSender : ICotMessageSender {
  public List<XmlDocument> Sent = new List<XmlDocument>();
  public void Process(CotEvent m) {} public Task<bool> ProcessAsync(CotEvent m) { return Task.FromResult(true); }
  public void Process(CoTMessageArgument a) {} public Task<bool> ProcessAsync(CoTMessageArgument a) { return Task.FromResult(true); }
  public void Send(XmlDocument m) {} public void Send(CoTMessageArgument a) {}
  public void ProcessInternal(CotEvent m) {} public void ProcessInternal(MapItem i) {}
  public Task<bool> ProcessInternalAsync(CotEvent m) { return Task.FromResult(true); }
  public void SendInternal(XmlDocument m) { Sent.Add(m); }
  public Task<bool> ProcessInternalAsync(XmlDocument m) { Sent.Add(m); return Task.FromResult(true); }
}
public static class SelfPosition {
  public static XmlDocument Doc;
  public static XmlDocument Get() { return Doc; }
}
"@
$fake = New-Object RecordingCotSender
$hostType.GetProperty("CotSender", $any).SetValue($null, $fake)
$preview = $hostType.GetMethod("CotReceiver_PreviewMessageReceived", $any)
$opsType = $asm.GetType($ns + "OpenMANET_Operators")
$layerType = $asm.GetType($ns + "OpenMANET_MapLayer"); $layer = [Activator]::CreateInstance($layerType, $true)
$opsType.GetMethod("SetSelfPositionProvider").Invoke($null, @([Func[System.Xml.XmlDocument]][SelfPosition].GetMethod("Get").CreateDelegate([Func[System.Xml.XmlDocument]]))) | Out-Null

$metersPerDegree = 111195.0
$lat0 = 34.2457; $lon0 = -116.0890      # Node0
$lat1 = 34.2445; $lon1 = -116.0905      # Node1, about 190 m away
function North([double]$meters) { $lat0 + $meters / $metersPerDegree }

# A TAK client's position report, as WinTAK hands it to plugins.
function New-Sa($uid, [double]$lat, [double]$lon, [int]$staleMinutes = 5, [switch]$NoClient) {
    $now = [DateTime]::UtcNow
    $takv = if ($NoClient) { "" } else { '<takv device="Laptop" platform="WinTAK-CIV" version="5.7"/>' }
    $d = New-Object System.Xml.XmlDocument
    $d.LoadXml(("<event version=""2.0"" uid=""{0}"" type=""a-f-G-U-C"" how=""m-g"" time=""{1}"" start=""{1}"" stale=""{2}""><point lat=""{3}"" lon=""{4}"" hae=""500"" ce=""9"" le=""9""/><detail><contact callsign=""{0}-CS"" endpoint=""*:-1:stcp""/>{5}<__group name=""Cyan"" role=""Team Member""/></detail></event>" -f $uid, $now.ToString("o"), $now.AddMinutes($staleMinutes).ToString("o"), $lat.ToString([Globalization.CultureInfo]::InvariantCulture), $lon.ToString([Globalization.CultureInfo]::InvariantCulture), $takv))
    return ,$d
}
function Record($doc) { $opsType.GetMethod("TryRecord").Invoke($null, @([System.Xml.XmlDocument]$doc, [DateTime]::UtcNow)) }
function Current { ,($opsType.GetMethod("Current").Invoke($null, @([DateTime]::UtcNow))) }
function Node-List {
    $list = New-NodeList
    $list.Add((New-Node @{ Name = "Node0"; MacAddress = "02:00:5E:10:00:00"; IsSelf = $true; Latitude = [Nullable[double]]$lat0; Longitude = [Nullable[double]]$lon0 }))
    $list.Add((New-Node @{ Name = "Node1"; MacAddress = "02:00:5E:10:00:01"; Latitude = [Nullable[double]]$lat1; Longitude = [Nullable[double]]$lon1 }))
    return ,$list
}
$topoType = $asm.GetType($ns + "OpenMANET_TopologyInfo"); $topo = [Activator]::CreateInstance($topoType, $true)
$edgeType = $asm.GetType($ns + "OpenMANET_TopologyEdgeInfo"); $edge = [Activator]::CreateInstance($edgeType, $true)
$edgeType.GetProperty("FromMac").SetValue($edge, "02:00:5E:10:00:00"); $edgeType.GetProperty("ToMac").SetValue($edge, "02:00:5E:10:00:01"); $edgeType.GetProperty("Metric").SetValue($edge, [double]32.4)
$topo.Edges.Add($edge)
function Update-Map($operators, [int]$meters = 25) {
    $fake.Sent.Clear()
    $layerType.GetMethod("Update", [Type[]]@([System.Collections.Generic.IReadOnlyList``1].MakeGenericType($asm.GetType($ns + "OpenMANET_NodeInfo")), $topoType, [System.Collections.Generic.IReadOnlyList``1].MakeGenericType($asm.GetType($ns + "OpenMANET_OperatorPosition")), [int])).Invoke($layer, @((Node-List), $topo, $operators, $meters)) | Out-Null
}
function Markers { @($fake.Sent | Where-Object { $_.DocumentElement.GetAttribute("type") -eq "a-f-G-U-U-S-R" } | ForEach-Object { $_.SelectSingleNode("/event/detail/contact").GetAttribute("callsign") }) }
function Deleted { @($fake.Sent | Where-Object { $_.DocumentElement.GetAttribute("type") -eq "t-x-d-d" } | ForEach-Object { $_.SelectSingleNode("/event/detail/link").GetAttribute("uid") }) }
function LinkEnds { $l = $fake.Sent | Where-Object { $_.DocumentElement.GetAttribute("type") -eq "u-d-f" } | Select-Object -First 1; if ($l) { @($l.SelectNodes("/event/detail/link") | ForEach-Object { $_.GetAttribute("point") }) } }
function Fmt([double]$lat, [double]$lon) { $lat.ToString("0.0000000", [Globalization.CultureInfo]::InvariantCulture) + "," + $lon.ToString("0.0000000", [Globalization.CultureInfo]::InvariantCulture) }

# --- Tracking operators
Check "Operator report from a TAK client is recorded" (Record (New-Sa "ANDROID-1" (North 5) $lon0))
Check "Reports without a TAK client element are ignored" (-not (Record (New-Sa "sensor-1" (North 5) $lon0 -NoClient)))
Check "Already-stale reports are ignored" (-not (Record (New-Sa "ANDROID-2" (North 5) $lon0 -staleMinutes -1)))
Check "0,0 (no position) is ignored" (-not (Record (New-Sa "ANDROID-3" 0 0)))
Check "The plugin's own markers are never operators" (-not (Record (New-Sa "openmanet-node-x" (North 5) $lon0)))
$cot = New-Object System.Xml.XmlDocument; $cot.LoadXml('<event version="2.0" uid="Node1-MANET" type="a-f-G-U-U-S-R" how="m-g" time="2026-10-28T17:00:00Z" start="2026-10-28T17:00:00Z" stale="2099-01-01T00:00:00Z"><point lat="34.2445" lon="-116.0905" hae="0" ce="9" le="9"/><detail><contact callsign="Node1-MANET"/><__group name="Magenta" role="MANET Radio"/><takv device="Node1-MANET" platform="RPI4 (OpenMANET)"/></detail></event>')
Check "A node's own COT OUT marker is a radio, not an operator" (-not (Record $cot))
[SelfPosition]::Doc = (New-Sa "S-1-5-21-SELF" (North 3) $lon0 -NoClient)
$ops = Current
Check "This operator's own position is included first" ($ops.Count -eq 2 -and $ops[0].Uid -eq "S-1-5-21-SELF")
$opsType.GetMethod("Clear").Invoke($null, @()) | Out-Null
$arg = New-Object WinTak.Common.CoT.CoTMessageArgument([System.Xml.XmlDocument](New-Sa "ANDROID-4" $lat1 $lon1))
$preview.Invoke($null, @($null, $arg.psobject.BaseObject)) | Out-Null
Check "WinTAK: other operators' reports are recorded and still drawn by WinTAK" ((-not $arg.Handled) -and ((Current) | Where-Object { $_.Uid -eq "ANDROID-4" }))

# --- Merging on the map
$opsType.GetMethod("Clear").Invoke($null, @()) | Out-Null
[SelfPosition]::Doc = (New-Sa "S-1-5-21-SELF" (North 5) $lon0 -NoClient)
Update-Map (Current)
Check "Operator 5 m from Node0: Node0 gets no radio icon, Node1 still does" ((@(Markers) -join ",") -eq "Node1")
$ends = LinkEnds
Check "Link line ends at the operator, not the hidden node position" ($ends.Count -eq 2 -and $ends[0] -eq (Fmt (North 5) $lon0) -and $ends[1] -eq (Fmt $lat1 $lon1))
Check "Radial menu: the operator's marker opens Node0" ($layerType.GetMethod("NodeKeyForOperator").Invoke($null, @("S-1-5-21-SELF")) -eq "02_00_5e_10_00_00")
Check "Radial menu: unrelated markers open nothing" ($null -eq $layerType.GetMethod("NodeKeyForOperator").Invoke($null, @("ANDROID-9")))

[SelfPosition]::Doc = (New-Sa "S-1-5-21-SELF" (North 32) $lon0 -NoClient)
Update-Map (Current)
Check "Already merged: stays merged out to 1.5x (32 m at 25 m) so it doesn't flicker" ((@(Markers) -join ",") -eq "Node1")
[SelfPosition]::Doc = (New-Sa "S-1-5-21-SELF" (North 40) $lon0 -NoClient)
Update-Map (Current)
Check "Operator walks away (40 m): Node0's radio icon comes back" ((@(Markers) -join ",") -eq "Node0,Node1")
Update-Map (Current)
[SelfPosition]::Doc = (New-Sa "S-1-5-21-SELF" (North 32) $lon0 -NoClient)
Update-Map (Current)
Check "Not merged yet: 32 m is outside 25 m" ((@(Markers) -join ",") -eq "Node0,Node1")
Update-Map (Current) 50
Check "Distance setting is used (50 m)" ((@(Markers) -join ",") -eq "Node1")

Record (New-Sa "ANDROID-5" ($lat1 + 4 / $metersPerDegree) $lon1) | Out-Null
Update-Map (Current) 50
Check "Both nodes held by operators: no radio icons, link joins the two operators" ((@(Markers).Count -eq 0) -and (LinkEnds).Count -eq 2 -and (LinkEnds)[1] -eq (Fmt ($lat1 + 4 / $metersPerDegree) $lon1))
Check "Merged node's own COT OUT marker stays hidden" ($layerType.GetMethod("HideNodeOwnMarker").Invoke($null, @([System.Xml.XmlDocument]$cot)))

$opsType.GetMethod("Clear").Invoke($null, @()) | Out-Null
[SelfPosition]::Doc = (New-Sa "S-1-5-21-SELF" (North 2) $lon0 -NoClient)
Record (New-Sa "ANDROID-6" (North 3) $lon0) | Out-Null
Update-Map (Current)
Check "One operator holding both ends of a link: no zero-length line" (((LinkEnds) | Measure-Object).Count -eq 2)
Update-Map $null
Check "Merging off: every node keeps its radio icon" ((@(Markers) -join ",") -eq "Node0,Node1")

# --- Without node icons (2.1.4.0 default): only TAK users' own markers and the links between them
function Update-MapNoIcons($operators, [int]$meters = 25) {
    $fake.Sent.Clear()
    $layerType.GetMethod("Update", [Type[]]@([System.Collections.Generic.IReadOnlyList``1].MakeGenericType($asm.GetType($ns + "OpenMANET_NodeInfo")), $topoType, [System.Collections.Generic.IReadOnlyList``1].MakeGenericType($asm.GetType($ns + "OpenMANET_OperatorPosition")), [int], [bool])).Invoke($layer, @((Node-List), $topo, $operators, $meters, $false)) | Out-Null
}
$layer = [Activator]::CreateInstance($layerType, $true)
$opsType.GetMethod("Clear").Invoke($null, @()) | Out-Null
[SelfPosition]::Doc = (New-Sa "S-1-5-21-SELF" (North 5) $lon0 -NoClient)
Update-MapNoIcons (Current)
Check "No icons: nobody at Node1, so it isn't drawn; Node0 is the user" ((@(Markers).Count -eq 0) -and $null -eq (LinkEnds))
Check "No icons: the link to an undrawn node isn't drawn either" (@($fake.Sent | Where-Object { $_.DocumentElement.GetAttribute("type") -eq "u-d-f" }).Count -eq 0)
Check "Startup: lines and markers WinTAK restored from last session are deleted" ((@(Deleted) -contains "openmanet-link-02_00_5e_10_00_00-02_00_5e_10_00_01") -and (@(Deleted) -contains "openmanet-node-02_00_5e_10_00_01"))
Update-MapNoIcons (Current)
Check "Startup cleanup happens once, not every poll" (@(Deleted).Count -eq 0)
Check "No icons: undrawn node's own COT OUT marker is still hidden" ($layerType.GetMethod("HideNodeOwnMarker").Invoke($null, @([System.Xml.XmlDocument]$cot)))
Record (New-Sa "ANDROID-7" ($lat1 + 4 / $metersPerDegree) $lon1) | Out-Null
Update-MapNoIcons (Current)
Check "No icons: users at both nodes get a link line between them" ((@(Markers).Count -eq 0) -and (LinkEnds).Count -eq 2 -and (LinkEnds)[0] -eq (Fmt (North 5) $lon0))
Update-MapNoIcons $null
Check "No icons, no users: nothing drawn, earlier line removed" ((@(Markers).Count -eq 0) -and (Deleted).Count -eq 1 -and $null -eq (LinkEnds))

# --- Preferences
$p = New-PluginObject "OpenMANET_Preferences" @([string](Join-Path $tmp "m.settings")); $p.Load()
Check "Node icons off by default; users count within 25 m" ((-not $p.ShowUnattendedNodes) -and $p.MergeDistanceMeters -eq 25)
$p.MergeDistanceMeters = 37
Check "Distances outside the choices fall back to 25 m" ($p.MergeDistanceMeters -eq 25)
$p.MergeDistanceMeters = 50; $p.ShowUnattendedNodes = $true; $p.Save()
$p2 = New-PluginObject "OpenMANET_Preferences" @([string](Join-Path $tmp "m.settings")); $p2.Load()
Check "Map settings save and reload" ($p2.ShowUnattendedNodes -and $p2.MergeDistanceMeters -eq 50 -and $p2.ShowOnMap)

Remove-Item $tmp -Recurse -Force

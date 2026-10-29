# One marker per node: the markers nodes broadcast for themselves (COT OUT) are hidden while the
# plugin draws that node (2.1.2.0).
param([Parameter(Mandatory = $true)][string]$Dll, [string]$WinTakDir = "C:\Program Files\WinTAK")
. (Join-Path $PSScriptRoot "Common.ps1") -Dll $Dll -WinTakDir $WinTakDir

$tmp = New-TempDir
$plugin = Use-TempPluginSettings $tmp

$refs = @((Join-Path $wt "WinTak.CursorOnTarget.dll"), (Join-Path $wt "WinTak.Common.dll"), (Join-Path $wt "WinTak.Graphics.dll"), (Join-Path $wt "WinTak.Framework.dll"), "System.Xml", "WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml")
Add-Type -ReferencedAssemblies $refs -TypeDefinition @"
using System.Collections.Generic; using System.Threading.Tasks; using System.Xml;
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
"@
$fake = New-Object RecordingCotSender
$hostType.GetProperty("CotSender", $any).SetValue($null, $fake)
$preview = $hostType.GetMethod("CotReceiver_PreviewMessageReceived", $any)
$layerType = $asm.GetType($ns + "OpenMANET_MapLayer"); $layer = [Activator]::CreateInstance($layerType, $true)

# What openmanetd sends with COT OUT on, as WinTAK hands it to plugins.
function New-NodeMarker($uid, $role = "MANET Radio", $type = "a-f-G-U-U-S-R") {
    $d = New-Object System.Xml.XmlDocument
    $d.LoadXml("<event version=""2.0"" uid=""$uid"" type=""$type"" how=""m-g"" time=""2026-10-28T17:00:00Z"" start=""2026-10-28T17:00:00Z"" stale=""2026-10-28T17:05:00Z""><point lat=""34.2458"" lon=""-116.0891"" hae=""561.9"" ce=""9.2"" le=""9999999""/><detail><contact callsign=""$uid""/><__group name=""Magenta"" role=""$role""/><takv device=""$uid"" platform=""RPI4 (OpenMANET)""/></detail></event>")
    return ,$d
}
function OwnName($doc) { $layerType.GetMethod("NodeOwnMarkerName").Invoke($null, @(,[System.Xml.XmlDocument]$doc)) }
function Send-Preview($doc) {
    $arg = New-Object WinTak.Common.CoT.CoTMessageArgument([System.Xml.XmlDocument]$doc)
    $preview.Invoke($null, @($null, $arg.psobject.BaseObject)) | Out-Null
    return $arg.Handled
}
function Deletes($uid) { @($fake.Sent | Where-Object { $_.DocumentElement.GetAttribute("type") -eq "t-x-d-d" -and $_.SelectSingleNode("/event/detail/link").GetAttribute("uid") -eq $uid }).Count }
function Update-Layer([object[]]$nodeProps) {
    $list = New-NodeList
    foreach ($p in $nodeProps) { $list.Add((New-Node $p)) }
    ($layerType.GetMethods() | Where-Object { $_.Name -eq "Update" -and $_.GetParameters().Count -eq 2 }).Invoke($layer, @($list, $null))
}

Check "Recognizes a node's own marker by type, role and UID" ((OwnName (New-NodeMarker "Node1-MANET")) -eq "node1-manet")
Check "Ignores other radio markers and the plugin's own" ($null -eq (OwnName (New-NodeMarker "Node1-MANET" "Team Member")) -and $null -eq (OwnName (New-NodeMarker "openmanet-node-node1")))

Check "Before the plugin draws any node, node markers pass through" (-not (Send-Preview (New-NodeMarker "Node1-MANET")))

Update-Layer @(@{ Name = "Node0"; IsSelf = $true; Latitude = [Nullable[double]]34.2457; Longitude = [Nullable[double]]-116.0890 }, @{ Name = "Node1"; Latitude = [Nullable[double]]34.2458; Longitude = [Nullable[double]]-116.0891 }, @{ Name = "Node2" })
Check "Drawn node: its own marker is hidden" (Send-Preview (New-NodeMarker "Node1-MANET"))
Check "Drawn node: the marker already on the map is removed once" ((Deletes "Node1-MANET") -eq 1)
Send-Preview (New-NodeMarker "Node1-MANET") | Out-Null
Check "Later broadcasts are hidden without another delete" ((Deletes "Node1-MANET") -eq 1)
Check "Hostname already containing 'manet' matches without the suffix" ((Update-Layer @(@{ Name = "relay-manet"; Latitude = [Nullable[double]]34.1; Longitude = [Nullable[double]]-116.1 }, @{ Name = "Node1"; Latitude = [Nullable[double]]34.2458; Longitude = [Nullable[double]]-116.0891 })) -eq $null -and (Send-Preview (New-NodeMarker "relay-manet")))
Check "Node without a plugin marker (no GPS) keeps its own marker" (-not (Send-Preview (New-NodeMarker "Node2-MANET")))
Check "Unknown node keeps its own marker" (-not (Send-Preview (New-NodeMarker "Node9-MANET")))

# Node1 loses its position in the plugin: its own marker comes back, and is removed again when the plugin redraws it.
Update-Layer @(@{ Name = "Node1" })
Check "Node the plugin stops drawing gets its own marker back" (-not (Send-Preview (New-NodeMarker "Node1-MANET")))
Update-Layer @(@{ Name = "Node1"; Latitude = [Nullable[double]]34.2458; Longitude = [Nullable[double]]-116.0891 })
Check "When the plugin draws it again, its own marker is removed again" ((Send-Preview (New-NodeMarker "Node1-MANET")) -and (Deletes "Node1-MANET") -eq 2)

$plugin.Preferences.HideNodeOwnMarkers = $false
Check "Setting off: node markers pass through" (-not (Send-Preview (New-NodeMarker "Node1-MANET")))
$plugin.Preferences.HideNodeOwnMarkers = $true
$layerType.GetMethod("Clear").Invoke($layer, @($true))
Check "Map cleared (disconnect or map off): node markers pass through" (-not (Send-Preview (New-NodeMarker "Node1-MANET")))
$prefsPath = Join-Path $tmp "p2.settings"
$p2 = New-PluginObject "OpenMANET_Preferences" @([string]$prefsPath); $p2.Load()
Check "Hiding is on by default" ($p2.HideNodeOwnMarkers)

Remove-Item $tmp -Recurse -Force

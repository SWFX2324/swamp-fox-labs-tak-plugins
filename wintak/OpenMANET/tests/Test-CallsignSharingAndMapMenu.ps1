# Callsign sharing over TAK, the map marker radial menu and the node status timeline (2.1.1).
param([Parameter(Mandatory = $true)][string]$Dll, [string]$WinTakDir = "C:\Program Files\WinTAK")
. (Join-Path $PSScriptRoot "Common.ps1") -Dll $Dll -WinTakDir $WinTakDir

$tmp = New-TempDir
$plugin = Use-TempPluginSettings $tmp
$prefs = $plugin.Preferences
$sharing = $plugin.Sharing
$storeType = $asm.GetType($ns + "OpenMANET_CallsignStore")
$sharingType = $asm.GetType($ns + "OpenMANET_CallsignSharing")
$entryType = $asm.GetType($ns + "OpenMANET_CallsignEntry")
function New-Store($file) { return $storeType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string](Join-Path $tmp $file))) }
function New-Sharing($file, $uid) { return $sharingType.GetConstructor([type[]]@([string], [string])).Invoke([object[]]@([string](Join-Path $tmp $file), [string]$uid)) }
function New-Entries([object[]]$items) {
    $list = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($entryType))
    foreach ($i in $items) {
        $e = [Activator]::CreateInstance($entryType); $e.Mac = $i[0]; $e.Hostname = $i[1]; $e.Callsign = $i[2]
        $e.UpdatedUtc = $(if ($i.Count -gt 3) { $i[3] } else { [DateTimeOffset]::UtcNow }); $list.Add($e)
    }
    return ,$list
}
function Macs([string[]]$m) { return ,([string[]]$m) }
$now = [DateTimeOffset]::UtcNow

# --- Callsign store: timestamps, and files from 2.1.0 and earlier.
Set-Content -Path (Join-Path $tmp "legacy.settings") -Value "02:00:5e:10:00:00`tnode0`tBASE" -Encoding UTF8
$legacy = New-Store "legacy.settings"; $legacy.Load()
Check "Store: 2.1.0 callsign file (3 columns) still loads" ($legacy.Find((Macs "02:00:5E:10:00:00"), "x") -eq "BASE")
$legacy.Set("02:00:5e:10:00:01", "node1", "RELAY")
$cols = (Get-Content (Join-Path $tmp "legacy.settings"))[1].Split("`t")
$reloaded = New-Store "legacy.settings"; $reloaded.Load()
$relay = $reloaded.Entries | Where-Object { $_.Callsign -eq "RELAY" }
Check "Store: saves a timestamp per callsign and reloads it" ($cols.Count -eq 4 -and ($now - $relay.UpdatedUtc).TotalMinutes -lt 1)

# --- The shared event.
$alice = New-Sharing "alice.settings" "ANDROID-alice"
$doc = $alice.CreateMessage("VIPER 1", (New-Entries @(, @("02:00:5e:10:00:00", "node0", "BASE"))), [DateTime]::UtcNow)
$evt = $doc.DocumentElement
Check "Event: data type, namespaced UID, point 0,0, 15 min stale" ($evt.GetAttribute("type") -eq "b-x-openmanet-callsigns" -and $evt.GetAttribute("uid") -eq "openmanet-callsigns-ANDROID-alice" -and $doc.SelectSingleNode("/event/point").GetAttribute("lat") -eq "0.0" -and ([DateTime]::Parse($evt.GetAttribute("stale")).ToUniversalTime() - [DateTime]::Parse($evt.GetAttribute("time")).ToUniversalTime()).TotalMinutes -eq 15)
Check "Event: no contact or archive element, so no client draws or keeps it" ($doc.SelectSingleNode("//contact") -eq $null -and $doc.SelectSingleNode("//archive") -eq $null)
Check "Event: carries MAC, hostname, callsign and sender name" ($doc.SelectSingleNode("//node").GetAttribute("mac") -eq "02:00:5e:10:00:00" -and $doc.SelectSingleNode("//node").GetAttribute("host") -eq "node0" -and $doc.SelectSingleNode("//__openmanet_callsigns").GetAttribute("from") -eq "VIPER 1")

# --- Receiving.
$bob = New-Sharing "bob.settings" "ANDROID-bob"
$received = New-Object System.Collections.ArrayList
$bob.add_Received({ param($s, $e) [void]$received.Add($e) })
Check "Receive: another operator's list is applied" ($bob.TryApply($doc, $now) -and $received.Count -eq 1 -and $received[0].SharedBy -eq "VIPER 1" -and $received[0].Count -eq 1)
$found = $bob.Find((Macs "02:00:5E:10:00:00"), "nodeX")
Check "Receive: found by MAC with who shared it" ($found.Callsign -eq "BASE" -and $found.SharedBy -eq "VIPER 1")
Check "Receive: the same list again is not a change" (-not $bob.TryApply($doc, $now) -and $received.Count -eq 1)
Check "Receive: our own echo is ignored" (-not $alice.TryApply($doc, $now) -and $alice.SenderCount -eq 0)
$spoof = $doc.Clone(); $spoof.DocumentElement.SetAttribute("uid", "openmanet-callsigns-ANDROID-carol")
Check "Receive: sender that doesn't match the event UID is rejected" (-not (New-Sharing "x.settings" "me").TryApply($spoof, $now))
$wrongType = $doc.Clone(); $wrongType.DocumentElement.SetAttribute("type", "a-f-G")
Check "Receive: other event types are ignored" (-not (New-Sharing "y.settings" "me").TryApply($wrongType, $now))

# Hostile input: bad characters, oversized fields, too many entries, a clock far ahead.
$evil = New-Sharing "evil.settings" "evil-sender"
$bad = $evil.CreateMessage("<script>Mallory</script>", (New-Entries @(, @("02:00:5e:10:00:09", "node9", "OK"))), [DateTime]::UtcNow)
$list = $bad.SelectSingleNode("//__openmanet_callsigns")
foreach ($attrs in @(@{ mac = "02:00:5e:10:00:0a"; callsign = "<b>x</b>" }, @{ mac = ("a" * 40); callsign = "LONGMAC" }, @{ host = ("h" * 80); callsign = "LONGHOST" }, @{ mac = "02:00:5e:10:00:0b"; callsign = ("C" * 30) })) {
    $n = $bad.CreateElement("node"); foreach ($k in $attrs.Keys) { $n.SetAttribute($k, $attrs[$k]) }; [void]$list.AppendChild($n)
}
$future = $bad.CreateElement("node"); $future.SetAttribute("mac", "02:00:5e:10:00:0c"); $future.SetAttribute("callsign", "FUTURE"); $future.SetAttribute("updated", "2099-01-01T00:00:00.000Z"); [void]$list.AppendChild($future)
for ($i = 0; $i -lt 300; $i++) { $n = $bad.CreateElement("node"); $n.SetAttribute("host", "bulk$i"); $n.SetAttribute("callsign", "B$i"); [void]$list.AppendChild($n) }
$victim = New-Sharing "victim.settings" "me"
[void]$victim.TryApply($bad, $now)
Check "Hostile: invalid callsigns and oversized MAC/hostname dropped" ($victim.Find((Macs "02:00:5e:10:00:0a"), $null) -eq $null -and $victim.Find((Macs "02:00:5e:10:00:0b"), $null) -eq $null -and $victim.Find((Macs ("a" * 40)), $null) -eq $null -and $victim.Find((Macs "02:00:5e:10:00:09"), $null).Callsign -eq "OK")
Check "Hostile: entries capped at 256 per operator" ($victim.EntryCount -eq 256)
Check "Hostile: sender name stripped to safe characters" ($victim.Find((Macs "02:00:5e:10:00:09"), $null).SharedBy -eq "scriptMallory/script")

# Conflicts between operators: a MAC match beats a hostname match; otherwise the newest wins.
$merge = New-Sharing "merge.settings" "me"
$older = (New-Sharing "o1" "op-one").CreateMessage("ONE", (New-Entries @(, @("02:00:5e:10:00:00", "node0", "OLDER", $now.AddHours(-2)))), [DateTime]::UtcNow)
$newer = (New-Sharing "o2" "op-two").CreateMessage("TWO", (New-Entries @(, @("02:00:5e:10:00:00", "node0", "NEWER", $now.AddHours(-1)))), [DateTime]::UtcNow)
$hostOnly = (New-Sharing "o3" "op-three").CreateMessage("THREE", (New-Entries @(, @($null, "node0", "BYHOST", $now))), [DateTime]::UtcNow)
[void]$merge.TryApply($older, $now); [void]$merge.TryApply($newer, $now); [void]$merge.TryApply($hostOnly, $now)
Check "Conflict: newest MAC match wins over older and over hostname-only" ($merge.Find((Macs "02:00:5e:10:00:00"), "node0").Callsign -eq "NEWER")
Check "Conflict: hostname-only share used when no MAC matches" ($merge.Find((Macs "02:00:5e:10:00:77"), "node0").Callsign -eq "BYHOST")

# The node's own operator has the final say over newer names from anyone else.
$ownerEntries = New-Entries @(, @("02:00:5e:10:00:00", "node0", "OWNER", $now.AddHours(-3)))
$ownerEntries[0].FromNodeOperator = $true
$ownerMsg = (New-Sharing "o4" "op-owner").CreateMessage("OWNER-OP", $ownerEntries, [DateTime]::UtcNow)
Check "Owner: share marks the node's own operator" ($ownerMsg.SelectSingleNode("//node").GetAttribute("operator") -eq "1" -and $older.SelectSingleNode("//node").GetAttribute("operator") -eq "")
[void]$merge.TryApply($ownerMsg, $now)
Check "Owner: node's operator beats a newer name from someone else" ($merge.Find((Macs "02:00:5e:10:00:00"), "node0").Callsign -eq "OWNER" -and $merge.Find((Macs "02:00:5e:10:00:00"), "node0").SharedBy -eq "OWNER-OP")
$hijack = (New-Sharing "o5" "op-five").CreateMessage("FIVE", (New-Entries @(, @("02:00:5e:10:00:00", "node0", "HIJACK", $now))), [DateTime]::UtcNow)
[void]$merge.TryApply($hijack, $now)
Check "Owner: another operator renaming it later doesn't change it" ($merge.Find((Macs "02:00:5e:10:00:00"), "node0").Callsign -eq "OWNER")
$relay = (New-Sharing "o6" "op-lead").CreateMessage("LEAD", (New-Entries @(, @("02:00:5e:10:00:42", "relay", "HILLTOP"))), [DateTime]::UtcNow)
[void]$merge.TryApply($relay, $now)
Check "Owner: others still name nodes nobody connected has named" ($merge.Find((Macs "02:00:5e:10:00:42"), "relay").Callsign -eq "HILLTOP")
$mergeReload = New-Sharing "merge.settings" "me"; $mergeReload.Load()
Check "Owner: priority survives a restart" ($mergeReload.Find((Macs "02:00:5e:10:00:00"), "node0").Callsign -eq "OWNER")
[void]$merge.TryApply((New-Sharing "o4" "op-owner").CreateMessage("OWNER-OP", (New-Entries @()), [DateTime]::UtcNow), $now)
Check "Owner: when they stop sharing, the newest other name returns" ($merge.Find((Macs "02:00:5e:10:00:00"), "node0").Callsign -eq "HIJACK")

# Withdrawal and persistence.
$reload = New-Sharing "bob.settings" "ANDROID-bob"; $reload.Load()
Check "Persist: received callsigns survive a restart" ($reload.Find((Macs "02:00:5e:10:00:00"), $null).Callsign -eq "BASE")
$withdraw = $alice.CreateMessage("VIPER 1", (New-Entries @()), [DateTime]::UtcNow)
Check "Withdraw: an empty list removes that operator's callsigns" ($bob.TryApply($withdraw, $now) -and $bob.SenderCount -eq 0 -and $bob.Find((Macs "02:00:5e:10:00:00"), "node0") -eq $null -and $received[-1].Count -eq 0)

# --- WinTAK wiring: incoming events are taken out of normal processing; outgoing go to the network.
$preview = $hostType.GetMethod("CotReceiver_PreviewMessageReceived", $any)
$incoming = (New-Sharing "z" "ANDROID-dave").CreateMessage("DAVE", (New-Entries @(, @("02:00:5e:10:00:05", "node5", "DELTA"))), [DateTime]::UtcNow)
$prefs.UseSharedCallsigns = $false
$arg = New-Object WinTak.Common.CoT.CoTMessageArgument($incoming)
$preview.Invoke($null, @($null, $arg.psobject.BaseObject))
Check "WinTAK: callsign events marked handled; not applied while turned off" ($arg.Handled -and $sharing.Find((Macs "02:00:5e:10:00:05"), $null) -eq $null)
$prefs.UseSharedCallsigns = $true
$arg = New-Object WinTak.Common.CoT.CoTMessageArgument($incoming)
$preview.Invoke($null, @($null, $arg.psobject.BaseObject))
Check "WinTAK: applied when 'Use shared callsigns' is on" ($arg.Handled -and $sharing.Find((Macs "02:00:5e:10:00:05"), $null).Callsign -eq "DELTA")
$markerDoc = New-Object System.Xml.XmlDocument; $markerDoc.LoadXml('<event version="2.0" uid="x" type="a-f-G-U-C" how="m-g" time="2026-01-01T00:00:00Z" start="2026-01-01T00:00:00Z" stale="2026-01-01T00:05:00Z"><point lat="1" lon="1" hae="0" ce="1" le="1"/><detail/></event>')
$arg = New-Object WinTak.Common.CoT.CoTMessageArgument($markerDoc)
$preview.Invoke($null, @($null, $arg.psobject.BaseObject))
Check "WinTAK: other CoT passes through untouched" (-not $arg.Handled)

$refs = @((Join-Path $wt "WinTak.Common.dll"), "System.Xml", "System")
Add-Type -ReferencedAssemblies $refs -TypeDefinition @"
using System; using System.Collections.Generic; using System.IO; using System.Net.NetworkInformation; using System.Xml;
using WinTak.Common.CoT; using WinTak.Common.Commo; using WinTak.Common.Services;
public class RecordingCommunicationService : ICommunicationService {
  public List<XmlDocument> Broadcasts = new List<XmlDocument>();
  public event EventHandler<ConnectionEventArgs> Connecting { add {} remove {} }
  public event EventHandler<ConnectionEventArgs> Connected { add {} remove {} }
  public event EventHandler<ConnectionEventArgs> Disconnected { add {} remove {} }
  public event EventHandler<FileTransferEventArgs> FileTransferring { add {} remove {} }
  public event EventHandler<FileTransferEventArgs> FileTransferred { add {} remove {} }
  public event EventHandler<FileTransferErrorEventArgs> FileTransferFailed { add {} remove {} }
  public event EventHandler<FileUploadEventArgs> FileUploading { add {} remove {} }
  public event EventHandler<FileUploadEventArgs> FileUploaded { add {} remove {} }
  public event EventHandler<FileUploadErrorEventArgs> FileUploadFailed { add {} remove {} }
  public event EventHandler<CoTMessageArgument> PreviewCotBroadcast { add {} remove {} }
  public event EventHandler<GenericDataArgument> GenericDataReceived { add {} remove {} }
  public event EventHandler<StreamingConnectionInterface> StreamingConnectionEnrolled { add {} remove {} }
  public IEnumerable<StreamingConnection> GetStreamingConnections() { return new StreamingConnection[0]; }
  public IEnumerable<InputConnection> GetInputConnections() { return new InputConnection[0]; }
  public string GetStreamIdForConnection(Uri uri) { return null; }
  public NetworkInterface[] GetValidNetworkInterfaces() { return new NetworkInterface[0]; }
  public Uri SelectStreamingUri() { return null; }
  public bool AddContact(string contactUid, string callsign, string ipAddress, int port) { return false; }
  public bool AddContact(string contactUid, string callsign, string ipAddress) { return false; }
  public void BroadcastCot(XmlDocument cot) { Broadcasts.Add(cot); }
  public void BroadcastCot(XmlDocument cot, SendMethod sendMethod) { Broadcasts.Add(cot); }
  public void SendCot(string contactUid, XmlDocument cot) {}
  public void SendCot(string contactUid, XmlDocument cot, SendMethod sendMethod) {}
  public void SendCot(List<string> contactUids, XmlDocument cot) {}
  public void SendCot(List<string> contactUids, XmlDocument cot, SendMethod sendMethod) {}
  public void SendMissionPackage(List<string> contactUids, FileInfo file, string name, bool deleteAfterSend) {}
  public void SendMissionPackage(Uri serverUrl, FileInfo file) {}
}
"@
$comms = New-Object RecordingCommunicationService
$hostType.GetField("_communicationService", $any).SetValue($null, $comms)

# --- The pane: sharing toggles, shared names in the views, marker clicks and the timeline.
$ctlType = $asm.GetType($ns + "OpenMANET"); $ctl = [Activator]::CreateInstance($ctlType)
function F($name) { return ,($ctlType.GetField($name, $any).GetValue($ctl)) }
function SetF($name, $v) { $ctlType.GetField($name, $any).SetValue($ctl, $v) }
function M($name, [object[]]$a) { $ctlType.GetMethod($name, $any).Invoke($ctl, $a) }
$store = New-Store "pane-callsigns.settings"; SetF "_callsigns" $store
SetF "_settingsFilePath" ([string](Join-Path $tmp "connection.settings"))
$store.Set("02:00:5e:10:00:00", "Node0", "MINE")

$nodes = New-NodeList
$n0 = New-Node @{ Name = "Node0"; MacAddress = "02:00:5E:10:00:00"; IsOnline = $true; IsRouted = $true; HasLiveLink = $true }
$n5 = New-Node @{ Name = "Node5"; MacAddress = "02:00:5E:10:00:05"; IsOnline = $false; IsDiscovering = $true }
$nodes.Add($n0); $nodes.Add($n5)
SetF "_lastNodes" $nodes
(F "NodesListBox").ItemsSource = $nodes
M "ApplyCallsigns" @($nodes, $null)
Check "Pane: own callsign wins; shared one fills the gap" ($n0.Callsign -eq "MINE" -and $n0.CallsignSharedBy -eq $null -and $n5.Callsign -eq "DELTA" -and $n5.CallsignSharedBy -eq "DAVE")
(F "NodesListBox").SelectedItem = $n5
Check "Pane: editor stays empty for a shared name and says who shared it" ((F "CallsignTextBox").Text -eq "" -and (F "CallsignCaptionText").Text -like "*shared by DAVE*")
(F "UseSharedCallsignsCheckBox").IsChecked = $false
Check "Pane: turning 'Use shared' off goes back to the hostname" ($n5.Callsign -eq $null -and -not $prefs.UseSharedCallsigns)
(F "UseSharedCallsignsCheckBox").IsChecked = $true
$requests = @($comms.Broadcasts | Where-Object { $_.DocumentElement.GetAttribute("type") -eq "b-x-openmanet-callsigns-request" })
Check "Request: sent when the pane opens and when 'Use shared' is turned on" ($requests.Count -eq 2 -and $comms.Broadcasts.Count -eq 2 -and $requests[0].SelectSingleNode("//__openmanet_callsigns").GetAttribute("sender") -eq "test-local-uid" -and $requests[0].SelectSingleNode("//contact") -eq $null)
$comms.Broadcasts.Clear()

(F "ShareCallsignsCheckBox").IsChecked = $true
$sent = $comms.Broadcasts[-1]
Check "Share on: broadcasts saved callsigns with the operator's TAK callsign" ($comms.Broadcasts.Count -eq 1 -and $sent.SelectSingleNode("//node").GetAttribute("callsign") -eq "MINE" -and $sent.SelectSingleNode("//__openmanet_callsigns").GetAttribute("from") -eq "TEST-OP" -and $prefs.ShareCallsigns)
(F "NodesListBox").SelectedItem = $n0
M "SetSelectedNodeCallsign" @("BASE-2")
Check "Share: changing a callsign rebroadcasts it" ($comms.Broadcasts.Count -eq 2 -and $comms.Broadcasts[-1].SelectSingleNode("//node").GetAttribute("callsign") -eq "BASE-2")
(F "ShareCallsignsCheckBox").IsChecked = $false
Check "Share off: broadcasts an empty list to withdraw" ($comms.Broadcasts.Count -eq 3 -and $comms.Broadcasts[-1].SelectNodes("//node").Count -eq 0)

# Answering another operator's request for shared callsigns.
function Send-Request($uid) {
    $r = (New-Sharing "req-$uid" $uid).CreateRequest([DateTime]::UtcNow)
    $a = New-Object WinTak.Common.CoT.CoTMessageArgument($r)
    $preview.Invoke($null, @($null, $a.psobject.BaseObject)) | Out-Null
    Pump-Dispatcher 0.3
    return $a.Handled
}
$comms.Broadcasts.Clear()
Check "Request: ignored while not sharing, and kept off the map" ((Send-Request "ANDROID-frank") -and $comms.Broadcasts.Count -eq 0)
$prefs.ShareCallsigns = $true; SetF "_lastCallsignShareUtc" ([DateTimeOffset]::UtcNow.AddMinutes(-1))
[void](Send-Request "ANDROID-frank")
Check "Request: answered right away with the saved list" ($comms.Broadcasts.Count -eq 1 -and $comms.Broadcasts[0].SelectSingleNode("//node").GetAttribute("callsign") -eq "BASE-2")
[void](Send-Request "ANDROID-gina")
Check "Request: a second request within 10 s isn't answered again" ($comms.Broadcasts.Count -eq 1)
SetF "_lastCallsignShareUtc" ([DateTimeOffset]::UtcNow.AddMinutes(-1))
[void](Send-Request "test-local-uid")
Check "Request: our own request echoed back is ignored" ($comms.Broadcasts.Count -eq 1)
Check "Rebroadcast: every minute" ($sharingType.GetField("RebroadcastInterval").GetValue($null).TotalMinutes -eq 1)
$prefs.ShareCallsigns = $false; $comms.Broadcasts.Clear()

$events = F "_meshEvents"
$before = $events.Count
$fresh = (New-Sharing "w" "ANDROID-erin").CreateMessage("ERIN", (New-Entries @(, @("02:00:5e:10:00:05", "node5", "ECHO", $now.AddMinutes(1)))), [DateTime]::UtcNow)
$arg = New-Object WinTak.Common.CoT.CoTMessageArgument($fresh)
$preview.Invoke($null, @($null, $arg.psobject.BaseObject))
Pump-Dispatcher 0.5
Check "Pane: received callsigns logged and shown without a poll" ($events.Count -gt $before -and $events[0].Message -like "Received 1 callsign from ERIN*" -and $n5.Callsign -eq "ECHO")
Check "Pane: sharing status counts operators" ((F "SharedCallsignsStatusText").Text -like "Holding 2 callsigns from 2 operators*")
(F "ForgetSharedCallsignsButton").RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)))
Check "Pane: Forget Shared drops received names" ($sharing.SenderCount -eq 0 -and $n5.Callsign -eq $null -and -not (F "ForgetSharedCallsignsButton").IsEnabled)

# Map marker radial menu: the marker UID leads back to the node.
$layerType = $asm.GetType($ns + "OpenMANET_MapLayer")
$key = $layerType.GetMethod("NodeKey").Invoke($null, @($n5))
Check "Map menu: node key round-trips through the marker UID" ($layerType.GetMethod("NodeKeyFromUid").Invoke($null, @("openmanet-node-" + $key)) -eq $key -and $key -eq "02_00_5e_10_00_05")
Check "Map menu: links, other plugins' items and blanks are ignored" ($layerType.GetMethod("NodeKeyFromUid").Invoke($null, @("openmanet-link-a-b")) -eq $null -and $layerType.GetMethod("NodeKeyFromUid").Invoke($null, @("ANDROID-123")) -eq $null -and $layerType.GetMethod("NodeKeyFromUid").Invoke($null, @("openmanet-node-")) -eq $null)
(F "MainTabControl").SelectedIndex = 0; (F "NodesListBox").SelectedItem = $n0
$hostType.GetMethod("RequestNodeFocus").Invoke($null, @($key))
Check "Map menu: choosing it opens the Nodes tab on that node" ((F "MainTabControl").SelectedIndex -eq 2 -and (F "NodesListBox").SelectedItem -eq $n5)

# Status timeline for cold-boot diagnosis.
M "RecordNodeTimelines" @(, $nodes)
M "RecordNodeTimelines" @(, $nodes)
$n5.IsDiscovering = $false; $n5.IsOnline = $true; $n5.HasLiveLink = $true; $n5.HasDirectLinkMetrics = $true
M "RecordNodeTimelines" @(, $nodes)
$timelines = F "_nodeTimelines"
$t5 = $timelines["mac:02:00:5E:10:00:05"]
Check "Timeline: one entry per change, not per poll" ($t5.Count -eq 2 -and $t5[0].State -like "Discovering*" -and $t5[1].State -like "Online*RF link yes*neighbor table yes*")
$full = M "BuildDiagnosticSnapshot" @($true)
$redacted = M "BuildDiagnosticSnapshot" @($false)
Check "Timeline: in the snapshot with time since first seen" ($full -match "NODE STATUS TIMELINE" -and $full -match "\(\+0 s\) Discovering" -and $full -match "Node5")
Check "Timeline: redacted snapshot hides node names" ($redacted -match "NODE STATUS TIMELINE" -and $redacted -notmatch "Node5" -and $redacted -match "CALLSIGN SHARING")

# Field case from 2.1.1.0 testing: an operator named the node they connect to while their plugin
# knew it only by its own interface MAC and the "Local node" placeholder, so nobody could match it.
Set-Content -Path (Join-Path $tmp "field.settings") -Value "62:5f:69:0c:af:b0`tlocal node`tSWAMP FOX`t2026-10-28T04:22:34.796Z" -Encoding UTF8
$field = New-Store "field.settings"; $field.Load()
Check "Field: 'Local node' placeholder is never a callsign key" ($field.Count -eq 1 -and $field.Entries[0].Hostname -eq $null -and $field.Find((Macs "aa:bb:cc:dd:ee:ff"), "Local node") -eq $null)

$clientType = $asm.GetType($ns + "OpenMANET_Client")
$topoType = $clientType.GetNestedType("MeshTopology", [System.Reflection.BindingFlags]::NonPublic)
$topoJson = '{"selfMac":"62:5f:69:0c:af:b0","nodes":[{"mac":"62:5f:69:0c:af:b0","isSelf":true,"hopsFromSelf":0,"secondaryMacs":["9c:04:b6:a0:aa:3f"]}]}'
$selfTopo = $clientType.GetMethod("DeserializeJson", $any).MakeGenericMethod($topoType).Invoke($null, @(,[Text.Encoding]::UTF8.GetBytes($topoJson)))
$fieldClient = [Activator]::CreateInstance($clientType, $true)
$sys = New-PluginObject "OpenMANET_SystemInfo"; $sys.Hostname = "Node1"
$clientType.GetProperty("LastSystemDetails").SetValue($fieldClient, $sys)
$emptyNodes = New-NodeList
$self = $clientType.GetMethod("CreateSelfNode", $any).Invoke($fieldClient, @($selfTopo, $emptyNodes, [DateTimeOffset]::UtcNow, 1))
Check "Field: connected node takes its hostname from system status" ($self.Name -eq "Node1" -and $self.IsSelf)

# Sharing side: their pane, connected to Node1, with the old entry.
SetF "_callsigns" $field
$selfNode = New-Node @{ Name = "Node1"; MacAddress = "62:5F:69:0C:AF:B0"; IsSelf = $true; IsOnline = $true }
$fieldNodes = New-NodeList; $fieldNodes.Add($selfNode); SetF "_lastNodes" $fieldNodes
$fieldTopo = New-PluginObject "OpenMANET_TopologyInfo"
$st = New-PluginObject "OpenMANET_TopologyNodeInfo"; $st.Hostname = "Node1"; $st.MacAddress = "62:5F:69:0C:AF:B0"; $st.IsSelf = $true; $st.SecondaryMacs.Add("9C:04:B6:A0:AA:3F"); $fieldTopo.Nodes.Add($st)
SetF "_lastTopology" $fieldTopo
$expanded = $ctlType.GetMethod("ExpandForSharing", $any).Invoke($ctl, @(, $field.Entries))
Check "Field: shared entry carries the hostname and every known MAC" ($expanded.Count -eq 2 -and @($expanded | Where-Object { $_.Hostname -eq "node1" -and $_.Callsign -eq "SWAMP FOX" }).Count -eq 2 -and @($expanded | Where-Object { $_.Mac -eq "9c:04:b6:a0:aa:3f" }).Count -eq 1)
Check "Owner: naming your connected node shares you as its operator" (@($expanded | Where-Object { $_.FromNodeOperator }).Count -eq 2)
$fieldMsg = (New-Sharing "field-sender" "ANDROID-longbeard").CreateMessage("Longbeard", $expanded, [DateTime]::UtcNow)
# Receiving side: connected to Node0, which sees Node1 by its mesh MAC and link MAC.
$rx = New-Sharing "field-rx.settings" "me"; [void]$rx.TryApply($fieldMsg, $now)
Check "Field: the other operator now matches Node1 (by link MAC or hostname)" ($rx.Find((Macs "F2:7E:C1:6A:68:30", "9C:04:B6:A0:AA:3F"), "Node1").Callsign -eq "SWAMP FOX" -and $rx.Find((Macs "F2:7E:C1:6A:68:30"), "Node1").Callsign -eq "SWAMP FOX")

Remove-Item -Recurse -Force $tmp -Confirm:$false

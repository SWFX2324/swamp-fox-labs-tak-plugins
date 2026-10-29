# Map layer CoT, GPS parsing and HTTPS certificate pinning (2.1.0).
param([Parameter(Mandatory = $true)][string]$Dll, [string]$WinTakDir = "C:\Program Files\WinTAK")
. (Join-Path $PSScriptRoot "Common.ps1") -Dll $Dll -WinTakDir $WinTakDir

# Fake CoT sender that records what the map layer sends.
$refs = @((Join-Path $wt "WinTak.CursorOnTarget.dll"), (Join-Path $wt "WinTak.Common.dll"), (Join-Path $wt "WinTak.Graphics.dll"), (Join-Path $wt "WinTak.Framework.dll"), "System.Xml", "WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml")
Add-Type -ReferencedAssemblies $refs -TypeDefinition @"
using System.Collections.Generic; using System.Threading.Tasks; using System.Xml;
using WinTak.CursorOnTarget.Services; using WinTak.Common.CoT; using WinTak.Graphics;
public class RecordingCotSender : ICotMessageSender {
  public List<XmlDocument> Sent = new List<XmlDocument>();
  public int NetworkSends = 0;
  public void Process(CotEvent m) { NetworkSends++; } public Task<bool> ProcessAsync(CotEvent m) { NetworkSends++; return Task.FromResult(true); }
  public void Process(CoTMessageArgument a) { NetworkSends++; } public Task<bool> ProcessAsync(CoTMessageArgument a) { NetworkSends++; return Task.FromResult(true); }
  public void Send(XmlDocument m) { NetworkSends++; } public void Send(CoTMessageArgument a) { NetworkSends++; }
  public void ProcessInternal(CotEvent m) {} public void ProcessInternal(MapItem i) {}
  public Task<bool> ProcessInternalAsync(CotEvent m) { return Task.FromResult(true); }
  public void SendInternal(XmlDocument m) { Sent.Add(m); }
  public Task<bool> ProcessInternalAsync(XmlDocument m) { Sent.Add(m); return Task.FromResult(true); }
}
"@

$fake = New-Object RecordingCotSender
$hostType = $asm.GetType($ns + "OpenMANET_PluginHost")
$hostType.GetProperty("CotSender", $any).SetValue($null, $fake)

function New-Obj($typeName, [hashtable]$props) {
    $t = $asm.GetType($ns + $typeName); $o = [Activator]::CreateInstance($t, $true)
    foreach ($k in $props.Keys) { $t.GetProperty($k).SetValue($o, $props[$k]) }; return $o
}
$nodeType = $asm.GetType($ns + "OpenMANET_NodeInfo")
$nodes = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($nodeType))
$nodes.Add((New-Obj "OpenMANET_NodeInfo" @{ Name = "Node1"; Address = "10.41.0.2"; MacAddress = "02:00:5E:10:00:01"; IsOnline = $true; IsRouted = $true; IsSelf = $true; HopsFromSelf = [Nullable[int]]0; Latitude = [Nullable[double]]32.7765; Longitude = [Nullable[double]](-79.9311); Altitude = [Nullable[double]]5.0 }))
$nodes.Add((New-Obj "OpenMANET_NodeInfo" @{ Name = "Node0"; Callsign = "BASE"; Address = "10.41.0.1"; MacAddress = "02:00:5E:10:00:00"; IsOnline = $true; IsRouted = $true; HasLiveLink = $true; IsGateway = $true; HopsFromSelf = [Nullable[int]]1; Rssi = [Nullable[int]](-70); CurrentRssi = [Nullable[int]](-70); Latitude = [Nullable[double]]32.7801; Longitude = [Nullable[double]](-79.9250) }))
$nodes.Add((New-Obj "OpenMANET_NodeInfo" @{ Name = "Node3"; Address = "10.41.0.4"; MacAddress = "02:00:5E:10:00:03"; IsOnline = $false; Latitude = [Nullable[double]]32.7700; Longitude = [Nullable[double]](-79.9400) }))
$nodes.Add((New-Obj "OpenMANET_NodeInfo" @{ Name = "Node4"; Address = "10.41.0.5"; MacAddress = "02:00:5E:10:00:04"; IsOnline = $true }))
$topo = New-Obj "OpenMANET_TopologyInfo" @{ SelfMac = "02:00:5E:10:00:01"; SelfHostname = "Node1" }
$tn = New-Obj "OpenMANET_TopologyNodeInfo" @{ Hostname = "Node0"; MacAddress = "02:00:5E:AA:00:00" }; $tn.SecondaryMacs.Add("02:00:5E:20:00:00"); $topo.Nodes.Add($tn)
$topo.Nodes.Add((New-Obj "OpenMANET_TopologyNodeInfo" @{ Hostname = "Node1"; MacAddress = "02:00:5E:10:00:01"; IsSelf = $true }))
# The first edge names Node0 by a secondary MAC; the second ends at a node without GPS.
$topo.Edges.Add((New-Obj "OpenMANET_TopologyEdgeInfo" @{ FromMac = "02:00:5E:10:00:01"; ToMac = "02:00:5E:20:00:00"; Metric = [double]32.4; IsOnMyPath = $true }))
$topo.Edges.Add((New-Obj "OpenMANET_TopologyEdgeInfo" @{ FromMac = "02:00:5E:10:00:01"; ToMac = "02:00:5E:10:00:04"; Metric = [double]5.0 }))

$layerType = $asm.GetType($ns + "OpenMANET_MapLayer"); $layer = [Activator]::CreateInstance($layerType, $true)
($layerType.GetMethods() | Where-Object { $_.Name -eq "Update" -and $_.GetParameters().Count -eq 2 }).Invoke($layer, @($nodes, $topo))
$docs = @($fake.Sent)
$markers = @($docs | Where-Object { $_.DocumentElement.GetAttribute("type") -eq "a-f-G-U-U-S-R" })
$links = @($docs | Where-Object { $_.DocumentElement.GetAttribute("type") -eq "u-d-f" })
Check "3 markers (nodes with GPS only), 1 link" ($markers.Count -eq 3 -and $links.Count -eq 1)
$base = $markers | Where-Object { $_.SelectSingleNode("/event/detail/contact").GetAttribute("callsign") -eq "BASE" }
Check "Marker uses callsign and position" ($base -ne $null -and $base.SelectSingleNode("/event/point").GetAttribute("lat") -eq "32.7801000")
$staleNode = $markers | Where-Object { $_.SelectSingleNode("/event/detail/contact").GetAttribute("callsign") -eq "Node3" }
$st = [DateTime]::Parse($staleNode.DocumentElement.GetAttribute("stale")).ToUniversalTime(); $tm = [DateTime]::Parse($staleNode.DocumentElement.GetAttribute("time")).ToUniversalTime()
$bst = [DateTime]::Parse($base.DocumentElement.GetAttribute("stale")).ToUniversalTime()
Check "Stale node published already stale; live node ~3 min" ($st -le $tm.AddSeconds(1) -and ($bst - $tm).TotalMinutes -gt 2.5)
$link = $links[0]
$pts = $link.SelectNodes("/event/detail/link")
$color = [int]$link.SelectSingleNode("/event/detail/strokeColor").GetAttribute("value")
$fair = [BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]4294096202), 0)
Check "Link resolved via secondary MAC, 2 points, fair color at -70 dBm" ($pts.Count -eq 2 -and $color -eq $fair)
Check "No archive flag; nothing sent through network Send/Process" ((@($docs | Where-Object { $_.SelectSingleNode("//archive") })).Count -eq 0 -and $fake.NetworkSends -eq 0)
$uids = $docs | ForEach-Object { $_.DocumentElement.GetAttribute("uid") }
Check "UIDs are namespaced openmanet-*" ((@($uids | Where-Object { $_ -notlike "openmanet-*" })).Count -eq 0)

# A second update without Node3 must delete its marker.
$fake.Sent.Clear(); $nodes.RemoveAt(2)
($layerType.GetMethods() | Where-Object { $_.Name -eq "Update" -and $_.GetParameters().Count -eq 2 }).Invoke($layer, @($nodes, $topo))
$deletes = @(@($fake.Sent) | Where-Object { $_.DocumentElement.GetAttribute("type") -eq "t-x-d-d" })
Check "Removed node gets a t-x-d-d delete with link uid" ($deletes.Count -eq 1 -and $deletes[0].SelectSingleNode("/event/detail/link").GetAttribute("uid") -like "openmanet-node-*03" -and $deletes[0].SelectSingleNode("/event/detail/__forcedelete") -ne $null)
$fake.Sent.Clear(); $layerType.GetMethod("Clear").Invoke($layer, @($true))
Check "Clear deletes all remaining (2 markers + 1 link)" ($fake.Sent.Count -eq 3 -and (@(@($fake.Sent) | Where-Object { $_.DocumentElement.GetAttribute("type") -ne "t-x-d-d" })).Count -eq 0)

# JSON parsing of positions and the GNSS fix.
$clientType = $asm.GetType($ns + "OpenMANET_Client")
$listType = $clientType.GetNestedType("ListNodesResponse", [System.Reflection.BindingFlags]::NonPublic)
$gnssType = $clientType.GetNestedType("GnssStatusResponse", [System.Reflection.BindingFlags]::NonPublic)
$deser = $clientType.GetMethod("DeserializeJson", $any); $validate = $clientType.GetMethod("ValidateAndNormalizeResponse", $any)
$json = '{"nodes":[{"mac":"aa","hostname":"Node0","ipaddr":"10.41.0.1","position":{"latitude":32.78,"longitude":-79.92,"altitude":12.5}},{"mac":"bb","hostname":"Node5","ipaddr":"10.41.0.6","position":{"latitude":0,"longitude":0}},{"mac":"cc","hostname":"Node6","ipaddr":"10.41.0.7"}]}'
$resp = $deser.MakeGenericMethod($listType).Invoke($null, @(,[Text.Encoding]::UTF8.GetBytes($json))); $validate.MakeGenericMethod($listType).Invoke($null, @($resp))
Check "Node position parsed; 0,0 and missing treated as no fix" ($resp.Nodes[0].Position.Latitude -eq 32.78 -and $resp.Nodes[0].Position.Altitude -eq 12.5 -and $resp.Nodes[1].Position -eq $null -and $resp.Nodes[2].Position -eq $null)
$g = $deser.MakeGenericMethod($gnssType).Invoke($null, @(,[Text.Encoding]::UTF8.GetBytes('{"position":{"fixType":"FIX_TYPE_3D","latitude":32.7765,"longitude":-79.9311,"altitude":5}}')))
Check "GNSS fix parsed with fixType" ($g.Position.FixType -eq "FIX_TYPE_3D" -and $g.Position.Longitude -eq -79.9311)
$g2 = $deser.MakeGenericMethod($gnssType).Invoke($null, @(,[Text.Encoding]::UTF8.GetBytes('{}')))
Check "Empty GNSS response (no receiver) is harmless" ($g2.Position -eq $null)

# Certificate pinning, with an in-memory certificate (nothing touches the Windows store).
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$req = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest("CN=node-test", $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
$cert = $req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(30))
$thumb = $cert.GetCertHashString([System.Security.Cryptography.HashAlgorithmName]::SHA256)
$client = [Activator]::CreateInstance($clientType, $true)
$v = $clientType.GetMethod("ValidateServerCertificate", $any)
$none = [System.Net.Security.SslPolicyErrors]::None; $chainErr = [System.Net.Security.SslPolicyErrors]::RemoteCertificateChainErrors
Check "First use: trusted cert accepted, fingerprint recorded" ($v.Invoke($client, @($null, $cert, $null, $none, $null)) -and $clientType.GetProperty("ServerCertificateThumbprint").GetValue($client) -eq $thumb)
Check "Matching pin accepted" ($v.Invoke($client, @($null, $cert, $null, $none, $thumb)))
Check "Different pin rejected and recorded" (-not $v.Invoke($client, @($null, $cert, $null, $none, ("A" * 64))) -and $clientType.GetField("_rejectedCertificateThumbprint", $any).GetValue($client) -eq $thumb)
Check "Windows validation failure rejects even with matching pin" (-not $v.Invoke($client, @($null, $cert, $null, $chainErr, $thumb)))

$pinType = $asm.GetType($ns + "OpenMANET_CertificatePinStore")
$tmp = Join-Path $env:TEMP ("om-pins-" + [guid]::NewGuid().ToString("N") + ".settings")
$ctor = $pinType.GetConstructor([type[]]@([string]))
$pins = $ctor.Invoke([object[]]@([string]$tmp)); $pins.Set("10.41.0.1", 8081, $thumb)
$pins2 = $ctor.Invoke([object[]]@([string]$tmp)); $pins2.Load()
Check "Pin store saves, reloads, keyed per host:port" ($pins2.Find("10.41.0.1", 8081) -eq $thumb -and $pins2.Find("10.41.0.1", 8443) -eq $null)
$fmt = $pinType.GetMethod("Format").Invoke($null, @($thumb))
Check "Fingerprint formats as AA:BB:..., normalizes back" ($fmt.Length -eq 95 -and $pinType.GetMethod("NormalizeThumbprint").Invoke($null, @($fmt)) -eq $thumb)
Check "Invalid fingerprint rejected" ($pinType.GetMethod("NormalizeThumbprint").Invoke($null, @("xyz")) -eq $null)
$pins2.Remove("10.41.0.1", 8081) | Out-Null; $pins3 = $ctor.Invoke([object[]]@([string]$tmp)); $pins3.Load()
Check "Forget removes the pin" ($pins3.Find("10.41.0.1", 8081) -eq $null)
Remove-Item $tmp -Confirm:$false

# WinTAK's global callback: trusts the node host only, and needs an HttpWebRequest sender.
Add-Type -ReferencedAssemblies System -TypeDefinition @"
using System.Net; using System.Net.Security; using System.Security.Cryptography.X509Certificates;
public static class FakeWinTakTrust {
  public static int Calls;
  public static bool Check(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors) {
    Calls++; HttpWebRequest r = sender as HttpWebRequest; return r != null && r.RequestUri.Host == "10.41.0.1";
  }
}
"@
$saved = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = [System.Net.Security.RemoteCertificateValidationCallback][FakeWinTakTrust].GetMethod("Check").CreateDelegate([System.Net.Security.RemoteCertificateValidationCallback])
$msgNode = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "https://10.41.0.1:8081/auth/login")
$msgOther = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "https://203.0.113.9:8081/auth/login")
$client2 = [Activator]::CreateInstance($clientType, $true)
Check "Self-signed cert WinTAK trusts is accepted and pinned (first use)" ($v.Invoke($client2, @($msgNode.psobject.BaseObject, $cert, $null, $chainErr, $null)) -and [FakeWinTakTrust]::Calls -ge 1 -and $clientType.GetProperty("ServerCertificateThumbprint").GetValue($client2) -eq $thumb)
Check "WinTAK-trusted cert still must match the pin" (-not $v.Invoke($client2, @($msgNode.psobject.BaseObject, $cert, $null, $chainErr, ("B" * 64))))
Check "Cert WinTAK rejects stays rejected" (-not $v.Invoke($client2, @($msgOther.psobject.BaseObject, $cert, $null, $chainErr, $null)))
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = $saved
Check "No host callback: untrusted cert rejected" (-not $v.Invoke($client2, @($msgNode.psobject.BaseObject, $cert, $null, $chainErr, $null)))

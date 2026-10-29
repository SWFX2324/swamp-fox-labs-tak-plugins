# Stale data: cached endpoints age out and lost contact marks nodes stale (2.1.0.2).
param([Parameter(Mandatory = $true)][string]$Dll, [string]$WinTakDir = "C:\Program Files\WinTAK")
. (Join-Path $PSScriptRoot "Common.ps1") -Dll $Dll -WinTakDir $WinTakDir

$tmp = New-TempDir
$plugin = Use-TempPluginSettings $tmp

# Minimal fake OpenMANET API (legacy HTTP paths) on loopback.
Add-Type -TypeDefinition @"
using System; using System.Collections.Generic; using System.IO; using System.Net; using System.Net.Sockets; using System.Text; using System.Threading;
public class FakeOpenManet {
  public HashSet<string> Failing = new HashSet<string>();
  public int Port; TcpListener _l; volatile bool _stop;
  public void Start() { _l = new TcpListener(IPAddress.Loopback, 0); _l.Start(); Port = ((IPEndPoint)_l.LocalEndpoint).Port; new Thread(Loop) { IsBackground = true }.Start(); }
  public void Stop() { _stop = true; _l.Stop(); }
  void Loop() { while (!_stop) { try { var c = _l.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(_ => Handle(c)); } catch { return; } } }
  void Handle(TcpClient c) {
    using (c) { var s = c.GetStream(); var head = new StringBuilder(); int b; int len = 0;
      while ((b = s.ReadByte()) >= 0) { head.Append((char)b); if (head.Length >= 4 && head.ToString().EndsWith("\r\n\r\n")) break; }
      string h = head.ToString(); string path = h.Split(' ')[1];
      foreach (var line in h.Split(new[] {"\r\n"}, StringSplitOptions.None)) if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) len = int.Parse(line.Substring(15).Trim());
      for (int i = 0; i < len; i++) s.ReadByte();
      string body; int code = 200;
      string key = path.Substring(path.LastIndexOf('/') + 1);
      if (Failing.Contains(key) || Failing.Contains("*")) { code = 500; body = "{}"; }
      else if (path.EndsWith("auth/login")) body = "{\"token\":\"test-token-123456\"}";
      else if (key == "ListNodes") body = "{\"nodes\":[{\"mac\":\"02:00:5e:10:00:00\",\"hostname\":\"Node0\",\"ipaddr\":\"10.41.0.1\"}]}";
      else if (key == "GetMeshTopology") body = "{\"topology\":{\"selfMac\":\"02:00:5e:10:00:01\",\"selfHostname\":\"Node1\",\"algorithm\":\"BATMAN_V\",\"nodes\":[{\"mac\":\"02:00:5e:10:00:01\",\"hostname\":\"Node1\",\"isSelf\":true,\"hopsFromSelf\":0},{\"mac\":\"02:00:5e:10:00:00\",\"hostname\":\"Node0\",\"hopsFromSelf\":1}],\"edges\":[]}}";
      else if (key == "ListMeshNeighbors") body = "{\"neighbors\":[{\"neighbor\":\"Node0.wlh0\",\"hardwareAddress\":\"02:00:5e:10:00:00\",\"signal\":-40,\"signalStrength\":-40,\"lastSeen\":120,\"throughput\":30000}]}";
      else body = "{}";
      byte[] payload = Encoding.UTF8.GetBytes(body);
      byte[] resp = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + (code == 200 ? " OK" : " Internal Server Error") + "\r\nContent-Type: application/json\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n");
      s.Write(resp, 0, resp.Length); s.Write(payload, 0, payload.Length); s.Flush(); }
  }
}
"@
$server = New-Object FakeOpenManet; $server.Start()
$clientType = $asm.GetType($ns + "OpenMANET_Client"); $client = [Activator]::CreateInstance($clientType, $true)
function Poll { return ,($clientType.GetMethod("ListNodesAsync").Invoke($client, @([System.Threading.CancellationToken]::None)).GetAwaiter().GetResult()) }
function Node0($list) { return $list | Where-Object { $_.Name -eq "Node0" } }
function Age($field) { $clientType.GetField($field, $any).SetValue($client, [Nullable[DateTimeOffset]][DateTimeOffset]::UtcNow.AddSeconds(-60)) }
try {
  $clientType.GetMethod("LoginAsync").Invoke($client, @("127.0.0.1", $server.Port, "operator", "password1", $true, [System.Threading.CancellationToken]::None)).GetAwaiter().GetResult() | Out-Null
  $n = Node0 (Poll)
  Check "Healthy poll: Node0 Online (routed + live link)" ($n.IsOnline -and $n.IsRouted -and $n.HasLiveLink)

  $server.Failing.Add("GetMeshTopology") | Out-Null; $server.Failing.Add("ListMeshNeighbors") | Out-Null
  $n = Node0 (Poll)
  Check "Topology+neighbors failing, cache fresh: still Online" ($n.IsOnline)
  Age "_topologyFetchedUtc"; Age "_neighborsFetchedUtc"
  $n = Node0 (Poll)
  Check "Same, cache older than 45 s: Node0 no longer Online" (-not $n.IsOnline -and -not $n.IsRouted -and -not $n.HasLiveLink)
  Check "Partial failure still reported per endpoint" ($client.LastRefreshFailures.ContainsKey("Topology") -and $client.LastRefreshFailures.ContainsKey("Link quality"))

  $server.Failing.Clear(); $n = Node0 (Poll)
  Check "Endpoints recover: Online again" ($n.IsOnline)

  $server.Failing.Add("*") | Out-Null
  $threw = $false; try { Poll | Out-Null } catch { $threw = $true }
  Check "Every endpoint failing: poll throws (lost contact), no cached result" ($threw)
} finally { $server.Stop() }

# Pane: after 45 s of failed polls, nodes are marked unreachable.
$ctlType = $asm.GetType($ns + "OpenMANET"); $ctl = [Activator]::CreateInstance($ctlType)
function F($name) { return ,($ctlType.GetField($name, $any).GetValue($ctl)) }
$nodeType = $asm.GetType($ns + "OpenMANET_NodeInfo")
$list = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($nodeType))
$a = [Activator]::CreateInstance($nodeType, $true); $a.Name = "Node0"; $a.IsOnline = $true; $a.IsRouted = $true; $a.HasLiveLink = $true; $list.Add($a)
$b = [Activator]::CreateInstance($nodeType, $true); $b.Name = "Node1"; $b.IsOnline = $true; $b.IsSelf = $true; $list.Add($b)
$ctlType.GetField("_lastNodes", $any).SetValue($ctl, $list)
(F "NodesListBox").ItemsSource = $list
$ctlType.GetMethod("UpdateConnectionState", $any).Invoke($ctl, @($true))
$ctlType.GetMethod("MarkNodesUnreachable", $any).Invoke($ctl, @())
Check "Pane: all nodes Stale, 0 online, flagged unreachable" (-not $a.IsOnline -and -not $b.IsOnline -and $a.StatusDisplay -eq "Stale" -and (F "NodeCountText").Text -eq "0" -and (F "_nodesMarkedUnreachable"))
$health = $asm.GetType($ns + "OpenMANET_PluginHost").GetProperty("Health").GetValue($null)
$events = F "_meshEvents"
Check "Pane: critical finding, red badge, lost-contact event" ("$health" -eq "Critical" -and $events[0].Message -like "Lost contact*")
Remove-Item -Recurse -Force $tmp -Confirm:$false

<#
.SYNOPSIS
    Regenerates the README screenshots by rendering the built plugin offscreen with sample data.
.EXAMPLE
    .\build.ps1
    .\docs\render-screenshots.ps1
#>
param(
    [string]$Dll = (Join-Path $PSScriptRoot "..\src\bin\Release\WinTAK_OpenMANET_Plugin.dll"),
    [string]$OutDir = (Join-Path $PSScriptRoot "screenshots"),
    [string]$WinTakDir = "C:\Program Files\WinTAK"
)
# Renders README screenshots of the OpenMANET pane with sample (fictional) mesh data.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
foreach ($dep in "WinTak.Framework.dll", "Prism.dll", "Prism.Wpf.dll", "Prism.Mef.Wpf.dll") {
    [System.Reflection.Assembly]::LoadFrom((Join-Path $WinTakDir $dep)) | Out-Null
}
$asm = [System.Reflection.Assembly]::LoadFrom($Dll)
$ns = "WinTAK_OpenMANET_Plugin."
$ctlType = $asm.GetType($ns + "OpenMANET")
$flags = [System.Reflection.BindingFlags]"NonPublic,Public,Instance"
$ctl = [Activator]::CreateInstance($ctlType)
function F($name) { return ,($ctlType.GetField($name, $flags).GetValue($ctl)) }
function SetF($name, $value) { $ctlType.GetField($name, $flags).SetValue($ctl, $value) }
function M($name, [object[]]$a) { $ctlType.GetMethod($name, $flags).Invoke($ctl, $a) }
function New-Obj($typeName, [hashtable]$props) {
    $t = $asm.GetType($ns + $typeName)
    $o = [Activator]::CreateInstance($t, $true)
    foreach ($k in $props.Keys) { $t.GetProperty($k).SetValue($o, $props[$k]) }
    return $o
}
function New-Nested($typeName, [hashtable]$props) {
    $t = $ctlType.GetNestedType($typeName, [System.Reflection.BindingFlags]::NonPublic)
    $o = [Activator]::CreateInstance($t, $true)
    foreach ($k in $props.Keys) { $t.GetProperty($k).SetValue($o, $props[$k]) }
    return $o
}
$now = [DateTimeOffset]::UtcNow
function ToInt([int]$v) { [Nullable[int]]$v }

# Settings shown in the Settings tab: sample values, not this machine's saved ones.
(F "GatewayHostTextBox").Text = "10.41.0.1"
(F "GatewayPortTextBox").Text = "8081"
(F "UsernameTextBox").Text = "operator"

# --- Nodes -------------------------------------------------------------------------------
$nodeType = $asm.GetType($ns + "OpenMANET_NodeInfo")
$nodes = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($nodeType))
$common = { param($h) $h["LastSuccessfulPollUtc"] = [Nullable[DateTimeOffset]]$now; $h }
$nodes.Add((New-Obj "OpenMANET_NodeInfo" (& $common @{ Name = "Node1"; Address = "10.41.0.2"; MacAddress = "02:00:5E:10:00:01"; IsOnline = $true; IsRouted = $true; IsSelf = $true; HopsFromSelf = (ToInt 0); RouteInterface = "wlh0"; Segment = "local"; DirectLinkCount = 2 })))
$nodes.Add((New-Obj "OpenMANET_NodeInfo" (& $common @{ Name = "Node0"; Callsign = "BASE"; Address = "10.41.0.1"; MacAddress = "02:00:5E:10:00:00"; IsOnline = $true; IsRouted = $true; HasLiveLink = $true; IsGateway = $true; HopsFromSelf = (ToInt 1); RouteInterface = "wlh0"; Segment = "local"; Rssi = (ToInt -48); CurrentRssi = (ToInt -47); ThroughputKbps = (ToInt 32400); LinkAgeMilliseconds = [Nullable[long]]180; NeighborIdentifier = "Node0.wlh0"; LinkHardwareAddress = "02:00:5E:20:00:00"; DirectLinkCount = 1; HasDirectLinkMetrics = $true; GossipAgeSeconds = (ToInt 4) })))
$nodes.Add((New-Obj "OpenMANET_NodeInfo" (& $common @{ Name = "Node2"; Callsign = "RELAY 2"; Address = "10.41.0.3"; MacAddress = "02:00:5E:10:00:02"; IsOnline = $true; IsRouted = $true; HasLiveLink = $true; HopsFromSelf = (ToInt 1); RouteInterface = "wlh0"; Segment = "local"; Rssi = (ToInt -64); CurrentRssi = (ToInt -66); ThroughputKbps = (ToInt 18900); LinkAgeMilliseconds = [Nullable[long]]420; NeighborIdentifier = "Node2.wlh0"; LinkHardwareAddress = "02:00:5E:20:00:02"; DirectLinkCount = 1; HasDirectLinkMetrics = $true; GossipAgeSeconds = (ToInt 6) })))
$nodes.Add((New-Obj "OpenMANET_NodeInfo" (& $common @{ Name = "Node3"; Address = "10.41.0.4"; MacAddress = "02:00:5E:10:00:03"; IsOnline = $true; IsRouted = $true; HopsFromSelf = (ToInt 2); RouteInterface = "wlh0"; Segment = "local"; GossipAgeSeconds = (ToInt 9) })))
$nodes.Add((New-Obj "OpenMANET_NodeInfo" (& $common @{ Name = "Node4"; Address = "10.41.0.5"; MacAddress = "02:00:5E:10:00:04"; IsDiscovering = $true; Segment = "local" })))

# --- Topology ----------------------------------------------------------------------------
function TN($host_, $mac, $hops, $self, $gw, $age) { New-Obj "OpenMANET_TopologyNodeInfo" @{ Hostname = $host_; MacAddress = $mac; HopsFromSelf = $hops; IsSelf = $self; IsGateway = $gw; RouteInterface = "wlh0"; Segment = "local"; GossipAgeSeconds = $age } }
function TE($from, $to, $fromName, $toName, $metric, $onPath) { New-Obj "OpenMANET_TopologyEdgeInfo" @{ FromMac = $from; ToMac = $to; FromName = $fromName; ToName = $toName; Metric = [double]$metric; IsOnMyPath = $onPath } }
$topo = New-Obj "OpenMANET_TopologyInfo" @{ SelfMac = "02:00:5E:10:00:01"; SelfHostname = "Node1"; Algorithm = "BATMAN_V"; CollectedAtUtc = [Nullable[DateTimeOffset]]$now; GossipPublished = 4; GossipTotal = 4 }
$tn0 = TN "Node0" "02:00:5E:10:00:00" 1 $false $true 4; $tn0.Callsign = "BASE"
$tn2 = TN "Node2" "02:00:5E:10:00:02" 1 $false $false 6; $tn2.Callsign = "RELAY 2"
$topo.Nodes.Add((TN "Node1" "02:00:5E:10:00:01" 0 $true $false 0)); $topo.Nodes.Add($tn0); $topo.Nodes.Add($tn2); $topo.Nodes.Add((TN "Node3" "02:00:5E:10:00:03" 2 $false $false 9))
$topo.Edges.Add((TE "02:00:5E:10:00:01" "02:00:5E:10:00:00" "Node1" "BASE" 32.4 $true))
$topo.Edges.Add((TE "02:00:5E:10:00:01" "02:00:5E:10:00:02" "Node1" "RELAY 2" 18.9 $false))
$topo.Edges.Add((TE "02:00:5E:10:00:02" "02:00:5E:10:00:03" "RELAY 2" "Node3" 12.6 $false))

# --- System ------------------------------------------------------------------------------
$sys = New-Obj "OpenMANET_SystemInfo" @{ Hostname = "Node1"; Model = "RPI RPI4-MM6108 (SPI)"; Firmware = "OpenMANET 24.10 1.8.0"; Kernel = "6.6.138"; Architecture = "aarch64"; Uptime = [Nullable[TimeSpan]][TimeSpan]::FromMinutes(222); DeviceTimeUtc = [Nullable[DateTimeOffset]]$now; CpuLoadPercent = 6.4; MemoryTotalBytes = [long]8160000000; MemoryUsedBytes = [long]199000000; OverlayTotalBytes = [long]4230000000; OverlayUsedBytes = [long]142400000; CpuTemperatureCelsius = 58.4; CollectedAtUtc = $now }
function NIF($name, $display, $state, $detail, $rx, $tx) { New-Obj "OpenMANET_NetworkSummaryInfo" @{ InterfaceName = $name; DisplayName = $display; State = $state; Detail = $detail; RxBytes = [long]$rx; TxBytes = [long]$tx } }
$sys.NetworkEntries.Add((NIF "wlh0" "HaLow Mesh" "CONNECTED" "802.11ah mesh point" 412000000 98000000))
$sys.NetworkEntries.Add((NIF "bat0" "BATMAN" "CONNECTED" "Mesh routing interface" 388000000 91000000))
$sys.NetworkEntries.Add((NIF "br-ahwlan" "LAN" "CONNECTED" "Local access network" 76000000 142000000))
$sys.NetworkEntries.Add((NIF "eth0" "WAN" "DISCONNECTED" "Cable unplugged" 0 0))

# --- Feed it through the control's own update paths ---------------------------------------
M "UpdateConnectionState" @($true)
M "UpdateTopology" (,[object]$topo)
M "UpdateSystemDetails" (,[object]$sys)
M "UpdateNodeEvents" (,[object]$nodes)
SetF "_lastNodes" $nodes
(F "NodesListBox").ItemsSource = $nodes
(F "LinkQualityNodeComboBox").ItemsSource = $nodes
(F "NodeCountText").Text = "4"; (F "NodeTotalText").Text = "of 5"
(F "LastPollText").Text = (Get-Date).ToString("HH:mm:ss")
SetF "_meshInterfaceAvailable" ([Nullable[bool]]$true)

$events = F "_meshEvents"
foreach ($e in @(
    @("INFO", "Connected to OpenMANET gateway 10.41.0.1:8081 using trusted HTTPS."),
    @("INFO", "Mesh baseline established with 4 nodes."),
    @("INFO", "Node4 joined the mesh."),
    @("WARNING", "RELAY 2 RSSI degraded below -75 dBm."),
    @("INFO", "RELAY 2 RSSI recovered to -72 dBm or better."),
    @("INFO", "Node3 route established."))) { M "AddMeshEvent" @($e[0], $e[1], [Enum]::Parse($asm.GetType($ns + "OpenMANET_EventCategory"), "None")) }

# Charts: 60 s of bandwidth/throughput samples, 60 min of trends for the selected node.
$bw = F "_bandwidthSamples"; $tp = F "_throughputSamples"
for ($i = 12; $i -ge 0; $i--) {
    $t = $now.AddSeconds(-5 * $i)
    $bw.Add((New-Nested "BandwidthSample" @{ CapturedAtUtc = $t; MegabitsPerSecond = 18.9 + 0.6 * [Math]::Sin($i / 2.0) }))
    $tp.Add((New-Nested "ThroughputSample" @{ CapturedAtUtc = $t; RxMegabitsPerSecond = 2.4 + 1.6 * [Math]::Abs([Math]::Sin($i / 1.7)); TxMegabitsPerSecond = 0.8 + 0.5 * [Math]::Abs([Math]::Cos($i / 2.3)) }))
}
SetF "_currentBandwidthMbps" ([Nullable[double]]18.9)
SetF "_currentThroughputMbps" ([Nullable[double]]3.6)
(F "BandwidthCurrentValueText").Text = "18.90"; (F "ThroughputCurrentValueText").Text = "3.60"
(F "BandwidthEmptyState").Visibility = "Collapsed"; (F "ThroughputEmptyState").Visibility = "Collapsed"
M "UpdateUtilization" @()

$trendType = $ctlType.GetNestedType("NodeTrendSample", [System.Reflection.BindingFlags]::NonPublic)
$trend = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($trendType))
for ($i = 59; $i -ge 0; $i--) {
    $trend.Add((New-Nested "NodeTrendSample" @{ CapturedAtUtc = $now.AddMinutes(-$i); Rssi = [Nullable[double]](-48 - 3 * [Math]::Sin($i / 6.0)); BandwidthMbps = [Nullable[double]](32.4 - 2 * [Math]::Abs([Math]::Sin($i / 9.0))); Hops = [Nullable[double]]1; AgeSeconds = [Nullable[double]](0.2 + 0.3 * [Math]::Abs([Math]::Sin($i / 4.0))) }))
}
(F "_nodeTrendHistory")["mac:02:00:5E:10:00:00"] = $trend

$w = New-Object System.Windows.Window
$w.Width = 480; $w.Height = 1040; $w.Content = $ctl; $w.WindowStyle = "None"; $w.ShowInTaskbar = $false; $w.Left = -3000; $w.Top = 0
$w.Show()
(F "NodesListBox").SelectedItem = $nodes[1]
M "UpdateHealthFindings" @()
$tabs = F "MainTabControl"
function Shoot($index, $name, $height) {
    $tabs.SelectedIndex = $index
    $w.Height = $height
    $w.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::ApplicationIdle)
    $ctl.UpdateLayout()
    M "RenderBandwidthChart" @(); M "RenderThroughputChart" @(); M "RenderNodeTrends" @(); M "RenderTopology" @()
    $ctl.UpdateLayout()
    $scale = 2
    $pw = [int]($ctl.ActualWidth * $scale); $ph = [int]($ctl.ActualHeight * $scale); $dpi = [double](96 * $scale)
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($pw, $ph, $dpi, $dpi, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($ctl)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $fs = [System.IO.File]::Create((Join-Path $OutDir "$name.png")); $enc.Save($fs); $fs.Close()
}
Shoot 0 "dashboard" 1040
Shoot 1 "topology" 1040
Shoot 2 "nodes" 1040
Shoot 3 "system" 1040
Shoot 4 "settings" 1040
$w.Close()
"ok"

# OpenMANET for WinTAK

[![Latest release](https://img.shields.io/github/v/release/SWFX2324/swamp-fox-labs-tak-plugins?label=latest&color=4C9AFF)](https://github.com/SWFX2324/swamp-fox-labs-tak-plugins/releases/latest)

A WinTAK plugin for monitoring an OpenMANET HaLow mesh from a WinTAK client. It shows mesh nodes, link quality, topology, gateway health and mesh events in a docked pane.

- **Dashboard:** nodes online, mesh bandwidth, throughput and utilization, plus the node list and a mesh event log.
- **Topology:** a live map of nodes and links from the connected node, including BLOS links.
- **Nodes:** per-node link quality, RSSI, routing details, 60-minute trends, and optional vanity callsigns shown in place of hostnames.
- **Shared callsigns:** opt in under Settings → Callsigns to share your callsigns with other operators running the plugin over your TAK network, and to use theirs. Changes go out right away and are repeated every minute; turning on shared callsigns asks other operators for theirs, so names appear within seconds. Your own callsigns always take precedence. Between other operators, the one connected to a node has the final say on its name, so nobody can rename your node for everyone else; anyone can still name nodes nobody is connected to, such as relays. Turning sharing off withdraws what you shared. CSV export and import still work for teams that prefer files.
- **System:** health findings, CPU, memory, storage and temperature, and network interfaces on the connected node.
- **Alerts:** WinTAK notifications when nodes go stale or come back, links weaken, gateway health changes, or the connection drops. Pick which in Settings. The OpenMANET toolbar icon shows a green, amber or red health dot, so you can see the mesh state with the pane closed.
- **Map:** each node is shown as the TAK user at it (within 25 m of the node's GPS by default), using that user's own WinTAK marker, and the plugin draws link lines between them colored by quality (green, amber, red; BLOS dashed orange). The plugin adds no icons of its own. Click a user's marker and choose the OpenMANET item in WinTAK's radial menu to open their node's details. Nodes with nobody at them aren't drawn unless you turn on **Draw radio icons for nodes nobody is at**. Nodes with COT OUT turned on also broadcast their own radio marker; the plugin hides it, so nodes don't add icons. Lines are drawn locally only and never sent to your TAK server. All of this is under Settings → Map.
- **Set a WinTAK callsign on every computer.** A computer without one appears to others as its Windows ID (S-1-5-21-…), and nodes aren't drawn as it.
- **Node GPS as your position (no plugin needed):** a laptop without its own GPS can use the connected node's fix through WinTAK's built-in support. On the node's GPS / GNSS page, turn on **NMEA OUT**. In WinTAK, open Settings → GPS Preferences and set GPS Type to **Network Provided GPS**. The node sends its fix to every device in its DHCP lease list, so your self marker follows the node that gave your computer its IP address. If one node hands out addresses for the whole mesh, every laptop gets that node's position, even while plugged into another node.
- **GPS checks:** Node details show the connected node's fix quality (HDOP and satellites used and in view). A poor fix (HDOP 5 or more, where positions can be tens of meters off) is flagged in health findings. If WinTAK's position keeps matching a different node's GPS, for example because that node hands out addresses for the whole mesh and sends its NMEA to every laptop, health findings say which node. Both are advisory and don't change the mesh health dot. Other nodes don't report fix quality over the mesh, so it's shown for the connected node only.
- **Certificate pinning:** the node's HTTPS certificate is remembered on first connect. If it ever changes, the plugin blocks the connection and shows both fingerprints before trusting the new one.
- **Auto-reconnect:** after a node reboot or an expired session, the plugin signs back in on its own, waiting a little longer between each attempt. It stops if the password is rejected.
- **Diagnostics:** redacted or full snapshots for troubleshooting, including a per-node status timeline (when each node was first seen, heard over RF, routed and online). Credentials are never included.

## Screenshots

<table>
  <tr>
    <td align="center"><img src="docs/screenshots/dashboard.png" width="230" alt="Dashboard tab: summary tiles, mesh node list with status labels, bandwidth chart"><br><b>Dashboard</b></td>
    <td align="center"><img src="docs/screenshots/topology.png" width="230" alt="Topology tab: mesh health tiles and a live map of nodes and links"><br><b>Topology</b></td>
    <td align="center"><img src="docs/screenshots/nodes.png" width="230" alt="Nodes tab: selected node with link quality, callsign editor and connection details"><br><b>Nodes</b></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/screenshots/system.png" width="230" alt="System tab: health findings, CPU, memory, storage and network interfaces"><br><b>System</b></td>
    <td align="center"><img src="docs/screenshots/settings.png" width="230" alt="Settings tab: connection settings and diagnostic snapshot export"><br><b>Settings</b></td>
    <td></td>
  </tr>
</table>

<sub>Screenshots use sample data. Node names, callsigns, addresses and MACs are illustrative.</sub>

Node status:

| Status | Meaning |
| --- | --- |
| Online | Reachable over a route, or heard on a direct RF link within the last 15 s |
| Discovering | Listed by the mesh but not reachable yet, usually right after a node boots |
| Stale | Was online, but has gone quiet (a direct neighbor unheard for over 30 s) |

## Requirements

- WinTAK 5.7 or 5.8 (x64). See [Supported WinTAK versions](#supported-wintak-versions).
- An OpenMANET node reachable from the WinTAK machine. HTTPS on port 8081 is the default; legacy HTTP on port 8087 is available as an opt-in.

## Supported WinTAK versions

| WinTAK | Status |
| --- | --- |
| 5.8 (5.8.0.149) | Supported: field-tested, and builds and passes the offline tests against the 5.8 SDK |
| 5.7 (5.7.0.144, 5.7.0.149) | Supported: release packages are built and tested against the 5.7 SDK |

One package works in both; the `5.7.0.144` in its file name is the SDK it was built against, not a limit.

Policy: the plugin supports the current WinTAK release and the one before it. When a new WinTAK ships, a compatible build follows within about 30 days of its SDK becoming available, and the oldest version is dropped at the next plugin release, announced in the release notes beforehand. If a WinTAK release ever breaks compatibility, each supported version gets its own package. Fixes go into the latest plugin release only.

## Install

1. Download `OpenMANET-<version>-5.7.0.144.wpk` from the repository's Releases page.
2. Close WinTAK, then install the `.wpk` package in WinTAK.
3. Start WinTAK, open the OpenMANET pane, then enter the node address and credentials under **Settings**.

When upgrading, remove the old `WinTAK_OpenMANET_Plugin.dll` from `%APPDATA%\WinTAK\Plugins` first, and restart WinTAK completely.

## Version history

Full notes and downloads are on the [Releases](https://github.com/SWFX2324/swamp-fox-labs-tak-plugins/releases) page.

| Version | Highlights |
| --- | --- |
| **2.2.2.0** | GPS checks: the connected node's fix quality (HDOP, satellites) in node details, a health finding for a poor fix, and a warning when WinTAK's position matches a different node's GPS. |
| 2.1.4.2 | The map uses only WinTAK's own markers: each node is shown as the TAK user at it, with quality-colored link lines between users; radio icons for unattended nodes are optional. Nodes' own COT OUT markers are hidden. Shared callsigns arrive within seconds and are repeated every minute; the operator connected to a node has the final say on its shared name. Link lines WinTAK restored from an earlier session are removed. README covers using the node's GPS as your WinTAK position. |
| 2.1.1.1 | Opt-in callsign sharing between operators over TAK, matched by hostname and every known MAC; click a node's map marker to open its details; a per-node status timeline in diagnostic snapshots. |
| 2.1.0.2 | Mesh nodes and quality-colored links on the WinTAK map from each node's GPS; HTTPS certificate pinning with change detection; GPS position in node details; nodes no longer stay Online on old data after the mesh goes quiet. |
| 2.0.8.0 | WinTAK notifications for node, link, gateway and connection events; a health dot on the toolbar icon; automatic reconnect after node reboots; callsign export/import. |
| 2.0.7.2 | New UI; Online as soon as a direct RF link is live; Discovering state; powered-off nodes go Stale in about 30 s; vanity callsigns; the connected node is listed. |
| 2.0.5.0 | Monitoring continues with the pane closed; 15-second polling, where one failed request no longer blanks the rest; steadier threshold warnings. |

## Build

You need the .NET SDK and a supported WinTAK installed (the build references its SDK assemblies). Release packages are built against the oldest supported WinTAK: when `C:\WinTAK-SDK\5.7.0.149` exists, `build.ps1` and `tests\Run-Tests.ps1` use it by default, otherwise the installed WinTAK. Point `-WinTakDir` at another version's folder to build or test against it; both scripts take the parameter.

To set up that folder without touching your installed WinTAK, unpack the 5.7 SDK installer and copy its files with an MSI administrative install:

```powershell
.\WinTAK-5.7.0.149-civ-sdk-installer-x64.exe /extract:"$env:TEMP\wt57"
msiexec /a "$env:TEMP\wt57\WinTAK_Civ_SDK_x64.msi" /qn TARGETDIR="C:\WinTAK-SDK\5.7.0.149"
```

```powershell
.\build.ps1                                # default WinTAK path: C:\Program Files\WinTAK
.\build.ps1 -WinTakDir "D:\WinTAK"         # custom install location
```

The package is written to `dist\OpenMANET-<version>-<sdk>.wpk`. Versions come from `src/Properties/AssemblyInfo.cs`.

After UI changes, regenerate the README screenshots with `.\docs\render-screenshots.ps1`. It renders the built plugin with sample data, so no real network details end up in the images.

## Tests

Offline tests load the built plugin and WinTAK's SDK assemblies into PowerShell; a small fake OpenMANET server stands in for a node. They don't need WinTAK running or a mesh attached, and they never touch your saved settings. Build first, then:

```powershell
.\tests\Run-Tests.ps1
```

It prints each check and exits with code 1 if any fail.

## Roadmap

Planned for 2.2.2:

- **Fallback node:** a list of node addresses, so the plugin moves to another reachable node when the connected one goes down.
- **Session recording:** export the event log, per-node RSSI and link quality, and GPS tracks (CSV or GPX) for after-action review and range testing.
- **Map extras:** distance labels on link lines and short track trails per node.

Cold boot: the 2.1.1 status timeline shows a node turns Online in the same poll the connected node first hears it on the radio, so the remaining delay (several minutes) is the node booting and joining the HaLow mesh, not the plugin.

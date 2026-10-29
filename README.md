<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="assets/swamp-fox-labs-logo-dark.svg"><img src="assets/swamp-fox-labs-logo-light.svg" width="420" alt="Swamp Fox Labs"></picture></p>

# Swamp Fox Labs TAK Plugins

Plugins for the TAK (Team Awareness Kit) ecosystem from Swamp Fox Labs.

## Plugins

| Plugin | Platform | Latest | What it does |
| --- | --- | --- | --- |
| [OpenMANET](wintak/OpenMANET) | WinTAK 5.7 and 5.8 (x64) | [![Latest release](https://img.shields.io/github/v/release/SWFX2324/swamp-fox-labs-tak-plugins?label=&color=4C9AFF)](https://github.com/SWFX2324/swamp-fox-labs-tak-plugins/releases/latest) | Monitors an OpenMANET HaLow mesh from inside WinTAK: node status, link quality, live topology, mesh nodes on the map, gateway health, WinTAK alerts and automatic reconnect. |

### OpenMANET for WinTAK

<img src="wintak/OpenMANET/docs/screenshots/dashboard.png" width="260" align="right" alt="OpenMANET dashboard in WinTAK, showing summary tiles and the mesh node list">

A docked WinTAK pane for operators running an OpenMANET mesh:

- **Dashboard:** nodes online, mesh bandwidth, throughput and utilization, plus the node list and an event log.
- **Topology:** a live map of nodes and links from the connected node, including BLOS links.
- **Nodes:** per-node RSSI, link quality, routing, 60-minute trends, and optional vanity callsigns that operators can share over TAK.
- **System:** health findings, CPU, memory, storage, temperature and interfaces on the connected node.
- **Map:** each node is shown as the TAK user at it, using WinTAK's own markers, with quality-colored link lines between them. Click a user's marker to open their node in the pane.
- **Alerts:** WinTAK notifications for node, link, gateway and connection changes, a health dot on the toolbar icon, and automatic reconnect.
- **Diagnostics:** redacted or full snapshots for troubleshooting. Credentials are never included.

See the [plugin README](wintak/OpenMANET/README.md) for node status meanings, requirements and build steps.

## Install

1. Download the `.wpk` from [Releases](https://github.com/SWFX2324/swamp-fox-labs-tak-plugins/releases). Each release lists its SHA-256 checksum.
2. Close WinTAK. If upgrading, delete the old plugin DLL from `%APPDATA%\WinTAK\Plugins`.
3. Install the `.wpk` package and restart WinTAK.

## Repository layout

```
wintak/                 WinTAK plugins
  OpenMANET/
    src/                C# / WPF source (.NET Framework 4.8)
    package/            .wpk manifest and icon
    build.ps1           builds the plugin and packages the .wpk
```

Built packages are published on the Releases page rather than committed to the repository.

## Building from source

Each plugin builds with the .NET SDK against a local WinTAK install. For OpenMANET:

```powershell
cd wintak\OpenMANET
.\build.ps1
```

## Security

Please don't report security issues in public issues. See [SECURITY.md](SECURITY.md) for how to report them privately and for an overview of how the plugins handle credentials and network traffic.

## License

Released under the [MIT License](LICENSE). The license covers this repository's code; WinTAK and its SDK are licensed separately by their owners.

## Disclaimer

This is an independent project. It is not affiliated with or endorsed by the TAK Product Center, the developers of WinTAK, or the OpenMANET project. TAK, WinTAK and ATAK are names of their respective owners.

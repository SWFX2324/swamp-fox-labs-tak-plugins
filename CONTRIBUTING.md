# Contributing

Thanks for helping. Bug reports, field results and fixes are all useful, and you don't need to write code to contribute.

By taking part, you agree to follow the [code of conduct](CODE_OF_CONDUCT.md).

**Issues and pull requests are public.** Leave out real node addresses, MAC addresses, hostnames, callsigns, positions, credentials and details of real operations. Made-up names and addresses show a problem just as well.

## Reporting a problem or an idea

- **Something's broken or the mesh looks wrong:** open a [bug report](https://github.com/SWFX2324/swamp-fox-labs-tak-plugins/issues/new?template=bug_report.yml).
- **You have an idea:** open a [feature request](https://github.com/SWFX2324/swamp-fox-labs-tak-plugins/issues/new?template=feature_request.yml).
- **You found a security problem:** follow [SECURITY.md](SECURITY.md) and don't open a public issue.

Problems in WinTAK itself, or in OpenMANET firmware, its web UI or its API, belong with those projects. If you're not sure where a problem comes from, open a bug report anyway.

### Field results

The plugin's offline tests use a fake node, so results from real meshes are especially valuable. If the plugin didn't match what your mesh was doing, open a bug report and include:

- Plugin and WinTAK versions
- OpenMANET firmware version, from the plugin's System tab
- Number of nodes, and how this computer connects to the mesh
- What the plugin showed, and what the node's web interface or the radios showed
- A redacted snapshot from **Settings → Export Redacted Support Snapshot**, if it helps

Never post the full operational snapshot. It contains real network details. Read the redacted one before you post it too.

## Before you write code

For anything bigger than a small fix, open an issue first so we can agree on the approach. The plugins are meant to stay focused and dependable in the field, so not every good idea will fit.

## Setting up

You need Windows, the .NET SDK and WinTAK 5.7 or 5.8 (x64). The build references WinTAK's SDK assemblies.

From `wintak\OpenMANET`, build the plugin and package:

```powershell
.\build.ps1
```

Then run the offline tests:

```powershell
.\tests\Run-Tests.ps1
```

The tests don't need WinTAK running or a mesh attached, and they never touch your saved settings. Both scripts take `-WinTakDir` to use a different WinTAK folder. The [plugin README](wintak/OpenMANET/README.md#build) explains how release builds use the oldest supported SDK, and how to set up that folder without touching your installed WinTAK.

## How the code is organized

Everything for the plugin is in `wintak/OpenMANET`:

- **`src/OpenMANET.xaml` and `.xaml.cs`:** the pane, including alerts and health findings.
- **`src/OpenMANET_Client.cs`:** the node API, plus the rules for Online, Discovering and Stale.
- **`src/OpenMANET_Security.cs`:** address checks, limits and validation of data from the node.
- **`src/OpenMANET_MapLayer.cs`:** map markers and link lines.
- **`src/OpenMANET_CallsignSharing.cs`:** callsign sharing over TAK.
- **`tests/`:** one `Test-*.ps1` file per area. `Run-Tests.ps1` picks up new files automatically.
- **`package/`:** the `.wpk` manifest and icon.

## Guidelines

**Security and privacy**
- Treat everything from a node or the TAK network as untrusted, and validate it before showing or storing it.
- Never write the password to disk, and keep credentials out of logs, notifications and snapshots.
- Never add a way to skip certificate validation or pinning.
- Anything new that sends data to other users must be opt-in. Map items stay local to this computer.
- Update `SECURITY.md` if a change affects what the plugin stores or sends.

**Compatibility**
- One package supports the current and previous WinTAK releases, so don't depend on anything only the newest one has.
- Other computers may run older plugins. Changes to callsign sharing events must keep working with them.

**Interface**
- Match the wording and code style around your change.
- After a UI change, regenerate the README screenshots with `.\docs\render-screenshots.ps1`. It uses sample data, so no real network details end up in the images.

**Repository**
- Never commit WinTAK's DLLs or installers, which are licensed separately, or built `.wpk` packages, which go on the Releases page.
- Prefer no new dependency when a small amount of code will do.

## Sending a pull request

1. Fork the repository and create a branch from `main`.
2. Keep the pull request to one change, and add tests for any logic you add or fix.
3. Run `.\build.ps1` and `.\tests\Run-Tests.ps1`. These don't run automatically on pull requests, so include the result.
4. Fill in the pull request template.

Leave version numbers alone. The maintainer sets them when preparing a release, and may field-test a change on a real mesh first.

Contributions are released under the project's [MIT license](LICENSE).

# Security Policy

## Supported versions

Security fixes go into the latest release only. Please update to the newest version on the [Releases](https://github.com/SWFX2324/swamp-fox-labs-tak-plugins/releases) page before reporting.

| Plugin | Supported | Not supported |
| --- | --- | --- |
| OpenMANET for WinTAK | 2.0.7.x (latest) | Anything older |

## Reporting a vulnerability

**Please don't open a public issue for security problems.**

Report them privately through GitHub instead:

1. Open the repository's **Security** tab.
2. Click **Report a vulnerability**.
3. Include the plugin and version, what you found, steps to reproduce, and what an attacker could do with it.

You should get an acknowledgement within 7 days. Once the issue is confirmed, a fix ships in a new release, and you'll be credited in the release notes unless you'd rather not be.

Please leave out real network details such as node addresses, MACs, hostnames or credentials. If a diagnostic snapshot helps, use the **redacted** support snapshot, not the full operational one.

### Scope

In scope: the plugin code and packages in this repository.

Out of scope, so please report these to their own maintainers:
- WinTAK or other TAK clients
- OpenMANET firmware, its web UI or its API
- Problems that need someone who already has administrator access to the WinTAK machine

## How OpenMANET for WinTAK handles security

**Network**
- Connects over **HTTPS by default**, using normal Windows certificate validation. The node's certificate must be trusted by Windows; there is no option to skip validation.
- **Legacy HTTP** (port 8087) is off by default. Enabling it takes an explicit checkbox plus a confirmation warning, and it only connects to private or local network addresses.
- Doesn't follow redirects, use cookies, or go through a system proxy. The gateway address must be a plain hostname or IP address, with no paths or embedded credentials.
- Response sizes, item counts and request times are capped, and data from the node is validated before it's shown.

**Credentials**
- The **password is never stored**. It's cleared from the form as soon as you connect.
- The session token is kept in memory only. On disconnect and when WinTAK shuts down, the plugin asks the node to sign the session out.
- Saved on disk, in `%APPDATA%\WinTAK\Plugins\WinTAK_OpenMANET_Plugin\` in plain text: the gateway address, port and username (`connection.settings`), plus any callsigns you assign (`callsigns.settings`). Nothing secret is saved there.

**Diagnostics**
- Snapshots never include credentials.
- The default **redacted support snapshot** replaces hostnames, addresses and MACs with placeholders and leaves out process details. It's the one to share.
- The **full operational snapshot** contains real network details. Treat it as sensitive.

**Known limitations**
- Legacy HTTP mode sends the username, password, session token and telemetry unencrypted. Use it only on a network you trust.
- The plugin relies on Windows to trust the node's HTTPS certificate. It doesn't pin certificates yet.

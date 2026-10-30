# Security Policy

## Supported versions

Security fixes go into the latest release only. Please update to the newest version on the [Releases](https://github.com/SWFX2324/swamp-fox-labs-tak-plugins/releases) page before reporting.

| Plugin | Supported | Not supported |
| --- | --- | --- |
| OpenMANET for WinTAK | 2.2.x (latest) | Anything older |

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
- Connects over **HTTPS by default**. The node's certificate must be trusted by Windows, or by WinTAK's own certificate validation (the same check WinTAK applies to TAK servers: certificates that chain to WinTAK's configured trust store, or that were accepted in WinTAK for that host). There is no option to skip validation.
- **Certificate pinning:** on the first HTTPS connect to a gateway, the plugin records the SHA-256 fingerprint of its certificate. Every later TLS handshake must present that same certificate in addition to passing the validation above. If it changes, the connection is blocked and both fingerprints are shown; the new certificate is trusted only if you confirm it. Automatic reconnect never accepts a changed certificate, and a change mid-session disconnects immediately. Pins can be cleared under Settings → Forget Certificate.
- **Map items** for mesh nodes and links are processed locally by WinTAK only. They're never sent to a TAK server or other clients, and they aren't archived; they're removed on disconnect and when WinTAK closes.
- **Callsign sharing over TAK** is off by default, in both directions. **Share my callsigns** broadcasts the callsigns you saved, with each node's MAC address and hostname and your TAK callsign, to everyone on your TAK network (server or mesh), every minute, whenever you change one, and when another plugin asks. **Use callsigns shared by other operators** sends a small request (your plugin's sender ID, no names or positions) when you turn it on or open the pane, so operators who share answer right away; each plugin answers at most once every 10 seconds. Turning it off broadcasts an empty list so other plugins drop your names. **Use callsigns shared by other operators** only fills in nodes you haven't named yourself; your own callsigns always win. Between other operators, a name from the operator connected to that node outranks everyone else's, then the newest name wins. That ranking prevents accidental overwrites only: the claim isn't verified. Received events are validated (allowed characters, field lengths, at most 256 names per operator and 32 operators, timestamps from the future clamped) and are kept out of WinTAK's map and stored data. Anyone who can send CoT on your TAK network can share names, so treat shared callsigns as labels, not as authenticated identities.
- **GPS checks** compare this computer's position from WinTAK with the positions nodes announce, in memory only, to warn when WinTAK seems to be using another node's GPS. Nothing is saved or sent.
- **Showing each node as the TAK user at it** reads the position reports WinTAK already receives from other TAK users, and this computer's own position from WinTAK. They're kept in memory only (at most 512, dropped when they go stale), compared with node positions, and never saved or sent anywhere.
- **Hiding nodes' own markers** only affects this computer's map. The plugin recognizes a node's COT OUT marker by its type (`a-f-G-U-U-S-R`), its `MANET Radio` group role and its UID (the node's hostname, usually with `-MANET`), and hides it for every node with that hostname the plugin has a position for. It doesn't change what the node sends or what other TAK users see.
- **Legacy HTTP** (port 8087) is off by default. Enabling it takes an explicit checkbox plus a confirmation warning, and it only connects to private or local network addresses.
- Doesn't follow redirects, use cookies, or go through a system proxy. The gateway address must be a plain hostname or IP address, with no paths or embedded credentials.
- Response sizes, item counts and request times are capped, and data from the node is validated before it's shown.

**Credentials**
- The **password is never written to disk**. It's cleared from the form as soon as you connect.
- With **automatic reconnect** on (the default), the password is kept in memory while you're connected, encrypted with Windows DPAPI for your Windows account, so the plugin can sign back in after a node reboot. It's wiped when you disconnect, turn the option off, the node rejects it, or WinTAK closes. Turn automatic reconnect off in Settings to keep no copy at all.
- The session token is kept in memory only. On disconnect and when WinTAK shuts down, the plugin asks the node to sign the session out.
- Saved on disk, in `%APPDATA%\WinTAK\Plugins\WinTAK_OpenMANET_Plugin\` in plain text: the gateway address, port and username (`connection.settings`), any callsigns you assign (`callsigns.settings`), callsigns other operators shared with you (`shared-callsigns.settings`), your alert, reconnect, map and sharing choices (`preferences.settings`), and pinned certificate fingerprints (`trusted-certificates.settings`). Nothing secret is saved there.
- WinTAK notifications carry the same text as the plugin's event log (node names, status and error messages, never credentials). The plugin removes its notifications from WinTAK's saved notification list when WinTAK closes.

**Diagnostics**
- Snapshots never include credentials.
- The default **redacted support snapshot** replaces hostnames, addresses and MACs with placeholders and leaves out process details. It's the one to share.
- The **full operational snapshot** contains real network details. Treat it as sensitive.

**Known limitations**
- Legacy HTTP mode sends the username, password, session token and telemetry unencrypted, and has no certificate to pin. Use it only on a network you trust.
- Pinning is trust on first use: it protects every connection after the first, so make the first connection on a network you trust.

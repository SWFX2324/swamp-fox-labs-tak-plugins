## What changed and why

<!-- Link the issue if there is one, such as "Closes #12". -->

## How you tested it

<!-- The WinTAK SDK and version you used, what you checked, and anything you couldn't test. Leave out real network details. -->

## Checklist

- [ ] `.\build.ps1` passes
- [ ] `.\tests\Run-Tests.ps1` passes
- [ ] No real addresses, MACs, hostnames, callsigns, positions or credentials in the code, tests, screenshots or this description

Delete any of these that don't apply:

- [ ] New or changed logic has tests in `wintak/OpenMANET/tests`
- [ ] Checked in WinTAK, on a real mesh if the change depends on one
- [ ] README screenshots regenerated with `.\docs\render-screenshots.ps1`, if the UI changed
- [ ] New data from a node or the TAK network is validated before it's shown or stored
- [ ] Changes to callsign sharing events still work with older plugins
- [ ] The plugin `README.md` and `SECURITY.md` still match what the plugin does

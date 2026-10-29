# Alerts, preferences, callsign export/import and the reconnect loop (2.0.8).
param([Parameter(Mandatory = $true)][string]$Dll, [string]$WinTakDir = "C:\Program Files\WinTAK")
. (Join-Path $PSScriptRoot "Common.ps1") -Dll $Dll -WinTakDir $WinTakDir

$tmp = New-TempDir
$plugin = Use-TempPluginSettings $tmp

# 1. WinTAK's notification log serializes List<Notification> with plugin types added.
$notifType = $asm.GetType($ns + "OpenMANET_Notification")
$baseType = [System.Reflection.Assembly]::LoadFrom((Join-Path $wt "WinTak.Framework.dll")).GetType("WinTak.Framework.Notifications.Notification")
$listType = [System.Collections.Generic.List``1].MakeGenericType($baseType)
$ser = New-Object System.Xml.Serialization.XmlSerializer($listType, [Type[]]@($notifType))
$n = [Activator]::CreateInstance($notifType); $n.Uid = "openmanet-test"; $n.Message = "Node0 became stale."; $n.StartTime = [DateTime]::UtcNow; $n.StaleTime = [DateTime]::UtcNow.AddHours(1)
$list = [Activator]::CreateInstance($listType); $list.Add($n)
$sw = New-Object System.IO.StringWriter; $ser.Serialize($sw, $list)
$back = $ser.Deserialize((New-Object System.IO.StringReader($sw.ToString())))
Check "Notification survives WinTAK log save/load" ($back.Count -eq 1 -and $back[0].Message -eq "Node0 became stale." -and $back[0].GetHeader() -eq "OpenMANET")

# 2-5 use a live control with every settings file redirected to the temp folder.
$ctlType = $asm.GetType($ns + "OpenMANET")
$ctl = [Activator]::CreateInstance($ctlType)
function F($name) { return ,($ctlType.GetField($name, $flags).GetValue($ctl)) }
function SetF($name, $v) { $ctlType.GetField($name, $flags).SetValue($ctl, $v) }
function M($name, [object[]]$a) { $ctlType.GetMethod($name, $flags).Invoke($ctl, $a) }
SetF "_settingsFilePath" (Join-Path $tmp "connection.settings")
$prefType = $asm.GetType($ns + "OpenMANET_Preferences")
$prefs = $prefType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string](Join-Path $tmp "preferences.settings")))
SetF "_preferences" $prefs
$storeType = $asm.GetType($ns + "OpenMANET_CallsignStore")
$store = $storeType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string](Join-Path $tmp "callsigns.settings")))
SetF "_callsigns" $store

# 2. DPAPI round trip and clearing.
M "RememberPasswordForReconnect" @("s3cret-pass")
$protected = F "_protectedPassword"
$plainBytes = [Text.Encoding]::UTF8.GetBytes("s3cret-pass")
$containsPlain = ([Text.Encoding]::UTF8.GetString($protected)).Contains("s3cret")
Check "Password encrypted in memory (no plaintext bytes)" ($protected -ne $null -and -not $containsPlain)
Check "Password decrypts for reconnect" ((M "RecallReconnectPassword" @()) -eq "s3cret-pass")
M "ClearReconnectPassword" @()
Check "Password cleared" ((F "_protectedPassword") -eq $null)
$prefs.AutoReconnect = $false; M "RememberPasswordForReconnect" @("x"); Check "Not kept when auto-reconnect is off" ((F "_protectedPassword") -eq $null); $prefs.AutoReconnect = $true

# 3. Preferences round trip.
$prefs.NotifyLinks = $false; $prefs.Save()
$prefs2 = $prefType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string](Join-Path $tmp "preferences.settings"))); $prefs2.Load()
Check "Preferences save and reload" ($prefs2.NotifyLinks -eq $false -and $prefs2.NotifyNodes -eq $true -and $prefs2.AutoReconnect -eq $true)
$catType = $asm.GetType($ns + "OpenMANET_EventCategory")
Check "Disabled category is not notified" (-not $prefs2.ShouldNotify([Enum]::Parse($catType, "Links")) -and $prefs2.ShouldNotify([Enum]::Parse($catType, "Nodes")) -and -not $prefs2.ShouldNotify([Enum]::Parse($catType, "None")))

# 4. Callsign export / import.
$store.Set("02:00:5E:10:00:00", "Node0", "BASE"); $store.Set($null, "Node3", "Relay/3")
$csv = Join-Path $tmp "export.csv"; $store.ExportTo($csv)
$other = $storeType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string](Join-Path $tmp "other.settings")))
$other.Set("02:00:5E:10:00:00", "Node0", "OLD NAME")
Add-Content -Path $csv -Value "zz,bad;name,???"
$skipped = 0; $imported = $other.ImportFrom($csv, [ref]$skipped)
Check "Import merges 2, replaces existing, skips 1 bad line" ($imported -eq 2 -and $skipped -eq 1 -and $other.Find([string[]]@("02:00:5e:10:00:00"), "x") -eq "BASE" -and $other.Find([string[]]@(), "node3") -eq "Relay/3")
Check "Export file has header and rows" ((Get-Content $csv)[0] -eq "mac,hostname,callsign" -and (Get-Content $csv).Count -eq 4)

# 5. Reconnect loop against an address that refuses connections, then stop it.
(F "GatewayHostTextBox").Text = "127.0.0.1"; (F "GatewayPortTextBox").Text = "9"; (F "UsernameTextBox").Text = "operator"
M "RememberPasswordForReconnect" @("s3cret-pass")
$hostType = $asm.GetType($ns + "OpenMANET_PluginHost")
$w = New-Object System.Windows.Window; $w.Content = $ctl; $w.Width = 480; $w.Height = 800; $w.Left = -3000; $w.WindowStyle = "None"; $w.ShowInTaskbar = $false; $w.Show()
$log = Join-Path $env:TEMP "om-reconnect-test.log"; Set-Content $log "start"
[System.Windows.Threading.Dispatcher]::CurrentDispatcher.add_UnhandledException({ param($sender, $e) [IO.File]::AppendAllText($log, "UNHANDLED: " + $e.Exception.ToString() + "`n"); $e.Handled = $true })
# Start it from a dispatcher callback, as WinTAK does, so awaits resume on the UI thread.
[System.Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([Action]{ M "StartReconnect" @() }) | Out-Null
Add-Content $log ("after start: " + (F "ConnectionStatusText").Text)
$deadline = [DateTime]::Now.AddSeconds(12)
while ([DateTime]::Now -lt $deadline) {
    $frame = New-Object System.Windows.Threading.DispatcherFrame
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([System.Windows.Threading.DispatcherPriority]::Background, [Action]{ $frame.Continue = $false }) | Out-Null
    [System.Windows.Threading.Dispatcher]::PushFrame($frame); Start-Sleep -Milliseconds 100
    Add-Content $log ((Get-Date).ToString("HH:mm:ss.fff") + " " + (F "ConnectionStatusText").Text)
}
$status = (F "ConnectionStatusText").Text
$health = $hostType.GetProperty("Health").GetValue($null)
Check "Reconnect retries with backoff ($status)" ((F "_reconnecting") -and $status -match "attempt [2-4]")
Check "Settings locked and button offers stop" ((F "ConnectButton").Content -eq "Stop Reconnecting" -and -not (F "ConnectionSettingsPanel").IsEnabled)
Check "Toolbar health is Critical while reconnecting" ("$health" -eq "Critical")
M "ConnectButton_Click" @($null, $null)
$health2 = $hostType.GetProperty("Health").GetValue($null)
Check "Stop ends loop, clears password, clears badge" (-not (F "_reconnecting") -and (F "_protectedPassword") -eq $null -and (F "ConnectButton").Content -eq "Connect" -and "$health2" -eq "None")
$w.Close()
Remove-Item -Recurse -Force $tmp -Confirm:$false

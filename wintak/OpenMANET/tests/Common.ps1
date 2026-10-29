# Shared setup for the offline tests, dot-sourced by each Test-*.ps1: loads WinTAK's SDK
# assemblies and the built plugin, and provides Check and a few reflection helpers.
param(
    [Parameter(Mandatory = $true)][string]$Dll,
    [string]$WinTakDir = "C:\Program Files\WinTAK"
)

$ErrorActionPreference = "Stop"
$wt = $WinTakDir
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.ComponentModel.Composition, System.Xml, System.Security, System.Net.Http
foreach ($dep in "WinTak.Framework.dll", "Prism.dll", "Prism.Wpf.dll", "Prism.Mef.Wpf.dll", "WinTak.Common.dll", "WinTak.Graphics.dll", "WinTak.CursorOnTarget.dll", "Spyglass.Graphics.dll", "WinTak.Display.dll") {
    [System.Reflection.Assembly]::LoadFrom((Join-Path $wt $dep)) | Out-Null
}
$asm = [System.Reflection.Assembly]::LoadFrom($Dll)
$ns = "WinTAK_OpenMANET_Plugin."
$any = [System.Reflection.BindingFlags]"NonPublic,Public,Instance,Static"
$flags = [System.Reflection.BindingFlags]"NonPublic,Public,Instance"
$hostType = $asm.GetType($ns + "OpenMANET_PluginHost")

# Run-Tests.ps1 counts lines ending in PASS or FAIL.
function Check($name, $ok) { "{0,-70} {1}" -f $name, $(if ($ok) { "PASS" } else { "FAIL" }) }

function New-TempDir {
    $dir = Join-Path $env:TEMP ("om-test-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory $dir | Out-Null
    return $dir
}

function New-PluginObject($typeName, [object[]]$ctorArgs) {
    $t = $asm.GetType($ns + $typeName)
    if ($ctorArgs) { return [Activator]::CreateInstance($t, $ctorArgs) }
    return [Activator]::CreateInstance($t, $true)
}

function New-Node([hashtable]$props) {
    $t = $asm.GetType($ns + "OpenMANET_NodeInfo"); $o = [Activator]::CreateInstance($t, $true)
    foreach ($k in $props.Keys) { $t.GetProperty($k).SetValue($o, $props[$k]) }
    return $o
}

function New-NodeList {
    return ,([Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($asm.GetType($ns + "OpenMANET_NodeInfo"))))
}

# Points the plugin-wide preferences and shared-callsign store at a temp folder, so tests never
# touch the real settings in %APPDATA%.
function Use-TempPluginSettings($dir) {
    $prefs = New-PluginObject "OpenMANET_Preferences" @([string](Join-Path $dir "preferences.settings"))
    $sharing = New-PluginObject "OpenMANET_CallsignSharing" @([string](Join-Path $dir "shared-callsigns.settings"), "test-local-uid")
    $hostType.GetField("_preferences", $any).SetValue($null, $prefs)
    $hostType.GetField("_callsignSharing", $any).SetValue($null, $sharing)
    $hostType.GetField("_localCallsignProvider", $any).SetValue($null, [Func[string]]{ "TEST-OP" })
    return @{ Preferences = $prefs; Sharing = $sharing }
}

# Runs queued dispatcher work for a while, as WinTAK's UI thread would.
function Pump-Dispatcher([double]$seconds = 0.3) {
    $deadline = [DateTime]::Now.AddSeconds($seconds)
    do {
        $frame = New-Object System.Windows.Threading.DispatcherFrame
        [System.Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([System.Windows.Threading.DispatcherPriority]::Background, [Action]{ $frame.Continue = $false }) | Out-Null
        [System.Windows.Threading.Dispatcher]::PushFrame($frame)
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::Now -lt $deadline)
}

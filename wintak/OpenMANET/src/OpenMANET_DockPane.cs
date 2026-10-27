using WinTak.Framework.Docking;
using WinTak.Framework.Docking.Attributes;

namespace WinTAK_OpenMANET_Plugin;

[DockPane(ID, "OpenMANET", Content = typeof(OpenMANET), DockLocation = DockLocation.Right, PreferredWidth = 480)]
internal sealed class OpenMANET_DockPane : DockPane
{
	internal const string ID = "WinTAK_OpenMANET_Plugin_OpenMANET_DockPane";
}

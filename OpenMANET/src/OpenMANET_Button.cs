using System.ComponentModel.Composition;
using WinTak.Framework.Docking;
using WinTak.Framework.Tools;
using WinTak.Framework.Tools.Attributes;

namespace WinTAK_OpenMANET_Plugin;

[Button("OpenMANET_Button", "OpenMANET", LargeImage = "pack://application:,,,/WinTAK_OpenMANET_Plugin;component/Assets/OpenMANET_Logo.png", SmallImage = "pack://application:,,,/WinTAK_OpenMANET_Plugin;component/Assets/OpenMANET_Logo.png")]
internal sealed class OpenMANET_Button : Button
{
	private readonly IDockingManager _dockingManager;

	[ImportingConstructor]
	public OpenMANET_Button(IDockingManager dockingManager)
	{
		_dockingManager = dockingManager;
	}

	protected override void OnClick()
	{
		base.OnClick();
		IDockPane dockPane = _dockingManager.GetDockPane("WinTAK_OpenMANET_Plugin_OpenMANET_DockPane");
		if (dockPane != null)
		{
			dockPane.Activate();
		}
	}
}

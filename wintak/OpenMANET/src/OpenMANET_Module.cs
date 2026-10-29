using System.ComponentModel.Composition;
using Prism.Mef.Modularity;
using Prism.Modularity;
using WinTak.Common.Services;
using WinTak.CursorOnTarget.Services;
using WinTak.Display;
using WinTak.Framework.Docking;
using WinTak.Framework.Messaging;
using WinTak.Framework.Notifications;

namespace WinTAK_OpenMANET_Plugin;

[ModuleExport(typeof(OpenMANET_Module), InitializationMode = InitializationMode.WhenAvailable)]
internal sealed class OpenMANET_Module : IModule
{
	// AllowDefault keeps the module loading even if a WinTAK build doesn't export one of these;
	// the plugin then just skips the feature that needs it.
	[Import(AllowDefault = true)]
	private IMessageHub MessageHub { get; set; }

	[Import(AllowDefault = true)]
	private INotificationLog NotificationLog { get; set; }

	[Import(AllowDefault = true)]
	private ICotMessageSender CotSender { get; set; }

	[Import(AllowDefault = true)]
	private ICotMessageReceiver CotReceiver { get; set; }

	[Import(AllowDefault = true)]
	private ICommunicationService CommunicationService { get; set; }

	[Import(AllowDefault = true)]
	private IDockingManager DockingManager { get; set; }

	[Import(AllowDefault = true)]
	private IMapViewController MapViewController { get; set; }

	[Import(AllowDefault = true)]
	private ILocationService LocationService { get; set; }

	public void Initialize()
	{
		OpenMANET_PluginHost.Initialize(MessageHub, NotificationLog, CotSender, CotReceiver, CommunicationService, DockingManager, LocationService);
		OpenMANET_MapMenu.Attach(MapViewController);
	}
}

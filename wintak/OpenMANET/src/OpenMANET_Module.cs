using System.ComponentModel.Composition;
using Prism.Mef.Modularity;
using Prism.Modularity;
using WinTak.Framework.Messaging;
using WinTak.Framework.Notifications;

namespace WinTAK_OpenMANET_Plugin;

[ModuleExport(typeof(OpenMANET_Module), InitializationMode = InitializationMode.WhenAvailable)]
internal sealed class OpenMANET_Module : IModule
{
	// AllowDefault keeps the module loading even if a WinTAK build doesn't export these;
	// the plugin then just skips WinTAK notifications.
	[Import(AllowDefault = true)]
	private IMessageHub MessageHub { get; set; }

	[Import(AllowDefault = true)]
	private INotificationLog NotificationLog { get; set; }

	public void Initialize()
	{
		OpenMANET_PluginHost.Initialize(MessageHub, NotificationLog);
	}
}

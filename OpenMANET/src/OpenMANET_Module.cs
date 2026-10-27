using Prism.Mef.Modularity;
using Prism.Modularity;

namespace WinTAK_OpenMANET_Plugin;

[ModuleExport(typeof(OpenMANET_Module), InitializationMode = InitializationMode.WhenAvailable)]
internal sealed class OpenMANET_Module : IModule
{
	public void Initialize()
	{
	}
}

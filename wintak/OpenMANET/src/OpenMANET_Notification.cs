using System;
using System.ComponentModel.Composition;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinTak.Framework.Notifications;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>A mesh alert in WinTAK's notification list.</summary>
/// <remarks>Exported so WinTAK's notification log can serialize it alongside its own types.</remarks>
[Export(typeof(Notification))]
public class OpenMANET_Notification : Notification
{
	private static ImageSource _icon;

	public OpenMANET_Notification()
	{
		Type = "OpenMANET";
		Key = "OpenMANET";
	}

	public override string GetHeader()
	{
		return "OpenMANET";
	}

	public override ImageSource GetHeaderIcon()
	{
		if (_icon == null)
		{
			BitmapImage icon = new BitmapImage(new Uri("pack://application:,,,/WinTAK_OpenMANET_Plugin;component/Assets/OpenMANET_Logo.png"));
			icon.Freeze();
			_icon = icon;
		}
		return _icon;
	}
}

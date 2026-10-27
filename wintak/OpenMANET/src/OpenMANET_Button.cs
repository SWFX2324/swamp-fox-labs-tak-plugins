using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
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
		// Show mesh health on the toolbar icon so it's visible with the pane closed.
		OpenMANET_PluginHost.HealthChanged += (sender, e) => UpdateHealthBadge();
		UpdateHealthBadge();
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

	private void UpdateHealthBadge()
	{
		Application application = Application.Current;
		if (application == null)
		{
			return;
		}
		application.Dispatcher.BeginInvoke((Action)(() => Overlay = CreateBadge(OpenMANET_PluginHost.Health)));
	}

	private static UIElement CreateBadge(OpenMANET_MeshHealth health)
	{
		Color color;
		switch (health)
		{
		case OpenMANET_MeshHealth.Healthy:
			color = Color.FromRgb(0x3D, 0xD6, 0x8C);
			break;
		case OpenMANET_MeshHealth.Degraded:
			color = Color.FromRgb(0xF2, 0xB5, 0x4A);
			break;
		case OpenMANET_MeshHealth.Critical:
			color = Color.FromRgb(0xF2, 0x6D, 0x6D);
			break;
		default:
			return null;
		}
		return new Ellipse
		{
			Width = 10.0,
			Height = 10.0,
			Fill = new SolidColorBrush(color),
			Stroke = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x15)),
			StrokeThickness = 1.5,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			IsHitTestVisible = false
		};
	}
}

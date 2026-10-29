using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinTak.Common.Utils;
using WinTak.Display;
using WinTak.Display.Controls;
using WinTak.Graphics;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>
/// Adds an OpenMANET item to WinTAK's radial menu when the operator clicks one of the plugin's
/// node markers, or the marker of an operator a node is drawn as. Choosing it opens the pane on that node's details.
/// </summary>
internal static class OpenMANET_MapMenu
{
	private const string MenuItemId = "openmanet-node-details";

	private static WheelMenuItem _menuItem;

	public static void Attach(IMapViewController mapViewController)
	{
		if (mapViewController != null)
		{
			mapViewController.WheelMenuOpening += MapViewController_WheelMenuOpening;
		}
	}

	private static void MapViewController_WheelMenuOpening(object sender, MenuPopupEventArgs e)
	{
		try
		{
			if (!(sender is WheelMenu menu))
			{
				return;
			}
			string uid = (e.GetSingleClickedItem(out object _) is MapItem item) ? item.GetUid() : null;
			// A node drawn as the operator standing at it is reached through that operator's marker.
			string nodeKey = OpenMANET_MapLayer.NodeKeyFromUid(uid) ?? OpenMANET_MapLayer.NodeKeyForOperator(uid);
			if (nodeKey == null)
			{
				// In case WinTAK reuses the menu, don't leave our item on anything else.
				if (_menuItem != null)
				{
					menu.Items.Remove(_menuItem);
				}
				return;
			}
			if (_menuItem == null)
			{
				ImageSource icon = new BitmapImage(new Uri("pack://application:,,,/WinTAK_OpenMANET_Plugin;component/Assets/OpenMANET_Logo.png"));
				_menuItem = new WheelMenuItem(string.Empty, icon, MenuItem_Click)
				{
					Id = MenuItemId,
					ToolTip = "OpenMANET node details"
				};
			}
			_menuItem.Tag = nodeKey;
			if (!menu.Items.Contains(_menuItem))
			{
				menu.Items.Add(_menuItem);
			}
		}
		catch
		{
			// Never break WinTAK's radial menu over an optional item.
		}
	}

	private static void MenuItem_Click(object sender, EventArgs e)
	{
		if ((sender as WheelMenuItem)?.Tag is string nodeKey)
		{
			OpenMANET_PluginHost.RequestNodeFocus(nodeKey);
		}
	}
}

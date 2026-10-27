using System;
using System.Collections.Generic;
using System.Linq;
using WinTak.Framework.Messaging;
using WinTak.Framework.Notifications;
using WinTak.Framework.Notifications.Messages;

namespace WinTAK_OpenMANET_Plugin;

internal enum OpenMANET_MeshHealth
{
	/// <summary>Not connected and not trying to be; the toolbar shows no badge.</summary>
	None,
	Healthy,
	Degraded,
	Critical
}

/// <summary>
/// WinTAK services and plugin-wide state shared between the module, the toolbar button and the
/// pane. The module is composed by MEF at startup; the pane is created later and only on demand.
/// </summary>
internal static class OpenMANET_PluginHost
{
	private static IMessageHub _messageHub;

	private static INotificationLog _notificationLog;

	public static OpenMANET_MeshHealth Health { get; private set; }

	public static event EventHandler HealthChanged;

	public static void Initialize(IMessageHub messageHub, INotificationLog notificationLog)
	{
		_messageHub = messageHub;
		_notificationLog = notificationLog;
	}

	public static void SetHealth(OpenMANET_MeshHealth health)
	{
		if (Health != health)
		{
			Health = health;
			HealthChanged?.Invoke(null, EventArgs.Empty);
		}
	}

	/// <summary>Posts a WinTAK notification. Returns false when WinTAK's message hub isn't available.</summary>
	public static bool Notify(string message)
	{
		if (_messageHub == null || string.IsNullOrWhiteSpace(message))
		{
			return false;
		}
		DateTime now = DateTime.UtcNow;
		_messageHub.Publish(new NotificationMessage(new OpenMANET_Notification
		{
			Uid = "openmanet-" + Guid.NewGuid().ToString("N"),
			StartTime = now,
			// Live mesh alerts lose their value quickly; let WinTAK prune them.
			StaleTime = now.AddHours(1.0),
			Message = message
		}));
		return true;
	}

	/// <summary>
	/// Removes this plugin's notifications from WinTAK's saved log. WinTAK persists the log with a
	/// serializer built from the installed plugins, so entries left behind would break loading it
	/// if the plugin were later uninstalled.
	/// </summary>
	public static void RemoveOwnNotifications()
	{
		if (_notificationLog == null)
		{
			return;
		}
		try
		{
			List<Notification> own = _notificationLog.Notifications.OfType<OpenMANET_Notification>().Cast<Notification>().ToList();
			foreach (Notification notification in own)
			{
				_notificationLog.RemoveNotification(notification);
			}
		}
		catch
		{
			// Shutdown cleanup is best effort.
		}
	}
}

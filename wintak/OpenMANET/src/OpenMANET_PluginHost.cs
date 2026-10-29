using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using WinTak.Common.CoT;
using WinTak.Common.Services;
using WinTak.Common.Utils;
using WinTak.CursorOnTarget.Services;
using WinTak.Framework.Docking;
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
	public static readonly string SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinTAK", "Plugins", "WinTAK_OpenMANET_Plugin");

	private static readonly object PreferencesLock = new object();

	private static IMessageHub _messageHub;

	private static INotificationLog _notificationLog;

	private static ICommunicationService _communicationService;

	private static OpenMANET_Preferences _preferences;

	private static OpenMANET_CallsignSharing _callsignSharing;

	// The operator's TAK callsign, read when sharing so a rename in WinTAK is picked up. Replaceable in tests,
	// where WinTAK's settings aren't loaded.
	private static Func<string> _localCallsignProvider = LocalTakCallsign;

	// Processes Cursor-on-Target locally; used to draw the mesh on the map.
	public static ICotMessageSender CotSender { get; private set; }

	public static IDockingManager DockingManager { get; private set; }

	public static OpenMANET_MeshHealth Health { get; private set; }

	public static event EventHandler HealthChanged;

	/// <summary>Raised on WinTAK's message thread when another operator asks for shared callsigns and this one shares.</summary>
	public static event EventHandler CallsignsRequested;

	/// <summary>Raised with a map node key when the operator asks to see that node in the pane.</summary>
	public static event EventHandler<string> NodeFocusRequested;

	/// <summary>Preferences shared by the pane and the module, loaded on first use.</summary>
	public static OpenMANET_Preferences Preferences
	{
		get
		{
			lock (PreferencesLock)
			{
				if (_preferences == null)
				{
					_preferences = new OpenMANET_Preferences(Path.Combine(SettingsDirectory, "preferences.settings"));
					_preferences.Load();
				}
				return _preferences;
			}
		}
	}

	/// <summary>Callsigns shared by other operators, loaded on first use.</summary>
	public static OpenMANET_CallsignSharing CallsignSharing
	{
		get
		{
			lock (PreferencesLock)
			{
				if (_callsignSharing == null)
				{
					_callsignSharing = new OpenMANET_CallsignSharing(Path.Combine(SettingsDirectory, "shared-callsigns.settings"), LocalTakUid());
					_callsignSharing.Load();
				}
				return _callsignSharing;
			}
		}
	}

	/// <summary>True when WinTAK's network service is available to share callsigns through.</summary>
	public static bool CanShareCallsigns => _communicationService != null;

	public static void Initialize(IMessageHub messageHub, INotificationLog notificationLog, ICotMessageSender cotSender, ICotMessageReceiver cotReceiver, ICommunicationService communicationService, IDockingManager dockingManager, ILocationService locationService = null)
	{
		_messageHub = messageHub;
		_notificationLog = notificationLog;
		CotSender = cotSender;
		_communicationService = communicationService;
		DockingManager = dockingManager;
		if (locationService != null)
		{
			OpenMANET_Operators.SetSelfPositionProvider(locationService.GetPositionDocument);
		}
		if (cotReceiver != null)
		{
			cotReceiver.PreviewMessageReceived += CotReceiver_PreviewMessageReceived;
		}
	}

	public static void SetHealth(OpenMANET_MeshHealth health)
	{
		if (Health != health)
		{
			Health = health;
			HealthChanged?.Invoke(null, EventArgs.Empty);
		}
	}

	/// <summary>Opens the pane on a node, for the map marker's radial menu.</summary>
	public static void RequestNodeFocus(string nodeKey)
	{
		DockingManager?.GetDockPane(OpenMANET_DockPane.ID)?.Activate();
		NodeFocusRequested?.Invoke(null, nodeKey);
	}

	/// <summary>Broadcasts this operator's callsigns to the TAK network. Returns false when WinTAK can't send.</summary>
	public static bool BroadcastCallsigns(IReadOnlyList<OpenMANET_CallsignEntry> entries)
	{
		if (_communicationService == null)
		{
			return false;
		}
		XmlDocument message = CallsignSharing.CreateMessage(_localCallsignProvider(), entries, DateTime.UtcNow);
		_communicationService.BroadcastCot(message);
		return true;
	}

	/// <summary>Asks operators who share callsigns to send them now. Returns false when WinTAK can't send.</summary>
	public static bool RequestSharedCallsigns()
	{
		if (_communicationService == null)
		{
			return false;
		}
		_communicationService.BroadcastCot(CallsignSharing.CreateRequest(DateTime.UtcNow));
		return true;
	}

	/// <summary>
	/// Takes callsign events out of WinTAK's normal processing so they never become map items or
	/// get stored, and applies them when the operator has opted in to shared callsigns.
	/// </summary>
	private static void CotReceiver_PreviewMessageReceived(object sender, CoTMessageArgument e)
	{
		try
		{
			if (e == null)
			{
				return;
			}
			if (e.Type == OpenMANET_CallsignSharing.RequestCotType)
			{
				e.Handled = true;
				if (Preferences.ShareCallsigns && CallsignSharing.IsRequestFromOther(e.Message))
				{
					CallsignsRequested?.Invoke(null, EventArgs.Empty);
				}
				return;
			}
			if (e.Type != OpenMANET_CallsignSharing.CotType)
			{
				// A node's own COT OUT marker duplicates the one this plugin draws for it.
				if (Preferences.HideNodeOwnMarkers && OpenMANET_MapLayer.HideNodeOwnMarker(e.Message))
				{
					e.Handled = true;
				}
				else if (e.Type != null && e.Type.StartsWith("a-", StringComparison.Ordinal))
				{
					// Other operators' positions, so a node with someone standing at it is drawn as them.
					OpenMANET_Operators.TryRecord(e.Message, DateTime.UtcNow);
				}
				return;
			}
			e.Handled = true;
			if (Preferences.UseSharedCallsigns)
			{
				CallsignSharing.TryApply(e.Message, DateTimeOffset.UtcNow);
			}
		}
		catch
		{
			// A malformed event from the network must never disturb WinTAK's message processing.
		}
	}

	private static string LocalTakUid()
	{
		try
		{
			return PreferenceUtils.GetUid();
		}
		catch
		{
			return null;
		}
	}

	private static string LocalTakCallsign()
	{
		try
		{
			return PreferenceUtils.GetCallsign();
		}
		catch
		{
			return null;
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

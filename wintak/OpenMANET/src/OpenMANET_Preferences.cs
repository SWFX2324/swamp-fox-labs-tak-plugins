using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>Alert, reconnect, map and callsign-sharing preferences, stored as key=value lines next to connection.settings.</summary>
internal sealed class OpenMANET_Preferences
{
	private const long MaximumFileBytes = 4096;

	private readonly string _filePath;

	public bool NotifyNodes { get; set; } = true;

	public bool NotifyLinks { get; set; } = true;

	public bool NotifyGateway { get; set; } = true;

	public bool NotifyConnection { get; set; } = true;

	public bool AutoReconnect { get; set; } = true;

	public bool ShowOnMap { get; set; } = true;

	// Hide the marker a node broadcasts for itself (COT OUT) while the plugin draws that node.
	public bool HideNodeOwnMarkers { get; set; } = true;

	// Radio icons for nodes nobody is at. Off: the map shows only TAK users' own markers, each node drawn as the user at it.
	public bool ShowUnattendedNodes { get; set; }

	public static readonly int[] MergeDistanceChoices = { 10, 25, 50, 100 };

	private int _mergeDistanceMeters = 25;

	/// <summary>How close a TAK user must be to a node to stand in for it; one of <see cref="MergeDistanceChoices"/>.</summary>
	public int MergeDistanceMeters
	{
		get => _mergeDistanceMeters;
		set => _mergeDistanceMeters = (Array.IndexOf(MergeDistanceChoices, value) >= 0) ? value : 25;
	}

	// Callsign sharing over TAK is opt-in both ways: it sends node identifiers to other users.
	public bool ShareCallsigns { get; set; }

	public bool UseSharedCallsigns { get; set; }

	public OpenMANET_Preferences(string filePath)
	{
		_filePath = filePath;
	}

	public void Load()
	{
		try
		{
			if (!File.Exists(_filePath) || new FileInfo(_filePath).Length > MaximumFileBytes)
			{
				return;
			}
			Dictionary<string, bool> values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, int> numbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (string line in File.ReadAllLines(_filePath, Encoding.UTF8))
			{
				int separator = line.IndexOf('=');
				if (separator > 0 && bool.TryParse(line.Substring(separator + 1).Trim(), out bool value))
				{
					values[line.Substring(0, separator).Trim()] = value;
				}
				else if (separator > 0 && int.TryParse(line.Substring(separator + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
				{
					numbers[line.Substring(0, separator).Trim()] = number;
				}
			}
			NotifyNodes = Get(values, nameof(NotifyNodes), NotifyNodes);
			NotifyLinks = Get(values, nameof(NotifyLinks), NotifyLinks);
			NotifyGateway = Get(values, nameof(NotifyGateway), NotifyGateway);
			NotifyConnection = Get(values, nameof(NotifyConnection), NotifyConnection);
			AutoReconnect = Get(values, nameof(AutoReconnect), AutoReconnect);
			ShowOnMap = Get(values, nameof(ShowOnMap), ShowOnMap);
			HideNodeOwnMarkers = Get(values, nameof(HideNodeOwnMarkers), HideNodeOwnMarkers);
			ShowUnattendedNodes = Get(values, nameof(ShowUnattendedNodes), ShowUnattendedNodes);
			if (numbers.TryGetValue(nameof(MergeDistanceMeters), out int distance))
			{
				MergeDistanceMeters = distance;
			}
			ShareCallsigns = Get(values, nameof(ShareCallsigns), ShareCallsigns);
			UseSharedCallsigns = Get(values, nameof(UseSharedCallsigns), UseSharedCallsigns);
		}
		catch
		{
			// A damaged file falls back to the defaults.
		}
	}

	public void Save()
	{
		string directory = Path.GetDirectoryName(_filePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}
		File.WriteAllLines(_filePath, new[]
		{
			Line(nameof(NotifyNodes), NotifyNodes),
			Line(nameof(NotifyLinks), NotifyLinks),
			Line(nameof(NotifyGateway), NotifyGateway),
			Line(nameof(NotifyConnection), NotifyConnection),
			Line(nameof(AutoReconnect), AutoReconnect),
			Line(nameof(ShowOnMap), ShowOnMap),
			Line(nameof(HideNodeOwnMarkers), HideNodeOwnMarkers),
			Line(nameof(ShowUnattendedNodes), ShowUnattendedNodes),
			nameof(MergeDistanceMeters) + "=" + MergeDistanceMeters.ToString(CultureInfo.InvariantCulture),
			Line(nameof(ShareCallsigns), ShareCallsigns),
			Line(nameof(UseSharedCallsigns), UseSharedCallsigns)
		}, new UTF8Encoding(false));
	}

	public bool ShouldNotify(OpenMANET_EventCategory category)
	{
		switch (category)
		{
		case OpenMANET_EventCategory.Nodes:
			return NotifyNodes;
		case OpenMANET_EventCategory.Links:
			return NotifyLinks;
		case OpenMANET_EventCategory.Gateway:
			return NotifyGateway;
		case OpenMANET_EventCategory.Connection:
			return NotifyConnection;
		default:
			return false;
		}
	}

	private static bool Get(Dictionary<string, bool> values, string key, bool fallback)
	{
		return values.TryGetValue(key, out bool value) ? value : fallback;
	}

	private static string Line(string key, bool value)
	{
		return key + "=" + value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
	}
}

/// <summary>Which notification setting a mesh event falls under; None events only go to the event log.</summary>
internal enum OpenMANET_EventCategory
{
	None,
	Nodes,
	Links,
	Gateway,
	Connection
}

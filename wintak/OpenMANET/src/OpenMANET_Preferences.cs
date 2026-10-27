using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>Alert, reconnect and map preferences, stored as key=value lines next to connection.settings.</summary>
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
			foreach (string line in File.ReadAllLines(_filePath, Encoding.UTF8))
			{
				int separator = line.IndexOf('=');
				if (separator > 0 && bool.TryParse(line.Substring(separator + 1).Trim(), out bool value))
				{
					values[line.Substring(0, separator).Trim()] = value;
				}
			}
			NotifyNodes = Get(values, nameof(NotifyNodes), NotifyNodes);
			NotifyLinks = Get(values, nameof(NotifyLinks), NotifyLinks);
			NotifyGateway = Get(values, nameof(NotifyGateway), NotifyGateway);
			NotifyConnection = Get(values, nameof(NotifyConnection), NotifyConnection);
			AutoReconnect = Get(values, nameof(AutoReconnect), AutoReconnect);
			ShowOnMap = Get(values, nameof(ShowOnMap), ShowOnMap);
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
			Line(nameof(ShowOnMap), ShowOnMap)
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>
/// User-assigned display callsigns for mesh nodes, kept on this machine only. Entries are keyed
/// by MAC address, with the hostname as a fallback for nodes whose MAC is unknown or changes.
/// </summary>
internal sealed class OpenMANET_CallsignStore
{
	public const int MaximumLength = 24;

	private const int MaximumEntries = 256;

	private const long MaximumFileBytes = 64 * 1024;

	private readonly string _filePath;

	private readonly List<Entry> _entries = new List<Entry>();

	private sealed class Entry
	{
		public string Mac { get; set; }

		public string Hostname { get; set; }

		public string Callsign { get; set; }
	}

	public OpenMANET_CallsignStore(string filePath)
	{
		_filePath = filePath;
	}

	public void Load()
	{
		_entries.Clear();
		try
		{
			if (!File.Exists(_filePath) || new FileInfo(_filePath).Length > MaximumFileBytes)
			{
				return;
			}
			foreach (string line in File.ReadAllLines(_filePath, Encoding.UTF8))
			{
				string[] parts = line.Split('\t');
				if (parts.Length != 3 || _entries.Count >= MaximumEntries)
				{
					continue;
				}
				string callsign = Normalize(parts[2]);
				string mac = NormalizeMac(parts[0]);
				string hostname = NormalizeHostname(parts[1]);
				if (callsign != null && (mac != null || hostname != null))
				{
					_entries.Add(new Entry { Mac = mac, Hostname = hostname, Callsign = callsign });
				}
			}
		}
		catch
		{
			// A damaged file only costs the vanity names; nodes still show their hostnames.
			_entries.Clear();
		}
	}

	/// <summary>Returns the callsign for a node, matching any of its MACs first, then its hostname.</summary>
	public string Find(IEnumerable<string> macs, string hostname)
	{
		foreach (string mac in macs ?? Enumerable.Empty<string>())
		{
			string normalized = NormalizeMac(mac);
			Entry entry = (normalized == null) ? null : _entries.Find((Entry e) => e.Mac == normalized);
			if (entry != null)
			{
				return entry.Callsign;
			}
		}
		string host = NormalizeHostname(hostname);
		return (host == null) ? null : _entries.Find((Entry e) => e.Mac == null && e.Hostname == host)?.Callsign
			?? _entries.Find((Entry e) => e.Hostname == host)?.Callsign;
	}

	/// <summary>Sets or, when <paramref name="callsign"/> is blank, clears a node's callsign and saves.</summary>
	public void Set(string mac, string hostname, string callsign)
	{
		string normalizedMac = NormalizeMac(mac);
		string normalizedHost = NormalizeHostname(hostname);
		if (normalizedMac == null && normalizedHost == null)
		{
			throw new InvalidOperationException("This node has no MAC address or hostname to attach a callsign to.");
		}
		string value = string.IsNullOrWhiteSpace(callsign) ? null : Normalize(callsign);
		if (!string.IsNullOrWhiteSpace(callsign) && value == null)
		{
			throw new ArgumentException($"Callsigns can be up to {MaximumLength} characters of letters, numbers, spaces and - _ . / #.");
		}
		_entries.RemoveAll((Entry e) => (normalizedMac != null) ? (e.Mac == normalizedMac || (e.Mac == null && e.Hostname == normalizedHost)) : (e.Mac == null && e.Hostname == normalizedHost));
		if (value != null)
		{
			if (_entries.Count >= MaximumEntries)
			{
				throw new InvalidOperationException("Too many saved callsigns.");
			}
			_entries.Add(new Entry { Mac = normalizedMac, Hostname = normalizedHost, Callsign = value });
		}
		Save();
	}

	private void Save()
	{
		string directory = Path.GetDirectoryName(_filePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}
		File.WriteAllLines(_filePath, _entries.Select((Entry e) => (e.Mac ?? string.Empty) + "\t" + (e.Hostname ?? string.Empty) + "\t" + e.Callsign), new UTF8Encoding(false));
	}

	/// <summary>Trims a proposed callsign and returns null when it is empty or has disallowed characters.</summary>
	public static string Normalize(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		string trimmed = value.Trim();
		if (trimmed.Length > MaximumLength)
		{
			return null;
		}
		foreach (char c in trimmed)
		{
			if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_' && c != '.' && c != '/' && c != '#')
			{
				return null;
			}
		}
		return trimmed;
	}

	private static string NormalizeMac(string mac)
	{
		if (string.IsNullOrWhiteSpace(mac) || mac.Trim() == "--")
		{
			return null;
		}
		return mac.Trim().Replace('-', ':').ToLowerInvariant();
	}

	private static string NormalizeHostname(string hostname)
	{
		if (string.IsNullOrWhiteSpace(hostname) || hostname.Trim() == "Unknown node")
		{
			return null;
		}
		return hostname.Trim().ToLowerInvariant();
	}
}

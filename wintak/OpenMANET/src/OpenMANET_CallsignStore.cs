using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>One node's callsign, as saved locally or as shared by another operator.</summary>
internal sealed class OpenMANET_CallsignEntry
{
	public string Mac { get; set; }

	public string Hostname { get; set; }

	public string Callsign { get; set; }

	// When the callsign was last set; decides between operators who named the same node.
	public DateTimeOffset UpdatedUtc { get; set; }

	// Shared by the operator connected to this node, whose name for it outranks other operators'.
	public bool FromNodeOperator { get; set; }
}

/// <summary>
/// User-assigned display callsigns for mesh nodes, kept on this machine only. Entries are keyed
/// by MAC address, with the hostname as a fallback for nodes whose MAC is unknown or changes.
/// </summary>
internal sealed class OpenMANET_CallsignStore
{
	public const int MaximumLength = 24;

	public const int MaximumEntries = 256;

	private const long MaximumFileBytes = 64 * 1024;

	private readonly string _filePath;

	private readonly List<OpenMANET_CallsignEntry> _entries = new List<OpenMANET_CallsignEntry>();

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
			// Files from before 2.1.1 have no timestamp column; date those entries by the file.
			DateTimeOffset fileTime = new DateTimeOffset(File.GetLastWriteTimeUtc(_filePath), TimeSpan.Zero);
			foreach (string line in File.ReadAllLines(_filePath, Encoding.UTF8))
			{
				string[] parts = line.Split('\t');
				if ((parts.Length != 3 && parts.Length != 4) || _entries.Count >= MaximumEntries)
				{
					continue;
				}
				string callsign = Normalize(parts[2]);
				string mac = NormalizeMac(parts[0]);
				string hostname = NormalizeHostname(parts[1]);
				if (callsign != null && (mac != null || hostname != null))
				{
					_entries.Add(new OpenMANET_CallsignEntry { Mac = mac, Hostname = hostname, Callsign = callsign, UpdatedUtc = (parts.Length == 4) ? ParseTime(parts[3], fileTime) : fileTime });
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
		return FindEntry(_entries, macs, hostname)?.Callsign;
	}

	/// <summary>Finds a node's entry in <paramref name="entries"/>: any of its MACs first, then its hostname.</summary>
	public static OpenMANET_CallsignEntry FindEntry(IReadOnlyList<OpenMANET_CallsignEntry> entries, IEnumerable<string> macs, string hostname)
	{
		foreach (string mac in macs ?? Enumerable.Empty<string>())
		{
			string normalized = NormalizeMac(mac);
			OpenMANET_CallsignEntry entry = (normalized == null) ? null : entries.FirstOrDefault((OpenMANET_CallsignEntry e) => e.Mac == normalized);
			if (entry != null)
			{
				return entry;
			}
		}
		string host = NormalizeHostname(hostname);
		return (host == null) ? null : entries.FirstOrDefault((OpenMANET_CallsignEntry e) => e.Mac == null && e.Hostname == host)
			?? entries.FirstOrDefault((OpenMANET_CallsignEntry e) => e.Hostname == host);
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
		_entries.RemoveAll((OpenMANET_CallsignEntry e) => (normalizedMac != null) ? (e.Mac == normalizedMac || (e.Mac == null && e.Hostname == normalizedHost)) : (e.Mac == null && e.Hostname == normalizedHost));
		if (value != null)
		{
			if (_entries.Count >= MaximumEntries)
			{
				throw new InvalidOperationException("Too many saved callsigns.");
			}
			_entries.Add(new OpenMANET_CallsignEntry { Mac = normalizedMac, Hostname = normalizedHost, Callsign = value, UpdatedUtc = DateTimeOffset.UtcNow });
		}
		Save();
	}

	public int Count => _entries.Count;

	/// <summary>A copy of every saved callsign, for sharing over TAK.</summary>
	public List<OpenMANET_CallsignEntry> Entries => _entries.Select((OpenMANET_CallsignEntry e) => new OpenMANET_CallsignEntry { Mac = e.Mac, Hostname = e.Hostname, Callsign = e.Callsign, UpdatedUtc = e.UpdatedUtc }).ToList();

	/// <summary>Writes every callsign to a CSV file (mac,hostname,callsign) for sharing.</summary>
	public void ExportTo(string path)
	{
		List<string> lines = new List<string> { "mac,hostname,callsign" };
		lines.AddRange(_entries.Select((OpenMANET_CallsignEntry e) => (e.Mac ?? string.Empty) + "," + (e.Hostname ?? string.Empty) + "," + e.Callsign));
		File.WriteAllLines(path, lines, new UTF8Encoding(false));
	}

	/// <summary>
	/// Merges callsigns from a CSV export into this store, replacing existing entries for the same
	/// node. Invalid lines are skipped. Returns how many entries were imported.
	/// </summary>
	public int ImportFrom(string path, out int skipped)
	{
		skipped = 0;
		if (new FileInfo(path).Length > MaximumFileBytes)
		{
			throw new InvalidOperationException("The callsign file is too large.");
		}
		int imported = 0;
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line.StartsWith("mac,", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			// Callsigns can't contain commas or tabs, so a plain split is safe.
			string[] parts = line.Split(line.IndexOf('\t') >= 0 ? '\t' : ',');
			string mac = (parts.Length == 3) ? NormalizeMac(parts[0]) : null;
			string hostname = (parts.Length == 3) ? NormalizeHostname(parts[1]) : null;
			string callsign = (parts.Length == 3) ? Normalize(parts[2]) : null;
			if (callsign == null || (mac == null && hostname == null))
			{
				skipped++;
				continue;
			}
			_entries.RemoveAll((OpenMANET_CallsignEntry e) => (mac != null) ? (e.Mac == mac || (e.Mac == null && e.Hostname == hostname)) : (e.Mac == null && e.Hostname == hostname));
			if (_entries.Count >= MaximumEntries)
			{
				skipped++;
				continue;
			}
			_entries.Add(new OpenMANET_CallsignEntry { Mac = mac, Hostname = hostname, Callsign = callsign, UpdatedUtc = now });
			imported++;
		}
		Save();
		return imported;
	}

	private void Save()
	{
		string directory = Path.GetDirectoryName(_filePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}
		File.WriteAllLines(_filePath, _entries.Select((OpenMANET_CallsignEntry e) => (e.Mac ?? string.Empty) + "\t" + (e.Hostname ?? string.Empty) + "\t" + e.Callsign + "\t" + FormatTime(e.UpdatedUtc)), new UTF8Encoding(false));
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

	public static string NormalizeMac(string mac)
	{
		if (string.IsNullOrWhiteSpace(mac) || mac.Trim() == "--")
		{
			return null;
		}
		string normalized = mac.Trim().Replace('-', ':').ToLowerInvariant();
		// Nothing longer than an EUI-64 is a MAC; this also bounds what other operators can send.
		return (normalized.Length > 23) ? null : normalized;
	}

	public static string NormalizeHostname(string hostname)
	{
		// The plugin's placeholders for nodes without a hostname aren't names; matching on them
		// would give every unnamed node the same callsign.
		if (string.IsNullOrWhiteSpace(hostname) || string.Equals(hostname.Trim(), "Unknown node", StringComparison.OrdinalIgnoreCase) || string.Equals(hostname.Trim(), "Local node", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		string normalized = hostname.Trim().ToLowerInvariant();
		return (normalized.Length > 63) ? null : normalized;
	}

	public static string FormatTime(DateTimeOffset value)
	{
		return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
	}

	public static DateTimeOffset ParseTime(string value, DateTimeOffset fallback)
	{
		return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed) ? parsed : fallback;
	}
}

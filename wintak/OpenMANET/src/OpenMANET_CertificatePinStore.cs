using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>
/// SHA-256 fingerprints of node HTTPS certificates, pinned the first time each gateway is trusted
/// (trust on first use). Stored as "host:port&lt;TAB&gt;fingerprint" lines next to connection.settings.
/// </summary>
internal sealed class OpenMANET_CertificatePinStore
{
	private const long MaximumFileBytes = 64 * 1024;

	private const int MaximumEntries = 256;

	private readonly string _filePath;

	private readonly Dictionary<string, string> _pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public OpenMANET_CertificatePinStore(string filePath)
	{
		_filePath = filePath;
	}

	public void Load()
	{
		_pins.Clear();
		try
		{
			if (!File.Exists(_filePath) || new FileInfo(_filePath).Length > MaximumFileBytes)
			{
				return;
			}
			foreach (string line in File.ReadAllLines(_filePath, Encoding.UTF8))
			{
				string[] parts = line.Split('\t');
				string thumbprint = (parts.Length == 2) ? NormalizeThumbprint(parts[1]) : null;
				if (thumbprint != null && parts[0].Trim().Length > 0 && _pins.Count < MaximumEntries)
				{
					_pins[parts[0].Trim()] = thumbprint;
				}
			}
		}
		catch
		{
			// An unreadable file means nothing is pinned yet; the next connect pins again.
			_pins.Clear();
		}
	}

	public string Find(string host, int port)
	{
		return _pins.TryGetValue(Key(host, port), out string thumbprint) ? thumbprint : null;
	}

	public void Set(string host, int port, string thumbprint)
	{
		string normalized = NormalizeThumbprint(thumbprint) ?? throw new ArgumentException("Invalid certificate fingerprint.", nameof(thumbprint));
		_pins[Key(host, port)] = normalized;
		Save();
	}

	public bool Remove(string host, int port)
	{
		bool removed = _pins.Remove(Key(host, port));
		if (removed)
		{
			Save();
		}
		return removed;
	}

	/// <summary>Formats a fingerprint as colon-separated byte pairs for display.</summary>
	public static string Format(string thumbprint)
	{
		string normalized = NormalizeThumbprint(thumbprint);
		if (normalized == null)
		{
			return "--";
		}
		StringBuilder builder = new StringBuilder(normalized.Length * 3 / 2);
		for (int i = 0; i < normalized.Length; i += 2)
		{
			if (i > 0)
			{
				builder.Append(':');
			}
			builder.Append(normalized, i, 2);
		}
		return builder.ToString();
	}

	public static string NormalizeThumbprint(string thumbprint)
	{
		if (string.IsNullOrWhiteSpace(thumbprint))
		{
			return null;
		}
		string hex = new string(thumbprint.Where((char c) => c != ':' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
		// SHA-256 is 32 bytes.
		if (hex.Length != 64 || hex.Any((char c) => !Uri.IsHexDigit(c)))
		{
			return null;
		}
		return hex;
	}

	private void Save()
	{
		string directory = Path.GetDirectoryName(_filePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}
		File.WriteAllLines(_filePath, _pins.Select((KeyValuePair<string, string> pin) => pin.Key + "\t" + pin.Value), new UTF8Encoding(false));
	}

	private static string Key(string host, int port)
	{
		return (host ?? string.Empty).Trim().ToLowerInvariant() + ":" + port.ToString(CultureInfo.InvariantCulture);
	}
}

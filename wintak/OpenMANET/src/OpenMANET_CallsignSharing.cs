using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>A callsign another operator shared, and who shared it.</summary>
internal sealed class OpenMANET_SharedCallsign
{
	public string Callsign { get; set; }

	public string SharedBy { get; set; }
}

/// <summary>Raised when another operator's shared callsigns arrive and change what is known.</summary>
internal sealed class OpenMANET_CallsignsReceivedEventArgs : EventArgs
{
	public string SharedBy { get; set; }

	public int Count { get; set; }
}

/// <summary>
/// Shares node callsigns between operators over the TAK network. Each operator broadcasts their
/// whole list as one Cursor-on-Target event; a newer event from the same operator replaces the
/// older one, and an empty list withdraws everything they shared. Received lists are kept on disk
/// so names survive a restart. Everything received is validated as untrusted input.
/// </summary>
internal sealed class OpenMANET_CallsignSharing
{
	// A data event, not a map item: "b" events carry data, and no TAK client draws this subtype.
	public const string CotType = "b-x-openmanet-callsigns";

	// Asks operators who share to send their lists now, so names show without waiting for a rebroadcast.
	public const string RequestCotType = "b-x-openmanet-callsigns-request";

	// Often enough that a lost multicast packet only costs a minute; each event is a few hundred bytes.
	public static readonly TimeSpan RebroadcastInterval = TimeSpan.FromMinutes(1.0);

	// Answering requests at most this often keeps a burst of requests from flooding the mesh.
	public static readonly TimeSpan RequestAnswerInterval = TimeSpan.FromSeconds(10.0);

	private const string UidPrefix = "openmanet-callsigns-";

	private const string DetailElement = "__openmanet_callsigns";

	private const string FormatVersion = "1";

	private const int MaximumSenders = 32;

	private const int MaximumSenderUidLength = 128;

	private const int MaximumSenderLabelLength = 40;

	private const long MaximumFileBytes = 1024 * 1024;

	private static readonly TimeSpan SenderRetention = TimeSpan.FromDays(30.0);

	// Rebroadcast every few minutes, so a stale event just means the operator stopped sharing.
	private static readonly TimeSpan MessageStaleAfter = TimeSpan.FromMinutes(15.0);

	private readonly object _sync = new object();

	private readonly string _filePath;

	private readonly Dictionary<string, Sender> _senders = new Dictionary<string, Sender>(StringComparer.Ordinal);

	private sealed class Sender
	{
		public string Uid { get; set; }

		public string Label { get; set; }

		public DateTimeOffset ReceivedUtc { get; set; }

		public List<OpenMANET_CallsignEntry> Entries { get; } = new List<OpenMANET_CallsignEntry>();
	}

	public OpenMANET_CallsignSharing(string filePath, string localSenderUid)
	{
		_filePath = filePath;
		LocalSenderUid = NormalizeSenderUid(localSenderUid) ?? ("openmanet-" + Guid.NewGuid().ToString("N"));
	}

	/// <summary>This WinTAK's identity in shared events; events carrying it are our own echoes.</summary>
	public string LocalSenderUid { get; }

	public event EventHandler<OpenMANET_CallsignsReceivedEventArgs> Received;

	public int SenderCount
	{
		get
		{
			lock (_sync)
			{
				return _senders.Count;
			}
		}
	}

	public int EntryCount
	{
		get
		{
			lock (_sync)
			{
				return _senders.Values.Sum((Sender s) => s.Entries.Count);
			}
		}
	}

	/// <summary>
	/// Returns the newest callsign other operators shared for a node, matching its MACs before its
	/// hostname, or null when nobody shared one.
	/// </summary>
	public OpenMANET_SharedCallsign Find(IEnumerable<string> macs, string hostname)
	{
		List<string> macList = (macs ?? Enumerable.Empty<string>()).ToList();
		lock (_sync)
		{
			OpenMANET_CallsignEntry best = null;
			Sender bestSender = null;
			bool bestByMac = false;
			foreach (Sender sender in _senders.Values)
			{
				OpenMANET_CallsignEntry entry = OpenMANET_CallsignStore.FindEntry(sender.Entries, macList, hostname);
				if (entry == null)
				{
					continue;
				}
				bool byMac = entry.Mac != null && macList.Any((string mac) => OpenMANET_CallsignStore.NormalizeMac(mac) == entry.Mac);
				// The node's own operator has the final say; then a MAC match beats a hostname match; among equals the newest wins.
				bool better = (entry.FromNodeOperator != best?.FromNodeOperator) ? entry.FromNodeOperator : ((byMac && !bestByMac) || (byMac == bestByMac && entry.UpdatedUtc > best.UpdatedUtc));
				if (best == null || better)
				{
					best = entry;
					bestSender = sender;
					bestByMac = byMac;
				}
			}
			return (best == null) ? null : new OpenMANET_SharedCallsign { Callsign = best.Callsign, SharedBy = bestSender.Label };
		}
	}

	/// <summary>Builds the event that shares <paramref name="entries"/>; an empty list withdraws earlier shares.</summary>
	public XmlDocument CreateMessage(string senderLabel, IReadOnlyList<OpenMANET_CallsignEntry> entries, DateTime nowUtc)
	{
		XmlDocument doc = new XmlDocument();
		XmlElement evt = doc.CreateElement("event");
		evt.SetAttribute("version", "2.0");
		evt.SetAttribute("uid", UidPrefix + LocalSenderUid);
		evt.SetAttribute("type", CotType);
		evt.SetAttribute("how", "h-g-i-g-o");
		evt.SetAttribute("time", FormatCotTime(nowUtc));
		evt.SetAttribute("start", FormatCotTime(nowUtc));
		evt.SetAttribute("stale", FormatCotTime(nowUtc.Add(MessageStaleAfter)));
		doc.AppendChild(evt);
		XmlElement point = doc.CreateElement("point");
		point.SetAttribute("lat", "0.0");
		point.SetAttribute("lon", "0.0");
		point.SetAttribute("hae", "9999999.0");
		point.SetAttribute("ce", "9999999.0");
		point.SetAttribute("le", "9999999.0");
		evt.AppendChild(point);
		XmlElement detail = doc.CreateElement("detail");
		evt.AppendChild(detail);
		XmlElement list = doc.CreateElement(DetailElement);
		list.SetAttribute("version", FormatVersion);
		list.SetAttribute("sender", LocalSenderUid);
		list.SetAttribute("from", NormalizeSenderLabel(senderLabel) ?? string.Empty);
		detail.AppendChild(list);
		foreach (OpenMANET_CallsignEntry entry in entries.Take(OpenMANET_CallsignStore.MaximumEntries))
		{
			XmlElement node = doc.CreateElement("node");
			if (entry.Mac != null)
			{
				node.SetAttribute("mac", entry.Mac);
			}
			if (entry.Hostname != null)
			{
				node.SetAttribute("host", entry.Hostname);
			}
			node.SetAttribute("callsign", entry.Callsign);
			node.SetAttribute("updated", OpenMANET_CallsignStore.FormatTime(entry.UpdatedUtc));
			if (entry.FromNodeOperator)
			{
				node.SetAttribute("operator", "1");
			}
			list.AppendChild(node);
		}
		return doc;
	}

	/// <summary>Builds the event asking operators who share their callsigns to send them now.</summary>
	public XmlDocument CreateRequest(DateTime nowUtc)
	{
		XmlDocument doc = new XmlDocument();
		XmlElement evt = doc.CreateElement("event");
		evt.SetAttribute("version", "2.0");
		evt.SetAttribute("uid", UidPrefix + "request-" + LocalSenderUid);
		evt.SetAttribute("type", RequestCotType);
		evt.SetAttribute("how", "h-g-i-g-o");
		evt.SetAttribute("time", FormatCotTime(nowUtc));
		evt.SetAttribute("start", FormatCotTime(nowUtc));
		evt.SetAttribute("stale", FormatCotTime(nowUtc.AddMinutes(1.0)));
		doc.AppendChild(evt);
		XmlElement point = doc.CreateElement("point");
		point.SetAttribute("lat", "0.0");
		point.SetAttribute("lon", "0.0");
		point.SetAttribute("hae", "9999999.0");
		point.SetAttribute("ce", "9999999.0");
		point.SetAttribute("le", "9999999.0");
		evt.AppendChild(point);
		XmlElement detail = doc.CreateElement("detail");
		evt.AppendChild(detail);
		XmlElement request = doc.CreateElement(DetailElement);
		request.SetAttribute("version", FormatVersion);
		request.SetAttribute("sender", LocalSenderUid);
		detail.AppendChild(request);
		return doc;
	}

	/// <summary>True when <paramref name="message"/> is another operator's request for shared callsigns.</summary>
	public bool IsRequestFromOther(XmlDocument message)
	{
		if (message?.DocumentElement == null || message.DocumentElement.Name != "event" || message.DocumentElement.GetAttribute("type") != RequestCotType)
		{
			return false;
		}
		string senderUid = (message.DocumentElement.SelectSingleNode("detail/" + DetailElement) is XmlElement request) ? NormalizeSenderUid(request.GetAttribute("sender")) : null;
		return senderUid != null && senderUid != LocalSenderUid;
	}

	/// <summary>True when <paramref name="message"/> is a callsign-sharing event, whoever sent it.</summary>
	public static bool IsSharingMessage(XmlDocument message)
	{
		return message?.DocumentElement != null && message.DocumentElement.Name == "event" && message.DocumentElement.GetAttribute("type") == CotType;
	}

	/// <summary>
	/// Applies another operator's event: replaces everything they shared before. Returns false for
	/// our own events and for anything malformed, which is ignored.
	/// </summary>
	public bool TryApply(XmlDocument message, DateTimeOffset nowUtc)
	{
		if (!IsSharingMessage(message) || !(message.DocumentElement.SelectSingleNode("detail/" + DetailElement) is XmlElement list) || list.GetAttribute("version") != FormatVersion)
		{
			return false;
		}
		string senderUid = NormalizeSenderUid(list.GetAttribute("sender"));
		// The event UID is derived from the sender, so one operator can't overwrite another's list.
		if (senderUid == null || senderUid == LocalSenderUid || message.DocumentElement.GetAttribute("uid") != UidPrefix + senderUid)
		{
			return false;
		}
		Sender sender = new Sender
		{
			Uid = senderUid,
			Label = NormalizeSenderLabel(list.GetAttribute("from")) ?? "another operator",
			ReceivedUtc = nowUtc
		};
		foreach (XmlNode child in list.ChildNodes)
		{
			if (sender.Entries.Count >= OpenMANET_CallsignStore.MaximumEntries)
			{
				break;
			}
			if (!(child is XmlElement node) || node.Name != "node")
			{
				continue;
			}
			string mac = OpenMANET_CallsignStore.NormalizeMac(node.GetAttribute("mac"));
			string host = OpenMANET_CallsignStore.NormalizeHostname(node.GetAttribute("host"));
			string callsign = OpenMANET_CallsignStore.Normalize(node.GetAttribute("callsign"));
			if (callsign == null || (mac == null && host == null))
			{
				continue;
			}
			DateTimeOffset updated = OpenMANET_CallsignStore.ParseTime(node.GetAttribute("updated"), nowUtc);
			// A clock running ahead mustn't let one operator win every conflict.
			sender.Entries.Add(new OpenMANET_CallsignEntry { Mac = mac, Hostname = host, Callsign = callsign, UpdatedUtc = (updated > nowUtc) ? nowUtc : updated, FromNodeOperator = node.GetAttribute("operator") == "1" });
		}
		bool changed;
		lock (_sync)
		{
			_senders.TryGetValue(senderUid, out Sender previous);
			changed = previous == null ? sender.Entries.Count > 0 : (previous.Label != sender.Label || !SameEntries(previous.Entries, sender.Entries));
			if (sender.Entries.Count == 0)
			{
				_senders.Remove(senderUid);
			}
			else
			{
				_senders[senderUid] = sender;
			}
			Prune(nowUtc);
			if (changed)
			{
				SaveLocked();
			}
		}
		if (changed)
		{
			Received?.Invoke(this, new OpenMANET_CallsignsReceivedEventArgs { SharedBy = sender.Label, Count = sender.Entries.Count });
		}
		return changed;
	}

	public void Load()
	{
		lock (_sync)
		{
			_senders.Clear();
			try
			{
				if (!File.Exists(_filePath) || new FileInfo(_filePath).Length > MaximumFileBytes)
				{
					return;
				}
				Sender current = null;
				foreach (string line in File.ReadAllLines(_filePath, Encoding.UTF8))
				{
					string[] parts = line.Split('\t');
					if (parts.Length == 4 && parts[0] == "S")
					{
						string uid = NormalizeSenderUid(parts[1]);
						current = (uid == null || uid == LocalSenderUid || _senders.Count >= MaximumSenders) ? null : new Sender
						{
							Uid = uid,
							Label = NormalizeSenderLabel(parts[2]) ?? "another operator",
							ReceivedUtc = OpenMANET_CallsignStore.ParseTime(parts[3], DateTimeOffset.UtcNow)
						};
						if (current != null)
						{
							_senders[uid] = current;
						}
					}
					else if ((parts.Length == 5 || parts.Length == 6) && parts[0] == "N" && current != null && current.Entries.Count < OpenMANET_CallsignStore.MaximumEntries)
					{
						string mac = OpenMANET_CallsignStore.NormalizeMac(parts[1]);
						string host = OpenMANET_CallsignStore.NormalizeHostname(parts[2]);
						string callsign = OpenMANET_CallsignStore.Normalize(parts[3]);
						if (callsign != null && (mac != null || host != null))
						{
							current.Entries.Add(new OpenMANET_CallsignEntry { Mac = mac, Hostname = host, Callsign = callsign, UpdatedUtc = OpenMANET_CallsignStore.ParseTime(parts[4], current.ReceivedUtc), FromNodeOperator = parts.Length == 6 && parts[5] == "1" });
						}
					}
				}
				Prune(DateTimeOffset.UtcNow);
			}
			catch
			{
				// Shared names are a convenience; a damaged file just starts empty.
				_senders.Clear();
			}
		}
	}

	/// <summary>Forgets every callsign received from other operators.</summary>
	public void Clear()
	{
		lock (_sync)
		{
			_senders.Clear();
			SaveLocked();
		}
	}

	private void Prune(DateTimeOffset nowUtc)
	{
		foreach (Sender expired in _senders.Values.Where((Sender s) => s.Entries.Count == 0 || nowUtc - s.ReceivedUtc > SenderRetention).ToList())
		{
			_senders.Remove(expired.Uid);
		}
		while (_senders.Count > MaximumSenders)
		{
			_senders.Remove(_senders.Values.OrderBy((Sender s) => s.ReceivedUtc).First().Uid);
		}
	}

	private void SaveLocked()
	{
		try
		{
			string directory = Path.GetDirectoryName(_filePath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}
			List<string> lines = new List<string>();
			foreach (Sender sender in _senders.Values)
			{
				lines.Add("S\t" + sender.Uid + "\t" + sender.Label + "\t" + OpenMANET_CallsignStore.FormatTime(sender.ReceivedUtc));
				lines.AddRange(sender.Entries.Select((OpenMANET_CallsignEntry e) => "N\t" + (e.Mac ?? string.Empty) + "\t" + (e.Hostname ?? string.Empty) + "\t" + e.Callsign + "\t" + OpenMANET_CallsignStore.FormatTime(e.UpdatedUtc) + (e.FromNodeOperator ? "\t1" : string.Empty)));
			}
			File.WriteAllLines(_filePath, lines, new UTF8Encoding(false));
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			// Keep them in memory; they'll be rebroadcast anyway.
		}
	}

	private static bool SameEntries(List<OpenMANET_CallsignEntry> a, List<OpenMANET_CallsignEntry> b)
	{
		if (a.Count != b.Count)
		{
			return false;
		}
		for (int i = 0; i < a.Count; i++)
		{
			if (a[i].Mac != b[i].Mac || a[i].Hostname != b[i].Hostname || a[i].Callsign != b[i].Callsign || a[i].FromNodeOperator != b[i].FromNodeOperator)
			{
				return false;
			}
		}
		return true;
	}

	private static string NormalizeSenderUid(string uid)
	{
		if (string.IsNullOrWhiteSpace(uid))
		{
			return null;
		}
		string trimmed = uid.Trim();
		if (trimmed.Length > MaximumSenderUidLength)
		{
			return null;
		}
		foreach (char c in trimmed)
		{
			if (!(c < 128 && char.IsLetterOrDigit(c)) && c != '-' && c != '_' && c != '.' && c != ':')
			{
				return null;
			}
		}
		return trimmed;
	}

	/// <summary>The sharing operator's TAK callsign, trimmed to safe display characters.</summary>
	private static string NormalizeSenderLabel(string label)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			return null;
		}
		StringBuilder builder = new StringBuilder();
		foreach (char c in label.Trim())
		{
			if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.' || c == '/' || c == '#')
			{
				builder.Append(c);
			}
			if (builder.Length >= MaximumSenderLabelLength)
			{
				break;
			}
		}
		string result = builder.ToString().Trim();
		return (result.Length == 0) ? null : result;
	}

	private static string FormatCotTime(DateTime utc)
	{
		return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
	}
}

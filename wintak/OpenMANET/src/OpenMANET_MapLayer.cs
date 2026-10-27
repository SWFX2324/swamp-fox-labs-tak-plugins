using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Xml;
using WinTak.CursorOnTarget.Services;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>
/// Draws mesh nodes and links on the WinTAK map as locally processed Cursor-on-Target. Events go
/// through WinTAK's internal processing path only: they are never sent to a TAK server or other
/// clients, and they carry no archive flag, so WinTAK doesn't keep them after they go stale.
/// </summary>
internal sealed class OpenMANET_MapLayer
{
	// Friendly ground unit > combat support > signal > radio (MIL-STD-2525).
	private const string NodeCotType = "a-f-G-U-U-S-R";

	private const string LinkCotType = "u-d-f";

	private const string DeleteCotType = "t-x-d-d";

	private const string UidPrefix = "openmanet-";

	// Refreshed every poll, so a node only goes stale on the map if polling stops.
	private static readonly TimeSpan LiveStaleAfter = TimeSpan.FromMinutes(3.0);

	private const int GoodRssiDbm = -67;

	private const int FairRssiDbm = -75;

	private const double GoodLinkMbps = 10.0;

	private const double FairLinkMbps = 3.0;

	private static readonly int GoodColor = unchecked((int)0xFF3DD68C);

	private static readonly int FairColor = unchecked((int)0xFFF2B54A);

	private static readonly int PoorColor = unchecked((int)0xFFF26D6D);

	private static readonly int BlosColor = unchecked((int)0xFFFF8C00);

	private static readonly int UnknownColor = unchecked((int)0xFF8A93A6);

	private readonly HashSet<string> _publishedUids = new HashSet<string>(StringComparer.Ordinal);

	private sealed class Endpoint
	{
		public OpenMANET_NodeInfo Node { get; set; }

		public string Key { get; set; }
	}

	public bool IsAvailable => OpenMANET_PluginHost.CotSender != null;

	/// <summary>Publishes markers for nodes with a position and lines for links between them, and removes the rest.</summary>
	public void Update(IReadOnlyList<OpenMANET_NodeInfo> nodes, OpenMANET_TopologyInfo topology)
	{
		ICotMessageSender sender = OpenMANET_PluginHost.CotSender;
		if (sender == null || nodes == null)
		{
			return;
		}
		DateTime now = DateTime.UtcNow;
		HashSet<string> current = new HashSet<string>(StringComparer.Ordinal);
		Dictionary<string, Endpoint> endpointsByMac = BuildEndpointIndex(nodes, topology);
		foreach (OpenMANET_NodeInfo node in nodes)
		{
			if (node.HasPosition)
			{
				string uid = NodeUid(node);
				current.Add(uid);
				Publish(sender, CreateNodeEvent(uid, node, now));
			}
		}
		if (topology != null)
		{
			foreach (OpenMANET_TopologyEdgeInfo edge in topology.Edges)
			{
				if (!TryResolve(endpointsByMac, edge.FromMac, out Endpoint from) || !TryResolve(endpointsByMac, edge.ToMac, out Endpoint to) || from.Key == to.Key || !from.Node.HasPosition || !to.Node.HasPosition)
				{
					continue;
				}
				string uid = LinkUid(from.Key, to.Key);
				if (current.Add(uid))
				{
					Publish(sender, CreateLinkEvent(uid, from.Node, to.Node, edge, now));
				}
			}
		}
		foreach (string uid in new List<string>(_publishedUids))
		{
			if (!current.Contains(uid))
			{
				Publish(sender, CreateDeleteEvent(uid, now));
				_publishedUids.Remove(uid);
			}
		}
		_publishedUids.UnionWith(current);
	}

	/// <summary>Removes everything this layer drew. Synchronous so it completes during WinTAK shutdown.</summary>
	public void Clear(bool synchronous)
	{
		ICotMessageSender sender = OpenMANET_PluginHost.CotSender;
		if (sender == null)
		{
			_publishedUids.Clear();
			return;
		}
		DateTime now = DateTime.UtcNow;
		foreach (string uid in _publishedUids)
		{
			XmlDocument delete = CreateDeleteEvent(uid, now);
			if (synchronous)
			{
				try
				{
					sender.SendInternal(delete);
				}
				catch
				{
					// Best effort during shutdown.
				}
			}
			else
			{
				Publish(sender, delete);
			}
		}
		_publishedUids.Clear();
	}

	private static void Publish(ICotMessageSender sender, XmlDocument cot)
	{
		Task<bool> task;
		try
		{
			task = sender.ProcessInternalAsync(cot);
		}
		catch
		{
			return;
		}
		// Observe failures so a bad event can't surface as an unobserved task exception.
		task?.ContinueWith((Task<bool> t) => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
	}

	private static string NodeUid(OpenMANET_NodeInfo node)
	{
		return UidPrefix + "node-" + NodeKey(node);
	}

	private static string LinkUid(string keyA, string keyB)
	{
		return (string.CompareOrdinal(keyA, keyB) < 0) ? (UidPrefix + "link-" + keyA + "-" + keyB) : (UidPrefix + "link-" + keyB + "-" + keyA);
	}

	private static string NodeKey(OpenMANET_NodeInfo node)
	{
		string mac = node.MacAddress;
		string raw = (!string.IsNullOrWhiteSpace(mac) && mac != "--") ? mac : (node.Name ?? "unknown");
		// Keep UIDs to characters every TAK client accepts.
		char[] chars = raw.Trim().ToLowerInvariant().ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			if (!char.IsLetterOrDigit(chars[i]))
			{
				chars[i] = '_';
			}
		}
		return new string(chars);
	}

	private static Dictionary<string, Endpoint> BuildEndpointIndex(IReadOnlyList<OpenMANET_NodeInfo> nodes, OpenMANET_TopologyInfo topology)
	{
		Dictionary<string, Endpoint> byMac = new Dictionary<string, Endpoint>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, Endpoint> byHost = new Dictionary<string, Endpoint>(StringComparer.OrdinalIgnoreCase);
		foreach (OpenMANET_NodeInfo node in nodes)
		{
			Endpoint endpoint = new Endpoint { Node = node, Key = NodeKey(node) };
			AddMac(byMac, node.MacAddress, endpoint);
			AddMac(byMac, node.LinkHardwareAddress, endpoint);
			if (!string.IsNullOrWhiteSpace(node.Name))
			{
				byHost[node.Name.Trim()] = endpoint;
			}
		}
		// Topology links name nodes by whichever interface MAC carried them; map those back
		// through the topology's hostname and secondary MACs.
		if (topology != null)
		{
			foreach (OpenMANET_TopologyNodeInfo topologyNode in topology.Nodes)
			{
				if (string.IsNullOrWhiteSpace(topologyNode.Hostname) || !byHost.TryGetValue(topologyNode.Hostname.Trim(), out Endpoint endpoint))
				{
					continue;
				}
				AddMac(byMac, topologyNode.MacAddress, endpoint);
				foreach (string mac in topologyNode.SecondaryMacs)
				{
					AddMac(byMac, mac, endpoint);
				}
			}
		}
		return byMac;
	}

	private static void AddMac(Dictionary<string, Endpoint> byMac, string mac, Endpoint endpoint)
	{
		if (!string.IsNullOrWhiteSpace(mac) && mac.Trim() != "--" && !byMac.ContainsKey(mac.Trim()))
		{
			byMac[mac.Trim()] = endpoint;
		}
	}

	private static bool TryResolve(Dictionary<string, Endpoint> byMac, string mac, out Endpoint endpoint)
	{
		endpoint = null;
		return !string.IsNullOrWhiteSpace(mac) && byMac.TryGetValue(mac.Trim(), out endpoint);
	}

	private static XmlDocument CreateNodeEvent(string uid, OpenMANET_NodeInfo node, DateTime now)
	{
		// A node the plugin considers stale is published already stale, so WinTAK dims it.
		DateTime stale = node.IsOnline ? now.Add(LiveStaleAfter) : now;
		XmlDocument doc = CreateEvent(uid, NodeCotType, "m-g", now, stale, node.Latitude.Value, node.Longitude.Value, node.Altitude);
		XmlElement detail = (XmlElement)doc.DocumentElement.SelectSingleNode("detail");
		AddElement(detail, "contact", "callsign", node.DisplayName);
		AddElement(detail, "precisionlocation", "geopointsrc", "GPS", "altsrc", node.Altitude.HasValue ? "GPS" : "???");
		XmlElement remarks = doc.CreateElement("remarks");
		remarks.InnerText = "OpenMANET " + (node.HasCallsign ? (node.Name + " · ") : string.Empty) + node.StatusDisplay + " · " + node.RoleDisplay + " · " + node.HopsDisplay + ((node.Rssi.HasValue || node.CurrentRssi.HasValue) ? (" · " + (node.CurrentRssi ?? node.Rssi).Value.ToString(CultureInfo.InvariantCulture) + " dBm") : string.Empty) + " · " + node.Address;
		detail.AppendChild(remarks);
		return doc;
	}

	private static XmlDocument CreateLinkEvent(string uid, OpenMANET_NodeInfo a, OpenMANET_NodeInfo b, OpenMANET_TopologyEdgeInfo edge, DateTime now)
	{
		double midLat = (a.Latitude.Value + b.Latitude.Value) / 2.0;
		double midLon = (a.Longitude.Value + b.Longitude.Value) / 2.0;
		XmlDocument doc = CreateEvent(uid, LinkCotType, "h-e", now, now.Add(LiveStaleAfter), midLat, midLon, null);
		XmlElement detail = (XmlElement)doc.DocumentElement.SelectSingleNode("detail");
		AddElement(detail, "link", "point", FormatPoint(a));
		AddElement(detail, "link", "point", FormatPoint(b));
		AddElement(detail, "strokeColor", "value", LinkColor(a, b, edge).ToString(CultureInfo.InvariantCulture));
		AddElement(detail, "strokeWeight", "value", edge.IsOnMyPath ? "4.0" : "3.0");
		if (edge.IsBlos)
		{
			AddElement(detail, "strokeStyle", "value", "dashed");
		}
		AddElement(detail, "labels_on", "value", "false");
		AddElement(detail, "contact", "callsign", a.DisplayName + " - " + b.DisplayName);
		XmlElement remarks = doc.CreateElement("remarks");
		remarks.InnerText = "OpenMANET link · " + edge.TypeDisplay + " · Metric " + edge.MetricDisplay + (edge.IsOnMyPath ? " · Active path" : string.Empty);
		detail.AppendChild(remarks);
		return doc;
	}

	/// <summary>
	/// Colors a link by RSSI when one end is the connected node and the other is its measured
	/// neighbor; otherwise by the routing metric, the link's estimated throughput in Mbps.
	/// </summary>
	private static int LinkColor(OpenMANET_NodeInfo a, OpenMANET_NodeInfo b, OpenMANET_TopologyEdgeInfo edge)
	{
		if (edge.IsBlos)
		{
			return BlosColor;
		}
		OpenMANET_NodeInfo neighbor = a.IsSelf ? b : (b.IsSelf ? a : null);
		int? rssi = neighbor?.CurrentRssi ?? neighbor?.Rssi;
		if (rssi.HasValue)
		{
			return (rssi.Value >= GoodRssiDbm) ? GoodColor : ((rssi.Value >= FairRssiDbm) ? FairColor : PoorColor);
		}
		if (!(edge.Metric > 0.0))
		{
			return UnknownColor;
		}
		return (edge.Metric >= GoodLinkMbps) ? GoodColor : ((edge.Metric >= FairLinkMbps) ? FairColor : PoorColor);
	}

	private static XmlDocument CreateDeleteEvent(string targetUid, DateTime now)
	{
		XmlDocument doc = CreateEvent(UidPrefix + "delete-" + Guid.NewGuid().ToString("N"), DeleteCotType, "h-g-i-g-o", now, now.AddMinutes(1.0), 0.0, 0.0, null);
		XmlElement detail = (XmlElement)doc.DocumentElement.SelectSingleNode("detail");
		AddElement(detail, "link", "uid", targetUid, "relation", "none", "type", "none");
		detail.AppendChild(doc.CreateElement("__forcedelete"));
		return doc;
	}

	private static XmlDocument CreateEvent(string uid, string type, string how, DateTime time, DateTime stale, double lat, double lon, double? hae)
	{
		XmlDocument doc = new XmlDocument();
		XmlElement evt = doc.CreateElement("event");
		evt.SetAttribute("version", "2.0");
		evt.SetAttribute("uid", uid);
		evt.SetAttribute("type", type);
		evt.SetAttribute("how", how);
		evt.SetAttribute("time", FormatTime(time));
		evt.SetAttribute("start", FormatTime(time));
		evt.SetAttribute("stale", FormatTime(stale));
		doc.AppendChild(evt);
		XmlElement point = doc.CreateElement("point");
		point.SetAttribute("lat", lat.ToString("0.0000000", CultureInfo.InvariantCulture));
		point.SetAttribute("lon", lon.ToString("0.0000000", CultureInfo.InvariantCulture));
		point.SetAttribute("hae", hae.HasValue ? hae.Value.ToString("0.0", CultureInfo.InvariantCulture) : "9999999.0");
		point.SetAttribute("ce", "9999999.0");
		point.SetAttribute("le", "9999999.0");
		evt.AppendChild(point);
		evt.AppendChild(doc.CreateElement("detail"));
		return doc;
	}

	private static void AddElement(XmlElement parent, string name, params string[] attributes)
	{
		XmlElement element = parent.OwnerDocument.CreateElement(name);
		for (int i = 0; i + 1 < attributes.Length; i += 2)
		{
			element.SetAttribute(attributes[i], attributes[i + 1]);
		}
		parent.AppendChild(element);
	}

	private static string FormatPoint(OpenMANET_NodeInfo node)
	{
		return node.Latitude.Value.ToString("0.0000000", CultureInfo.InvariantCulture) + "," + node.Longitude.Value.ToString("0.0000000", CultureInfo.InvariantCulture);
	}

	private static string FormatTime(DateTime utc)
	{
		return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
	}
}

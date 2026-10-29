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

	// WinTAK saves drawn links in its map state and restores them at startup, after this layer has
	// forgotten them. Every UID this layer could have drawn is deleted once, the first time it's seen.
	private readonly HashSet<string> _sweptUids = new HashSet<string>(StringComparer.Ordinal);

	// Lower-case names of the nodes this layer has a marker for. Replaced whole on each update and
	// read on WinTAK's CoT thread to recognize the markers nodes broadcast for themselves.
	private static volatile HashSet<string> _markedNodeNames = new HashSet<string>(StringComparer.Ordinal);

	// Nodes drawn as an operator at the last update, for the hysteresis in FindOperatorAt.
	private HashSet<string> _mergedNodeKeys = new HashSet<string>(StringComparer.Ordinal);

	// Operator marker UID -> the nearest node drawn as that operator. Replaced whole; read by the radial menu.
	private static volatile Dictionary<string, string> _nodeKeyByOperator = new Dictionary<string, string>(StringComparer.Ordinal);

	private static readonly object HiddenUidsLock = new object();

	// Node-broadcast marker UIDs already removed from the map; each is deleted once, not per broadcast.
	private static readonly HashSet<string> _hiddenNodeMarkerUids = new HashSet<string>(StringComparer.Ordinal);

	private sealed class Endpoint
	{
		public OpenMANET_NodeInfo Node { get; set; }

		public string Key { get; set; }
	}

	public bool IsAvailable => OpenMANET_PluginHost.CotSender != null;

	/// <summary>Publishes markers for nodes with a position and lines for links between them, and removes the rest.</summary>
	public void Update(IReadOnlyList<OpenMANET_NodeInfo> nodes, OpenMANET_TopologyInfo topology)
	{
		Update(nodes, topology, null, 0);
	}

	/// <summary>
	/// As <see cref="Update(IReadOnlyList{OpenMANET_NodeInfo}, OpenMANET_TopologyInfo)"/>, but a node with
	/// an operator within <paramref name="mergeMeters"/> is drawn as that operator: no radio icon of its
	/// own, and its link lines end at the operator's marker.
	/// </summary>
	public void Update(IReadOnlyList<OpenMANET_NodeInfo> nodes, OpenMANET_TopologyInfo topology, IReadOnlyList<OpenMANET_OperatorPosition> operators, int mergeMeters)
	{
		Update(nodes, topology, operators, mergeMeters, drawUnattendedNodes: true);
	}

	/// <summary>
	/// As above; without <paramref name="drawUnattendedNodes"/>, a node with nobody at it isn't drawn at all,
	/// so the map shows only TAK users' own markers and the links between them.
	/// </summary>
	public void Update(IReadOnlyList<OpenMANET_NodeInfo> nodes, OpenMANET_TopologyInfo topology, IReadOnlyList<OpenMANET_OperatorPosition> operators, int mergeMeters, bool drawUnattendedNodes)
	{
		ICotMessageSender sender = OpenMANET_PluginHost.CotSender;
		if (sender == null || nodes == null)
		{
			return;
		}
		DateTime now = DateTime.UtcNow;
		HashSet<string> current = new HashSet<string>(StringComparer.Ordinal);
		Dictionary<string, Endpoint> endpointsByMac = BuildEndpointIndex(nodes, topology);
		HashSet<string> markedNames = new HashSet<string>(StringComparer.Ordinal);
		Dictionary<string, OpenMANET_OperatorPosition> mergedInto = new Dictionary<string, OpenMANET_OperatorPosition>(StringComparer.Ordinal);
		Dictionary<string, string> nodeKeyByOperator = new Dictionary<string, string>(StringComparer.Ordinal);
		Dictionary<string, double> nearestByOperator = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach (OpenMANET_NodeInfo node in nodes)
		{
			if (node.HasPosition)
			{
				string key = NodeKey(node);
				OpenMANET_OperatorPosition holder = FindOperatorAt(node, operators, mergeMeters, _mergedNodeKeys.Contains(key), out double distance);
				if (holder != null)
				{
					mergedInto[key] = holder;
					if (!nearestByOperator.TryGetValue(holder.Uid, out double nearest) || distance < nearest)
					{
						nearestByOperator[holder.Uid] = distance;
						nodeKeyByOperator[holder.Uid] = key;
					}
				}
				else if (drawUnattendedNodes)
				{
					string uid = NodeUid(node);
					current.Add(uid);
					Publish(sender, CreateNodeEvent(uid, node, now));
				}
				// A merged or undrawn node is still placed by the plugin, so its own COT OUT marker stays hidden too.
				if (!string.IsNullOrWhiteSpace(node.Name))
				{
					markedNames.Add(node.Name.Trim().ToLowerInvariant());
				}
			}
		}
		_mergedNodeKeys = new HashSet<string>(mergedInto.Keys, StringComparer.Ordinal);
		_nodeKeyByOperator = nodeKeyByOperator;
		_markedNodeNames = markedNames;
		// A node the plugin stopped drawing gets its own marker back; if it returns, that marker is removed again.
		lock (HiddenUidsLock)
		{
			_hiddenNodeMarkerUids.RemoveWhere((string hidden) => !IsMarked(markedNames, hidden.Trim().ToLowerInvariant()));
		}
		if (topology != null)
		{
			foreach (OpenMANET_TopologyEdgeInfo edge in topology.Edges)
			{
				if (!TryResolve(endpointsByMac, edge.FromMac, out Endpoint from) || !TryResolve(endpointsByMac, edge.ToMac, out Endpoint to) || from.Key == to.Key || !from.Node.HasPosition || !to.Node.HasPosition)
				{
					continue;
				}
				mergedInto.TryGetValue(from.Key, out OpenMANET_OperatorPosition fromHolder);
				mergedInto.TryGetValue(to.Key, out OpenMANET_OperatorPosition toHolder);
				// Two nodes held by the same operator would make a zero-length line.
				if (fromHolder != null && fromHolder == toHolder)
				{
					continue;
				}
				// Without node icons, a line may only end at a marker that's actually on the map.
				if (!drawUnattendedNodes && (fromHolder == null || toHolder == null))
				{
					continue;
				}
				string uid = LinkUid(from.Key, to.Key);
				if (current.Add(uid))
				{
					Publish(sender, CreateLinkEvent(uid, from.Node, to.Node, fromHolder, toHolder, edge, now));
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
		foreach (string uid in CandidateUids(nodes, topology, endpointsByMac))
		{
			if (_sweptUids.Add(uid) && !current.Contains(uid))
			{
				Publish(sender, CreateDeleteEvent(uid, now));
			}
		}
	}

	// Every node marker and link this layer could draw for these nodes, whether or not it does now.
	private static IEnumerable<string> CandidateUids(IReadOnlyList<OpenMANET_NodeInfo> nodes, OpenMANET_TopologyInfo topology, Dictionary<string, Endpoint> endpointsByMac)
	{
		foreach (OpenMANET_NodeInfo node in nodes)
		{
			yield return NodeUid(node);
		}
		if (topology == null)
		{
			yield break;
		}
		foreach (OpenMANET_TopologyEdgeInfo edge in topology.Edges)
		{
			if (TryResolve(endpointsByMac, edge.FromMac, out Endpoint from) && TryResolve(endpointsByMac, edge.ToMac, out Endpoint to) && from.Key != to.Key)
			{
				yield return LinkUid(from.Key, to.Key);
			}
		}
	}

	/// <summary>Removes everything this layer drew. Synchronous so it completes during WinTAK shutdown.</summary>
	public void Clear(bool synchronous)
	{
		// Nodes' own markers show again from their next broadcast.
		_markedNodeNames = new HashSet<string>(StringComparer.Ordinal);
		_mergedNodeKeys = new HashSet<string>(StringComparer.Ordinal);
		_nodeKeyByOperator = new Dictionary<string, string>(StringComparer.Ordinal);
		lock (HiddenUidsLock)
		{
			_hiddenNodeMarkerUids.Clear();
		}
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

	/// <summary>
	/// Returns the lower-case UID of a node's own COT OUT marker, or null when the event
	/// isn't one. openmanetd sends type a-f-G-U-U-S-R in group role "MANET Radio", with the node's
	/// hostname as UID and callsign, suffixed "-MANET" unless the hostname already contains "manet".
	/// </summary>
	public static string NodeOwnMarkerName(XmlDocument message)
	{
		XmlElement root = message?.DocumentElement;
		if (root == null || root.Name != "event" || root.GetAttribute("type") != NodeCotType)
		{
			return null;
		}
		string uid = root.GetAttribute("uid");
		// Our own markers share the type; they're namespaced.
		if (string.IsNullOrWhiteSpace(uid) || uid.Length > 64 || uid.StartsWith(UidPrefix, StringComparison.Ordinal))
		{
			return null;
		}
		if (!(root.SelectSingleNode("detail/__group") is XmlElement group) || group.GetAttribute("role") != "MANET Radio")
		{
			return null;
		}
		return uid.Trim().ToLowerInvariant();
	}

	// A node's marker UID is its hostname plus "-MANET", or the bare hostname if it already contains "manet".
	private static bool IsMarked(HashSet<string> marked, string ownMarkerName)
	{
		return marked.Contains(ownMarkerName) || (ownMarkerName.EndsWith("-manet", StringComparison.Ordinal) && marked.Contains(ownMarkerName.Substring(0, ownMarkerName.Length - "-manet".Length)));
	}

	/// <summary>
	/// True when <paramref name="message"/> is a node's own marker for a node this layer is drawing,
	/// so WinTAK shouldn't draw it too. The first time, also removes that marker if it's already on the map.
	/// </summary>
	public static bool HideNodeOwnMarker(XmlDocument message)
	{
		string name = NodeOwnMarkerName(message);
		if (name == null || !IsMarked(_markedNodeNames, name))
		{
			return false;
		}
		string uid = message.DocumentElement.GetAttribute("uid");
		bool first;
		lock (HiddenUidsLock)
		{
			first = _hiddenNodeMarkerUids.Add(uid);
		}
		ICotMessageSender sender = OpenMANET_PluginHost.CotSender;
		if (first && sender != null)
		{
			Publish(sender, CreateDeleteEvent(uid, DateTime.UtcNow));
		}
		return true;
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

	/// <summary>Returns the node key in one of this layer's marker UIDs, or null for any other map item.</summary>
	public static string NodeKeyFromUid(string uid)
	{
		string prefix = UidPrefix + "node-";
		return (uid != null && uid.StartsWith(prefix, StringComparison.Ordinal) && uid.Length > prefix.Length) ? uid.Substring(prefix.Length) : null;
	}

	/// <summary>A stable key for a node, used in its map UIDs: its MAC, or its hostname without one.</summary>
	public static string NodeKey(OpenMANET_NodeInfo node)
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

	// A link end is drawn at the operator holding that node, when there is one, so it meets the marker shown.
	private static XmlDocument CreateLinkEvent(string uid, OpenMANET_NodeInfo a, OpenMANET_NodeInfo b, OpenMANET_OperatorPosition aHolder, OpenMANET_OperatorPosition bHolder, OpenMANET_TopologyEdgeInfo edge, DateTime now)
	{
		double aLat = aHolder?.Latitude ?? a.Latitude.Value;
		double aLon = aHolder?.Longitude ?? a.Longitude.Value;
		double bLat = bHolder?.Latitude ?? b.Latitude.Value;
		double bLon = bHolder?.Longitude ?? b.Longitude.Value;
		XmlDocument doc = CreateEvent(uid, LinkCotType, "h-e", now, now.Add(LiveStaleAfter), (aLat + bLat) / 2.0, (aLon + bLon) / 2.0, null);
		XmlElement detail = (XmlElement)doc.DocumentElement.SelectSingleNode("detail");
		AddElement(detail, "link", "point", FormatPoint(aLat, aLon));
		AddElement(detail, "link", "point", FormatPoint(bLat, bLon));
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

	private static string FormatPoint(double latitude, double longitude)
	{
		return latitude.ToString("0.0000000", CultureInfo.InvariantCulture) + "," + longitude.ToString("0.0000000", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// The operator nearest a node, if within <paramref name="mergeMeters"/>. A node already merged
	/// stays merged out to 1.5 times that, so GPS jitter at the edge doesn't make its icon flicker.
	/// </summary>
	private static OpenMANET_OperatorPosition FindOperatorAt(OpenMANET_NodeInfo node, IReadOnlyList<OpenMANET_OperatorPosition> operators, int mergeMeters, bool wasMerged, out double distance)
	{
		distance = double.MaxValue;
		if (operators == null || mergeMeters <= 0)
		{
			return null;
		}
		double limit = wasMerged ? mergeMeters * 1.5 : mergeMeters;
		OpenMANET_OperatorPosition nearest = null;
		foreach (OpenMANET_OperatorPosition candidate in operators)
		{
			double d = OpenMANET_Operators.DistanceMeters(node.Latitude.Value, node.Longitude.Value, candidate.Latitude, candidate.Longitude);
			if (d <= limit && d < distance)
			{
				distance = d;
				nearest = candidate;
			}
		}
		return nearest;
	}

	/// <summary>The node drawn as the operator with this marker UID, for the radial menu; null if none.</summary>
	public static string NodeKeyForOperator(string operatorUid)
	{
		return (operatorUid != null && _nodeKeyByOperator.TryGetValue(operatorUid, out string key)) ? key : null;
	}

	private static string FormatTime(DateTime utc)
	{
		return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
	}
}

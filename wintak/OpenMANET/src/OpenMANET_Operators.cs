using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>A TAK user's last reported position: this operator or another one on the network.</summary>
internal sealed class OpenMANET_OperatorPosition
{
	public string Uid { get; set; }

	public double Latitude { get; set; }

	public double Longitude { get; set; }
}

/// <summary>
/// Where TAK users are, so a node with an operator standing at it can be drawn as that operator
/// instead of a second icon on the same spot. Other operators come from the position reports
/// WinTAK passes to the plugin; this operator's own position comes from WinTAK's location service.
/// </summary>
internal static class OpenMANET_Operators
{
	public const int MaximumOperators = 512;

	private static readonly object OperatorsLock = new object();

	private static readonly Dictionary<string, Reported> _operators = new Dictionary<string, Reported>(StringComparer.Ordinal);

	// This operator's own position report (CoT), from WinTAK's location service. Replaceable in tests.
	private static Func<XmlDocument> _selfPositionProvider;

	private sealed class Reported
	{
		public OpenMANET_OperatorPosition Position { get; set; }

		public DateTime StaleUtc { get; set; }
	}

	public static void SetSelfPositionProvider(Func<XmlDocument> provider)
	{
		_selfPositionProvider = provider;
	}

	/// <summary>
	/// Records another operator's position report. Returns false for anything that isn't one: a TAK
	/// user's report is an a-* event with a contact callsign and a takv (client) element.
	/// </summary>
	public static bool TryRecord(XmlDocument message, DateTime nowUtc)
	{
		if (!TryParse(message, out OpenMANET_OperatorPosition position, out DateTime staleUtc) || staleUtc <= nowUtc)
		{
			return false;
		}
		lock (OperatorsLock)
		{
			if (!_operators.ContainsKey(position.Uid) && _operators.Count >= MaximumOperators)
			{
				RemoveStale(nowUtc);
				if (_operators.Count >= MaximumOperators)
				{
					return false;
				}
			}
			_operators[position.Uid] = new Reported { Position = position, StaleUtc = staleUtc };
		}
		return true;
	}

	/// <summary>Every operator with a current position, this one first.</summary>
	public static List<OpenMANET_OperatorPosition> Current(DateTime nowUtc)
	{
		List<OpenMANET_OperatorPosition> result = new List<OpenMANET_OperatorPosition>();
		try
		{
			if (_selfPositionProvider != null && TryParse(_selfPositionProvider(), out OpenMANET_OperatorPosition self, out DateTime _, requireClient: false))
			{
				result.Add(self);
			}
		}
		catch
		{
			// Without our own position, nodes just aren't merged into our marker.
		}
		string selfUid = (result.Count > 0) ? result[0].Uid : null;
		lock (OperatorsLock)
		{
			RemoveStale(nowUtc);
			result.AddRange(_operators.Values.Select((Reported r) => r.Position).Where((OpenMANET_OperatorPosition p) => p.Uid != selfUid).ToList());
		}
		return result;
	}

	public static void Clear()
	{
		lock (OperatorsLock)
		{
			_operators.Clear();
		}
	}

	/// <summary>Great-circle distance in meters.</summary>
	public static double DistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
	{
		const double EarthRadius = 6371008.8;
		double dLat = (latitude2 - latitude1) * Math.PI / 180.0;
		double dLon = (longitude2 - longitude1) * Math.PI / 180.0;
		double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(latitude1 * Math.PI / 180.0) * Math.Cos(latitude2 * Math.PI / 180.0) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
		return 2 * EarthRadius * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
	}

	private static void RemoveStale(DateTime nowUtc)
	{
		foreach (string uid in _operators.Where((KeyValuePair<string, Reported> o) => o.Value.StaleUtc <= nowUtc).Select((KeyValuePair<string, Reported> o) => o.Key).ToList())
		{
			_operators.Remove(uid);
		}
	}

	private static bool TryParse(XmlDocument message, out OpenMANET_OperatorPosition position, out DateTime staleUtc, bool requireClient = true)
	{
		position = null;
		staleUtc = DateTime.MinValue;
		XmlElement root = message?.DocumentElement;
		if (root == null || root.Name != "event" || !root.GetAttribute("type").StartsWith("a-", StringComparison.Ordinal))
		{
			return false;
		}
		string uid = root.GetAttribute("uid");
		if (string.IsNullOrWhiteSpace(uid) || uid.Length > 128 || uid.StartsWith("openmanet-", StringComparison.Ordinal))
		{
			return false;
		}
		// Nodes' own COT OUT markers look like TAK users (contact and takv); they're radios, not operators.
		if (OpenMANET_MapLayer.NodeOwnMarkerName(message) != null)
		{
			return false;
		}
		if (requireClient && (!(root.SelectSingleNode("detail/contact") is XmlElement contact) || string.IsNullOrWhiteSpace(contact.GetAttribute("callsign")) || root.SelectSingleNode("detail/takv") == null))
		{
			return false;
		}
		if (!(root.SelectSingleNode("point") is XmlElement point)
			|| !double.TryParse(point.GetAttribute("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude)
			|| !double.TryParse(point.GetAttribute("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude)
			|| double.IsNaN(latitude) || double.IsNaN(longitude) || latitude < -90.0 || latitude > 90.0 || longitude < -180.0 || longitude > 180.0
			// 0,0 is what a TAK client reports without a position.
			|| (Math.Abs(latitude) < 1E-07 && Math.Abs(longitude) < 1E-07))
		{
			return false;
		}
		DateTime.TryParse(root.GetAttribute("stale"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out staleUtc);
		position = new OpenMANET_OperatorPosition { Uid = uid, Latitude = latitude, Longitude = longitude };
		return true;
	}
}

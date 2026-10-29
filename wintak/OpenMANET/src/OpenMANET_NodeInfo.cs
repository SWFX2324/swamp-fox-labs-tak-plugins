using System;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_NodeInfo
{
	private string _macAddress;

	public string Name { get; set; }

	/// <summary>User-assigned vanity name; the hostname stays available as <see cref="Name"/>.</summary>
	public string Callsign { get; set; }

	public bool HasCallsign => !string.IsNullOrWhiteSpace(Callsign);

	/// <summary>Set when <see cref="Callsign"/> came from another operator rather than this computer.</summary>
	public string CallsignSharedBy { get; set; }

	public string DisplayName
	{
		get
		{
			if (HasCallsign)
			{
				return Callsign;
			}
			return string.IsNullOrWhiteSpace(Name) ? "Unknown node" : Name;
		}
	}

	public string RowSubtitle => (HasCallsign ? (Name + " · ") : string.Empty) + RoleDisplay + " · " + HopsDisplay + " · " + Address;

	public string Address { get; set; }

	public string MacAddress
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(_macAddress))
			{
				return _macAddress.ToUpperInvariant();
			}
			return "--";
		}
		set
		{
			_macAddress = value;
		}
	}

	public bool IsOnline { get; set; }

	/// <summary>The local node's topology has a route to this node.</summary>
	public bool IsRouted { get; set; }

	/// <summary>This node is a direct neighbor heard recently, whether or not a route exists yet.</summary>
	public bool HasLiveLink { get; set; }

	/// <summary>Listed by the node service but not yet reachable since it first appeared.</summary>
	public bool IsDiscovering { get; set; }

	public bool IsSelf { get; set; }

	public bool IsGateway { get; set; }

	public string Segment { get; set; }

	public int? HopsFromSelf { get; set; }

	public string RouteInterface { get; set; }

	public string RemoteGatewayMac { get; set; }

	public bool GossipStale { get; set; }

	public int? GossipAgeSeconds { get; set; }

	public DateTimeOffset? LastSuccessfulPollUtc { get; set; }

	public int? Rssi { get; set; }

	public int? CurrentRssi { get; set; }

	public int? ThroughputKbps { get; set; }

	public long? LinkAgeMilliseconds { get; set; }

	public string NeighborIdentifier { get; set; }

	public string LinkHardwareAddress { get; set; }

	public int DirectLinkCount { get; set; }

	public bool HasDirectLinkMetrics { get; set; }

	/// <summary>Last announced WGS-84 position; null when the node has no GPS fix.</summary>
	public double? Latitude { get; set; }

	public double? Longitude { get; set; }

	/// <summary>Meters above mean sea level, as reported by the node.</summary>
	public double? Altitude { get; set; }

	public bool HasPosition => Latitude.HasValue && Longitude.HasValue;

	public string PositionDisplay
	{
		get
		{
			if (!HasPosition)
			{
				return "No GPS fix";
			}
			return Latitude.Value.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture) + ", " + Longitude.Value.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture);
		}
	}

	public string RoleDisplay
	{
		get
		{
			if (!IsGateway)
			{
				return "Mesh Node";
			}
			return "Gateway";
		}
	}

	public string StatusDisplay
	{
		get
		{
			if (!IsOnline)
			{
				return IsDiscovering ? "Discovering" : "Stale";
			}
			return "Online";
		}
	}

	public string StatusDetailDisplay
	{
		get
		{
			if (IsSelf)
			{
				return "Local node";
			}
			if (IsOnline && !IsRouted)
			{
				return "Direct link up, route pending";
			}
			if (IsOnline)
			{
				return HasLiveLink ? "Routed, direct link up" : "Routed";
			}
			if (IsDiscovering)
			{
				return "Listed, waiting for a route or link";
			}
			return IsRouted ? "Radio link silent, old route still expiring" : "No route or recent link";
		}
	}

	public string Initial
	{
		get
		{
			if (string.IsNullOrWhiteSpace(DisplayName))
			{
				return "?";
			}
			string trimmed = DisplayName.Trim();
			char last = trimmed[trimmed.Length - 1];
			// "Node0" reads better as "0" than "N" when every hostname shares a prefix.
			return char.IsDigit(last) ? last.ToString() : char.ToUpperInvariant(trimmed[0]).ToString();
		}
	}

	/// <summary>Signal strength as 0-4 bars, for the compact node list.</summary>
	public int SignalBars
	{
		get
		{
			int? rssi = CurrentRssi ?? Rssi;
			if (!rssi.HasValue)
			{
				return 0;
			}
			if (rssi.Value >= -55)
			{
				return 4;
			}
			if (rssi.Value >= -67)
			{
				return 3;
			}
			if (rssi.Value >= -75)
			{
				return 2;
			}
			return 1;
		}
	}

	public bool SignalBar1Lit => SignalBars >= 1;

	public bool SignalBar2Lit => SignalBars >= 2;

	public bool SignalBar3Lit => SignalBars >= 3;

	public bool SignalBar4Lit => SignalBars >= 4;

	public string SegmentDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Segment))
			{
				return char.ToUpperInvariant(Segment.Trim()[0]) + Segment.Trim().Substring(1).ToLowerInvariant();
			}
			return "--";
		}
	}

	public string HopsDisplay
	{
		get
		{
			if (IsSelf)
			{
				return "Self";
			}
			if (!HopsFromSelf.HasValue || HopsFromSelf.Value >= 99)
			{
				// A live direct neighbor is one hop away even before the route table says so.
				return HasLiveLink ? "1 Hop" : "Unknown";
			}
			if (HopsFromSelf != 1)
			{
				return $"{HopsFromSelf} Hops";
			}
			return "1 Hop";
		}
	}

	public string RouteInterfaceDisplay
	{
		get
		{
			if (IsSelf)
			{
				return "Self";
			}
			if (string.IsNullOrWhiteSpace(RouteInterface))
			{
				return "--";
			}
			return RouteInterface.Trim().ToLowerInvariant() switch
			{
				"wlan0" => "HaLow", 
				"wlan1" => "2.4 GHz Mesh", 
				"wlan2" => "5 GHz Mesh", 
				"eth0" => "Ethernet", 
				"vxlan0" => "BLOS Tunnel", 
				_ => RouteInterface, 
			};
		}
	}

	public string PathTypeDisplay
	{
		get
		{
			if (IsSelf)
			{
				return "Local";
			}
			if (!string.Equals(Segment, "remote", StringComparison.OrdinalIgnoreCase))
			{
				return "Direct";
			}
			return "BLOS";
		}
	}

	public string TopologyStatusDisplay
	{
		get
		{
			if (!IsOnline)
			{
				return IsDiscovering ? "Discovering" : "Offline";
			}
			if (!IsSelf && (GossipStale || !IsRouted))
			{
				return "Degraded";
			}
			return "Healthy";
		}
	}

	public string RemoteGatewayDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(RemoteGatewayMac))
			{
				return RemoteGatewayMac.ToUpperInvariant();
			}
			return "--";
		}
	}

	public string TopologyAgeDisplay
	{
		get
		{
			if (IsSelf)
			{
				return "0 sec";
			}
			if (!GossipAgeSeconds.HasValue || GossipAgeSeconds.Value < 0)
			{
				return "Unknown";
			}
			return $"{GossipAgeSeconds} sec";
		}
	}

	public string LastPollDisplay
	{
		get
		{
			if (!LastSuccessfulPollUtc.HasValue)
			{
				return "--";
			}
			return LastSuccessfulPollUtc.Value.ToLocalTime().ToString("HH:mm:ss");
		}
	}

	public string RssiDisplay
	{
		get
		{
			if (!Rssi.HasValue)
			{
				return "--";
			}
			return $"{Rssi} dBm";
		}
	}

	public string CurrentRssiDisplay
	{
		get
		{
			if (!CurrentRssi.HasValue)
			{
				return "--";
			}
			return $"{CurrentRssi} dBm";
		}
	}

	public string ThroughputDisplay
	{
		get
		{
			if (!ThroughputKbps.HasValue || ThroughputKbps.Value <= 0)
			{
				return "--";
			}
			if (ThroughputKbps.Value >= 1000)
			{
				return $"{(double)ThroughputKbps.Value / 1000.0:0.0} Mbit/s";
			}
			return $"{ThroughputKbps.Value} kbit/s";
		}
	}

	public string LinkAgeDisplay
	{
		get
		{
			if (!LinkAgeMilliseconds.HasValue || LinkAgeMilliseconds.Value < 0)
			{
				return "--";
			}
			if (LinkAgeMilliseconds.Value < 1000)
			{
				return $"{LinkAgeMilliseconds.Value} ms ago";
			}
			return $"{(double)LinkAgeMilliseconds.Value / 1000.0:0.0} sec ago";
		}
	}

	public string LinkInterfaceDisplay
	{
		get
		{
			if (string.IsNullOrWhiteSpace(NeighborIdentifier))
			{
				return "--";
			}
			int num = -1;
			if (!string.IsNullOrWhiteSpace(Name) && NeighborIdentifier.Length > Name.Length && NeighborIdentifier.StartsWith(Name, StringComparison.OrdinalIgnoreCase) && (NeighborIdentifier[Name.Length] == '.' || NeighborIdentifier[Name.Length] == '_'))
			{
				num = Name.Length;
			}
			if (num < 0)
			{
				num = Math.Max(NeighborIdentifier.LastIndexOf('.'), NeighborIdentifier.LastIndexOf('_'));
			}
			if (num < 0 || num == NeighborIdentifier.Length - 1)
			{
				return NeighborIdentifier;
			}
			string text = NeighborIdentifier.Substring(num + 1);
			return text.ToLowerInvariant() switch
			{
				"wlan0" => "HaLow", 
				"wlan1" => "2.4 GHz Mesh", 
				"wlan2" => "5 GHz Mesh", 
				_ => text, 
			};
		}
	}

	public string LinkHardwareAddressDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(LinkHardwareAddress))
			{
				return LinkHardwareAddress.ToUpperInvariant();
			}
			return "--";
		}
	}

	public string DirectLinkCountDisplay
	{
		get
		{
			if (DirectLinkCount != 1)
			{
				return $"{DirectLinkCount} interfaces";
			}
			return "1 interface";
		}
	}

	public string LinkQualityStatusDisplay
	{
		get
		{
			if (IsSelf)
			{
				return "Local Node";
			}
			if (!HasDirectLinkMetrics || !Rssi.HasValue)
			{
				return "Unavailable";
			}
			if (Rssi.Value >= -55)
			{
				return "Excellent";
			}
			if (Rssi.Value >= -67)
			{
				return "Good";
			}
			if (Rssi.Value >= -75)
			{
				return "Fair";
			}
			return "Poor";
		}
	}
}

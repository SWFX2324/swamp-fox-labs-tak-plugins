using System;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_NodeInfo
{
	private string _macAddress;

	public string Name { get; set; }

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
				return "Stale";
			}
			return "Online";
		}
	}

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
				return "Unknown";
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
				return "Offline";
			}
			if (!IsSelf && GossipStale)
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

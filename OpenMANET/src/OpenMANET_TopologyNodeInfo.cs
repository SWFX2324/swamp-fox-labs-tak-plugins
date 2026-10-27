using System.Collections.Generic;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_TopologyNodeInfo
{
	public string MacAddress { get; set; }

	public List<string> SecondaryMacs { get; set; } = new List<string>();

	public string Hostname { get; set; }

	public string Segment { get; set; }

	public int HopsFromSelf { get; set; }

	public string RouteInterface { get; set; }

	public bool IsSelf { get; set; }

	public bool IsGateway { get; set; }

	public bool GossipStale { get; set; }

	public int GossipAgeSeconds { get; set; }

	public string RemoteGatewayMac { get; set; }

	public string NameDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Hostname))
			{
				return Hostname;
			}
			return "Unknown node";
		}
	}

	public string MacDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(MacAddress))
			{
				return MacAddress.ToUpperInvariant();
			}
			return "--";
		}
	}

	public string RoleDisplay
	{
		get
		{
			if (IsSelf && IsGateway)
			{
				return "Local Gateway";
			}
			if (IsSelf)
			{
				return "Local Node";
			}
			if (IsGateway)
			{
				return "Gateway";
			}
			return "Mesh Node";
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
			if (HopsFromSelf < 0 || HopsFromSelf >= 99)
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

	public string RouteDisplay
	{
		get
		{
			if (IsSelf)
			{
				return "Local";
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

	public string GossipDisplay
	{
		get
		{
			if (!IsSelf)
			{
				if (!GossipStale)
				{
					return $"Current · {GossipAgeSeconds} sec";
				}
				return $"Stale · {GossipAgeSeconds} sec";
			}
			return "Local";
		}
	}
}

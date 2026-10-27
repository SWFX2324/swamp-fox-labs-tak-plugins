using System;
using System.Collections.Generic;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_TopologyInfo
{
	public string SelfMac { get; set; }

	public string SelfHostname { get; set; }

	public string Algorithm { get; set; }

	public DateTimeOffset? CollectedAtUtc { get; set; }

	public int GossipPublished { get; set; }

	public int GossipTotal { get; set; }

	public List<OpenMANET_TopologyNodeInfo> Nodes { get; set; } = new List<OpenMANET_TopologyNodeInfo>();

	public List<OpenMANET_TopologyEdgeInfo> Edges { get; set; } = new List<OpenMANET_TopologyEdgeInfo>();

	public int DirectLinkCount => Edges.FindAll((OpenMANET_TopologyEdgeInfo edge) => !edge.IsBlos).Count;

	public int BlosLinkCount => Edges.FindAll((OpenMANET_TopologyEdgeInfo edge) => edge.IsBlos).Count;

	public int ActivePathCount => Edges.FindAll((OpenMANET_TopologyEdgeInfo edge) => edge.IsOnMyPath).Count;

	public string AlgorithmDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Algorithm))
			{
				return Algorithm;
			}
			return "Unknown";
		}
	}

	public string CollectedAtDisplay
	{
		get
		{
			if (!CollectedAtUtc.HasValue)
			{
				return "--";
			}
			return CollectedAtUtc.Value.ToLocalTime().ToString("HH:mm:ss");
		}
	}

	public string GossipCoverageDisplay
	{
		get
		{
			if (GossipTotal <= 0)
			{
				return "--";
			}
			double num = 100.0 * (double)GossipPublished / (double)GossipTotal;
			return $"{GossipPublished}/{GossipTotal} ({num:0}%)";
		}
	}

	public string HealthDisplay
	{
		get
		{
			if (Nodes.Count == 0)
			{
				return "Offline";
			}
			bool flag = Nodes.Exists((OpenMANET_TopologyNodeInfo node) => node.GossipStale);
			bool flag2 = GossipTotal > 0 && GossipPublished < GossipTotal;
			if (!(flag | flag2))
			{
				return "Healthy";
			}
			return "Degraded";
		}
	}
}

using System.Collections.Generic;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>
/// Spots WinTAK taking its position from the wrong node. A node's NMEA OUT goes to every device in
/// its DHCP lease list, so when one node hands out addresses for the whole mesh, every laptop gets
/// that node's GPS, even while plugged into another node.
/// </summary>
internal static class OpenMANET_GpsCheck
{
	// Close enough that WinTAK's position is that node's fix; allows for the node's own jitter and announce lag.
	public const double MatchMeters = 25.0;

	// While the connected node has a fix, WinTAK must be this far from it before another node is suspected.
	public const double FarFromConnectedMeters = 100.0;

	// Consecutive polls the match must hold before it's reported, so a passing coincidence isn't.
	public const int PollsBeforeReporting = 3;

	/// <summary>
	/// Returns the other node whose GPS WinTAK's position matches, or null. Only suspected when the
	/// connected node has no fix, or has one well away from where WinTAK says this computer is.
	/// </summary>
	public static OpenMANET_NodeInfo FindBorrowedGpsSource(IReadOnlyList<OpenMANET_NodeInfo> nodes, OpenMANET_OperatorPosition self)
	{
		if (nodes == null || self == null)
		{
			return null;
		}
		OpenMANET_NodeInfo connected = null;
		OpenMANET_NodeInfo nearest = null;
		double nearestMeters = double.MaxValue;
		foreach (OpenMANET_NodeInfo node in nodes)
		{
			if (node.IsSelf)
			{
				connected = node;
			}
			else if (node.HasPosition)
			{
				double meters = OpenMANET_Operators.DistanceMeters(self.Latitude, self.Longitude, node.Latitude.Value, node.Longitude.Value);
				if (meters < nearestMeters)
				{
					nearest = node;
					nearestMeters = meters;
				}
			}
		}
		if (connected == null || nearest == null || nearestMeters > MatchMeters)
		{
			return null;
		}
		if (connected.HasPosition && OpenMANET_Operators.DistanceMeters(self.Latitude, self.Longitude, connected.Latitude.Value, connected.Longitude.Value) <= FarFromConnectedMeters)
		{
			return null;
		}
		return nearest;
	}
}

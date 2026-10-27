using System;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_NetworkSummaryInfo
{
	public string InterfaceName { get; set; }

	public string DisplayName { get; set; }

	public string State { get; set; }

	public string Detail { get; set; }

	public long RxBytes { get; set; }

	public long TxBytes { get; set; }

	public string NameDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(DisplayName))
			{
				return DisplayName;
			}
			if (!string.IsNullOrWhiteSpace(InterfaceName))
			{
				return InterfaceName;
			}
			return "Unknown interface";
		}
	}

	public string InterfaceDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(InterfaceName))
			{
				return InterfaceName;
			}
			return "--";
		}
	}

	public string StateDisplay
	{
		get
		{
			switch ((State ?? string.Empty).Trim().ToUpperInvariant())
			{
			case "NETWORK_INTERFACE_STATE_CONNECTED":
			case "CONNECTED":
				return "Connected";
			default:
				return "Not Connected";
			}
		}
	}

	public string DetailDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Detail))
			{
				return Detail;
			}
			return "No additional details";
		}
	}

	public string TrafficDisplay => "RX " + FormatTrafficBytes(RxBytes) + "  ·  TX " + FormatTrafficBytes(TxBytes);

	private static string FormatTrafficBytes(long value)
	{
		if ((double)value >= 1073741824.0)
		{
			return $"{(double)value / 1073741824.0:0.00} GiB";
		}
		if ((double)value >= 1048576.0)
		{
			return $"{(double)value / 1048576.0:0.0} MiB";
		}
		if ((double)value >= 1024.0)
		{
			return $"{(double)value / 1024.0:0.0} KiB";
		}
		return $"{Math.Max(0L, value)} B";
	}
}

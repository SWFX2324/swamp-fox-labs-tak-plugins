namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_TopologyEdgeInfo
{
	public string FromMac { get; set; }

	public string ToMac { get; set; }

	public string FromName { get; set; }

	public string ToName { get; set; }

	public double Metric { get; set; }

	public bool IsBlos { get; set; }

	public bool IsOnMyPath { get; set; }

	public string FromDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(FromName))
			{
				return FromName;
			}
			return ShortMac(FromMac);
		}
	}

	public string ToDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(ToName))
			{
				return ToName;
			}
			return ShortMac(ToMac);
		}
	}

	public string LinkDisplay => FromDisplay + "  →  " + ToDisplay;

	public string TypeDisplay
	{
		get
		{
			if (!IsBlos)
			{
				return "Direct";
			}
			return "BLOS";
		}
	}

	public string MetricDisplay
	{
		get
		{
			if (!(Metric > 0.0))
			{
				return "--";
			}
			return Metric.ToString("0.00");
		}
	}

	public string PathDisplay
	{
		get
		{
			if (!IsOnMyPath)
			{
				return "Observed";
			}
			return "Active Path";
		}
	}

	private static string ShortMac(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "Unknown";
		}
		string text = value.Trim().ToUpperInvariant();
		if (text.Length <= 8)
		{
			return text;
		}
		return text.Substring(text.Length - 8);
	}
}

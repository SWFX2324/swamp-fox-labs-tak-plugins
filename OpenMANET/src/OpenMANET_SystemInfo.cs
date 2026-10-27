using System;
using System.Collections.Generic;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_SystemInfo
{
	public string Hostname { get; set; }

	public string Model { get; set; }

	public string Firmware { get; set; }

	public string Kernel { get; set; }

	public string Architecture { get; set; }

	public TimeSpan? Uptime { get; set; }

	public DateTimeOffset? DeviceTimeUtc { get; set; }

	public double CpuLoadPercent { get; set; }

	public long MemoryTotalBytes { get; set; }

	public long MemoryUsedBytes { get; set; }

	public long OverlayTotalBytes { get; set; }

	public long OverlayUsedBytes { get; set; }

	public double CpuTemperatureCelsius { get; set; }

	public DateTimeOffset CollectedAtUtc { get; set; }

	public List<OpenMANET_NetworkSummaryInfo> NetworkEntries { get; set; } = new List<OpenMANET_NetworkSummaryInfo>();

	public List<OpenMANET_ServiceInfo> Services { get; set; } = new List<OpenMANET_ServiceInfo>();

	public string HostnameDisplay => DisplayOrUnavailable(Hostname);

	public string ModelDisplay => DisplayOrUnavailable(Model);

	public string FirmwareDisplay => DisplayOrUnavailable(Firmware);

	public string KernelDisplay => DisplayOrUnavailable(Kernel);

	public string ArchitectureDisplay => DisplayOrUnavailable(Architecture);

	public double CpuLoadValue => ClampPercentage(CpuLoadPercent);

	public double MemoryUsagePercent => Percentage(MemoryUsedBytes, MemoryTotalBytes);

	public double OverlayUsagePercent => Percentage(OverlayUsedBytes, OverlayTotalBytes);

	public string CpuLoadDisplay => $"{CpuLoadValue:0.0}%";

	public string MemoryDisplay => FormatUsage(MemoryUsedBytes, MemoryTotalBytes, MemoryUsagePercent);

	public string OverlayDisplay => FormatUsage(OverlayUsedBytes, OverlayTotalBytes, OverlayUsagePercent);

	public string TemperatureDisplay
	{
		get
		{
			if (!(CpuTemperatureCelsius < 0.0))
			{
				return $"{CpuTemperatureCelsius:0.0} °C";
			}
			return "--";
		}
	}

	public string UptimeDisplay
	{
		get
		{
			if (!Uptime.HasValue)
			{
				return "--";
			}
			TimeSpan value = Uptime.Value;
			if (value.TotalDays >= 1.0)
			{
				return $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m";
			}
			if (value.TotalHours >= 1.0)
			{
				return $"{(int)value.TotalHours}h {value.Minutes}m";
			}
			return $"{value.Minutes}m {value.Seconds}s";
		}
	}

	public string DeviceTimeDisplay
	{
		get
		{
			if (!DeviceTimeUtc.HasValue)
			{
				return "--";
			}
			return DeviceTimeUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
		}
	}

	public string CollectedAtDisplay => CollectedAtUtc.ToLocalTime().ToString("HH:mm:ss");

	public int RunningServiceCount => Services.FindAll((OpenMANET_ServiceInfo service) => service.IsRunning).Count;

	public int StoppedServiceCount => Services.FindAll((OpenMANET_ServiceInfo service) => service.IsStopped).Count;

	public string ServiceSummaryDisplay
	{
		get
		{
			if (Services.Count != 0)
			{
				return $"{RunningServiceCount}/{Services.Count} running";
			}
			return "No service data";
		}
	}

	public string HealthDisplay
	{
		get
		{
			if (!(CpuLoadValue >= 90.0) && !(MemoryUsagePercent >= 90.0) && !(OverlayUsagePercent >= 90.0) && !(CpuTemperatureCelsius >= 80.0))
			{
				return "Healthy";
			}
			return "Degraded";
		}
	}

	private static string DisplayOrUnavailable(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return "--";
	}

	private static double Percentage(long used, long total)
	{
		if (used <= 0 || total <= 0)
		{
			return 0.0;
		}
		return ClampPercentage(100.0 * (double)used / (double)total);
	}

	private static double ClampPercentage(double value)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			return 0.0;
		}
		return Math.Max(0.0, Math.Min(100.0, value));
	}

	private static string FormatBytes(long value)
	{
		if (value < 0)
		{
			return "--";
		}
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
		return $"{value} B";
	}

	private static string FormatUsage(long used, long total, double percentage)
	{
		if (total <= 0)
		{
			return "--";
		}
		return FormatBytes(Math.Max(0L, used)) + " / " + FormatBytes(total) + $" ({percentage:0}%)";
	}
}

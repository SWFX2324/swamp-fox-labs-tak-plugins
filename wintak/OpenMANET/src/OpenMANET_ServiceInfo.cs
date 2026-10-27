using System;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_ServiceInfo
{
	public string Name { get; set; }

	public string Status { get; set; }

	public int ProcessId { get; set; }

	public bool IsRunning => string.Equals(Status, "SERVICE_STATUS_RUNNING", StringComparison.OrdinalIgnoreCase);

	public bool IsStopped => string.Equals(Status, "SERVICE_STATUS_STOPPED", StringComparison.OrdinalIgnoreCase);

	public string NameDisplay
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Name))
			{
				return Name;
			}
			return "Unknown service";
		}
	}

	public string StatusDisplay
	{
		get
		{
			if (!IsRunning)
			{
				if (!IsStopped)
				{
					return "Unknown";
				}
				return "Stopped";
			}
			return "Running";
		}
	}

	public string ProcessDisplay
	{
		get
		{
			if (ProcessId <= 0)
			{
				return "No PID";
			}
			return "PID " + ProcessId;
		}
	}
}

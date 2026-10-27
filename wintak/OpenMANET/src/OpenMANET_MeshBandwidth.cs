using System;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_MeshBandwidth
{
	public DateTimeOffset CapturedAtUtc { get; set; }

	public double MegabitsPerSecond { get; set; }

	public int DirectLinkCount { get; set; }

	public bool HasEstimate { get; set; }
}

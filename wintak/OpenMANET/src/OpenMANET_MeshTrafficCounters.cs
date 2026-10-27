using System;
using System.Collections.Generic;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_MeshTrafficCounters
{
	public DateTimeOffset CapturedAtUtc { get; set; }

	public long RxBytes { get; set; }

	public long TxBytes { get; set; }

	public bool HasMeshInterface { get; set; }

	public List<string> InterfaceNames { get; } = new List<string>();
}

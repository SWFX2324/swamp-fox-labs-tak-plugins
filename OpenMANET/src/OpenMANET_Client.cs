using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_Client : IDisposable
{
	[DataContract]
	private sealed class LoginRequest
	{
		[DataMember(Name = "username")]
		public string Username { get; set; }

		[DataMember(Name = "password")]
		public string Password { get; set; }
	}

	[DataContract]
	private sealed class LoginResponse
	{
		[DataMember(Name = "username")]
		public string Username { get; set; }

		[DataMember(Name = "token")]
		public string Token { get; set; }
	}

	[DataContract]
	private sealed class ListNodesResponse
	{
		[DataMember(Name = "nodes")]
		public List<NodeResponse> Nodes { get; set; }
	}

	[DataContract]
	private sealed class NodeResponse
	{
		[DataMember(Name = "hostname")]
		public string Hostname { get; set; }

		[DataMember(Name = "ipaddr")]
		public string IpAddress { get; set; }

		[DataMember(Name = "mac")]
		public string MacAddress { get; set; }
	}

	[DataContract]
	private sealed class ListMeshNeighborsResponse
	{
		[DataMember(Name = "neighbors")]
		public List<MeshNeighborResponse> Neighbors { get; set; }
	}

	[DataContract]
	private sealed class MeshNeighborResponse
	{
		[DataMember(Name = "neighbor")]
		public string Neighbor { get; set; }

		[DataMember(Name = "signalStrength")]
		public int SignalStrength { get; set; }

		[DataMember(Name = "hardwareAddress")]
		public string HardwareAddress { get; set; }

		[DataMember(Name = "signal")]
		public int Signal { get; set; }

		[DataMember(Name = "lastSeen")]
		public object LastSeen { get; set; }

		[DataMember(Name = "throughput")]
		public int Throughput { get; set; }
	}

	[DataContract]
	private sealed class ListNetworkInterfacesResponse
	{
		[DataMember(Name = "interfaces")]
		public List<NetworkInterfaceResponse> Interfaces { get; set; }
	}

	[DataContract]
	private sealed class NetworkInterfaceResponse
	{
		[DataMember(Name = "name")]
		public string Name { get; set; }

		[DataMember(Name = "type")]
		public string Type { get; set; }

		[DataMember(Name = "rxBytes")]
		public object RxBytes { get; set; }

		[DataMember(Name = "txBytes")]
		public object TxBytes { get; set; }
	}

	[DataContract]
	private sealed class DashboardStatusResponse
	{
		[DataMember(Name = "deviceInfo")]
		public DashboardDeviceInfo DeviceInfo { get; set; }

		[DataMember(Name = "systemResources")]
		public DashboardSystemResources SystemResources { get; set; }

		[DataMember(Name = "networkSummary")]
		public DashboardNetworkSummary NetworkSummary { get; set; }

		[DataMember(Name = "activeServices")]
		public List<DashboardServiceInfo> ActiveServices { get; set; }
	}

	[DataContract]
	private sealed class DashboardDeviceInfo
	{
		[DataMember(Name = "hostname")]
		public string Hostname { get; set; }

		[DataMember(Name = "model")]
		public string Model { get; set; }

		[DataMember(Name = "firmware")]
		public string Firmware { get; set; }

		[DataMember(Name = "kernel")]
		public string Kernel { get; set; }

		[DataMember(Name = "architecture")]
		public string Architecture { get; set; }
	}

	[DataContract]
	private sealed class DashboardSystemResources
	{
		[DataMember(Name = "uptime")]
		public string Uptime { get; set; }

		[DataMember(Name = "localTime")]
		public string LocalTime { get; set; }

		[DataMember(Name = "cpuLoadPercent")]
		public double? CpuLoadPercent { get; set; }

		[DataMember(Name = "memoryTotalBytes")]
		public object MemoryTotalBytes { get; set; }

		[DataMember(Name = "memoryUsedBytes")]
		public object MemoryUsedBytes { get; set; }

		[DataMember(Name = "overlayTotalBytes")]
		public object OverlayTotalBytes { get; set; }

		[DataMember(Name = "overlayUsedBytes")]
		public object OverlayUsedBytes { get; set; }

		[DataMember(Name = "cpuTempCelsius")]
		public double? CpuTemperatureCelsius { get; set; }
	}

	[DataContract]
	private sealed class DashboardNetworkSummary
	{
		[DataMember(Name = "entries")]
		public List<DashboardNetworkEntry> Entries { get; set; }
	}

	[DataContract]
	private sealed class DashboardNetworkEntry
	{
		[DataMember(Name = "interfaceName")]
		public string InterfaceName { get; set; }

		[DataMember(Name = "displayName")]
		public string DisplayName { get; set; }

		[DataMember(Name = "state")]
		public string State { get; set; }

		[DataMember(Name = "detail")]
		public string Detail { get; set; }

		[DataMember(Name = "rxBytes")]
		public object RxBytes { get; set; }

		[DataMember(Name = "txBytes")]
		public object TxBytes { get; set; }
	}

	[DataContract]
	private sealed class DashboardServiceInfo
	{
		[DataMember(Name = "name")]
		public string Name { get; set; }

		[DataMember(Name = "status")]
		public string Status { get; set; }

		[DataMember(Name = "pid")]
		public int ProcessId { get; set; }
	}

	[DataContract]
	private sealed class MeshTopologyResponse
	{
		[DataMember(Name = "topology")]
		public MeshTopology Topology { get; set; }
	}

	[DataContract]
	private sealed class MeshTopology
	{
		[DataMember(Name = "selfMac")]
		public string SelfMac { get; set; }

		[DataMember(Name = "selfHostname")]
		public string SelfHostname { get; set; }

		[DataMember(Name = "algorithm")]
		public string Algorithm { get; set; }

		[DataMember(Name = "collectedAt")]
		public string CollectedAt { get; set; }

		[DataMember(Name = "nodes")]
		public List<TopologyNodeResponse> Nodes { get; set; }

		[DataMember(Name = "edges")]
		public List<TopologyEdgeResponse> Edges { get; set; }

		[DataMember(Name = "gossipCoverage")]
		public GossipCoverageResponse GossipCoverage { get; set; }
	}

	[DataContract]
	private sealed class TopologyNodeResponse
	{
		[DataMember(Name = "mac")]
		public string MacAddress { get; set; }

		[DataMember(Name = "secondaryMacs")]
		public List<string> SecondaryMacs { get; set; }

		[DataMember(Name = "hostname")]
		public string Hostname { get; set; }

		[DataMember(Name = "segment")]
		public string Segment { get; set; }

		[DataMember(Name = "hopsFromSelf")]
		public int? HopsFromSelf { get; set; }

		[DataMember(Name = "myHardIfname")]
		public string MyHardInterfaceName { get; set; }

		[DataMember(Name = "isSelf")]
		public bool IsSelf { get; set; }

		[DataMember(Name = "remoteGatewayMac")]
		public string RemoteGatewayMac { get; set; }

		[DataMember(Name = "gossipStale")]
		public bool GossipStale { get; set; }

		[DataMember(Name = "gossipAgeSeconds")]
		public int? GossipAgeSeconds { get; set; }

		[DataMember(Name = "isGateway")]
		public bool IsGateway { get; set; }
	}

	[DataContract]
	private sealed class TopologyEdgeResponse
	{
		[DataMember(Name = "fromMac")]
		public string FromMac { get; set; }

		[DataMember(Name = "toMac")]
		public string ToMac { get; set; }

		[DataMember(Name = "metric")]
		public double Metric { get; set; }

		[DataMember(Name = "blos")]
		public bool IsBlos { get; set; }

		[DataMember(Name = "onMyPath")]
		public bool IsOnMyPath { get; set; }
	}

	[DataContract]
	private sealed class GossipCoverageResponse
	{
		[DataMember(Name = "published")]
		public int Published { get; set; }

		[DataMember(Name = "total")]
		public int Total { get; set; }
	}

	private static readonly TimeSpan StaleRetention = TimeSpan.FromMinutes(5.0);

	private const string LoginPath = "auth/login";

	private const string LogoutPath = "auth/logout";

	private const string ListNodesPath = "openmanet.service.v1.NodeService/ListNodes";

	private const string ListMeshNeighborsPath = "openmanet.service.v1.MeshNeighborService/ListMeshNeighbors";

	private const string GetMeshTopologyPath = "openmanet.mesh_topology.v1.MeshTopologyService/GetMeshTopology";

	private const string GetDashboardStatusPath = "openmanet.dashboard.v1.DashboardService/GetDashboardStatus";

	private const string ListNetworkInterfacesPath = "openmanet.network_interface.v1.NetworkInterfaceService/ListNetworkInterfaces";

	private readonly Dictionary<string, DateTimeOffset> _lastSeenByNode = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

	private HttpClient _httpClient;

	private CancellationTokenSource _sessionCancellation;

	private string _token;

	private bool _usesSecureFrontend;

	// Last good response from each refresh endpoint, reused when that endpoint fails so one
	// bad call doesn't blank the rest of the picture or mark every node stale.
	private ListNodesResponse _lastKnownNodes;

	private MeshTopology _lastTopologyResponse;

	private List<MeshNeighborResponse> _lastNeighbors;

	public bool IsConnected { get; private set; }

	public OpenMANET_TopologyInfo LastTopology { get; private set; }

	public OpenMANET_SystemInfo LastSystemDetails { get; private set; }

	/// <summary>Endpoints that failed on the latest refresh, mapped to an operator-safe message.</summary>
	public IReadOnlyDictionary<string, string> LastRefreshFailures { get; private set; } = new Dictionary<string, string>();

	public async Task LoginAsync(string host, int port, string username, string password, bool allowInsecureHttp, CancellationToken cancellationToken)
	{
		Uri uri = OpenMANET_Security.BuildEndpoint(host, port, allowInsecureHttp);
		if (allowInsecureHttp)
		{
			uri = await OpenMANET_Security.ValidateAndPinLegacyEndpointAsync(uri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		string username2 = OpenMANET_Security.ValidateUsername(username);
		OpenMANET_Security.ValidatePassword(password);
		Disconnect();
		_usesSecureFrontend = !allowInsecureHttp;
		HttpClientHandler handler = new HttpClientHandler
		{
			AllowAutoRedirect = false,
			UseCookies = false,
			UseProxy = false,
			// Refresh issues four RPCs at once while the 5 s link sampler may also be in flight.
			MaxConnectionsPerServer = 4
		};
		_sessionCancellation = new CancellationTokenSource();
		_httpClient = new HttpClient(handler)
		{
			BaseAddress = uri,
			Timeout = Timeout.InfiniteTimeSpan
		};
		LoginRequest loginRequest = new LoginRequest
		{
			Username = username2,
			Password = password
		};
		string content = SerializeJson(loginRequest);
		loginRequest.Password = null;
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
		request.Content = new StringContent(content, Encoding.UTF8, "application/json");
		using CancellationTokenSource requestCancellation = CreateRequestCancellation(cancellationToken);
		using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCancellation.Token).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			throw CreateHttpException("Authentication failed", response);
		}
		LoginResponse loginResponse = DeserializeJson<LoginResponse>(await OpenMANET_Security.ReadBoundedContentAsync(response.Content, requestCancellation.Token).ConfigureAwait(continueOnCapturedContext: false));
		if (loginResponse == null || string.IsNullOrWhiteSpace(loginResponse.Token))
		{
			throw new SerializationException("OpenMANET did not return a bearer token.");
		}
		_token = OpenMANET_Security.NormalizeIdentifier(loginResponse.Token, "bearer token", 8192);
		IsConnected = true;
	}

	public async Task<List<OpenMANET_NodeInfo>> ListNodesAsync(CancellationToken cancellationToken)
	{
		EnsureConnected();
		CancellationToken token = ((_sessionCancellation != null) ? _sessionCancellation.Token : CancellationToken.None);
		using CancellationTokenSource refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, token);
		refreshCancellation.CancelAfter(OpenMANET_Security.RefreshTimeout);
		Task<ListNodesResponse> nodesTask = PostAsync<ListNodesResponse>("openmanet.service.v1.NodeService/ListNodes", "Unable to retrieve OpenMANET nodes", refreshCancellation.Token);
		Task<MeshTopologyResponse> topologyTask = PostAsync<MeshTopologyResponse>("openmanet.mesh_topology.v1.MeshTopologyService/GetMeshTopology", "Unable to retrieve OpenMANET topology", refreshCancellation.Token);
		Task<ListMeshNeighborsResponse> neighborsTask = PostAsync<ListMeshNeighborsResponse>("openmanet.service.v1.MeshNeighborService/ListMeshNeighbors", "Unable to retrieve OpenMANET link quality", refreshCancellation.Token);
		Task<DashboardStatusResponse> dashboardTask = PostAsync<DashboardStatusResponse>("openmanet.dashboard.v1.DashboardService/GetDashboardStatus", "Unable to retrieve OpenMANET system details", refreshCancellation.Token);
		try
		{
			await Task.WhenAll(nodesTask, topologyTask, neighborsTask, dashboardTask).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
			// Each task is inspected individually below.
		}
		// Check every endpoint before applying any result so an expired session or a
		// disconnect is reported as such rather than as a partial refresh.
		Exception nodesFailure = GetEndpointFailure(nodesTask, cancellationToken, token);
		Exception topologyFailure = GetEndpointFailure(topologyTask, cancellationToken, token);
		Exception neighborsFailure = GetEndpointFailure(neighborsTask, cancellationToken, token);
		Exception dashboardFailure = GetEndpointFailure(dashboardTask, cancellationToken, token);
		Dictionary<string, string> failures = new Dictionary<string, string>(StringComparer.Ordinal);
		if (nodesFailure == null)
		{
			_lastKnownNodes = nodesTask.Result;
		}
		else if (_lastKnownNodes == null)
		{
			ExceptionDispatchInfo.Capture(nodesFailure).Throw();
		}
		else
		{
			failures["Node list"] = OpenMANET_Security.OperatorMessage(nodesFailure);
		}
		if (topologyFailure == null)
		{
			_lastTopologyResponse = topologyTask.Result?.Topology;
		}
		else
		{
			failures["Topology"] = OpenMANET_Security.OperatorMessage(topologyFailure);
		}
		if (neighborsFailure == null)
		{
			_lastNeighbors = neighborsTask.Result?.Neighbors;
		}
		else
		{
			failures["Link quality"] = OpenMANET_Security.OperatorMessage(neighborsFailure);
		}
		if (dashboardFailure == null)
		{
			LastSystemDetails = CreateSystemInfo(dashboardTask.Result);
		}
		else
		{
			failures["System status"] = OpenMANET_Security.OperatorMessage(dashboardFailure);
		}
		LastRefreshFailures = failures;
		ListNodesResponse knownNodes = _lastKnownNodes;
		MeshTopology topology = _lastTopologyResponse;
		List<MeshNeighborResponse> list = _lastNeighbors ?? new List<MeshNeighborResponse>();
		LastTopology = CreateTopologyInfo(topology);
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(topology?.SelfHostname))
		{
			hashSet.Add(topology.SelfHostname.Trim());
		}
		if (topology?.Nodes != null)
		{
			foreach (TopologyNodeResponse node in topology.Nodes)
			{
				if (node != null && (node.IsSelf || (node.HopsFromSelf.HasValue && node.HopsFromSelf.Value < 99)) && !string.IsNullOrWhiteSpace(node.Hostname))
				{
					hashSet.Add(node.Hostname.Trim());
				}
			}
		}
		Dictionary<string, TopologyNodeResponse> dictionary = new Dictionary<string, TopologyNodeResponse>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, TopologyNodeResponse> dictionary2 = new Dictionary<string, TopologyNodeResponse>(StringComparer.OrdinalIgnoreCase);
		if (topology?.Nodes != null)
		{
			foreach (TopologyNodeResponse node2 in topology.Nodes)
			{
				if (node2 != null)
				{
					if (!string.IsNullOrWhiteSpace(node2.Hostname))
					{
						dictionary[node2.Hostname.Trim()] = node2;
					}
					if (!string.IsNullOrWhiteSpace(node2.MacAddress))
					{
						dictionary2[node2.MacAddress.Trim()] = node2;
					}
				}
			}
		}
		List<OpenMANET_NodeInfo> list2 = new List<OpenMANET_NodeInfo>();
		HashSet<string> hashSet2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (knownNodes?.Nodes == null)
		{
			_lastSeenByNode.Clear();
			return list2;
		}
		foreach (NodeResponse node3 in knownNodes.Nodes)
		{
			if (node3 == null)
			{
				continue;
			}
			string nodeKey = GetNodeKey(node3);
			hashSet2.Add(nodeKey);
			string text = node3.Hostname?.Trim();
			bool flag = !string.IsNullOrWhiteSpace(text) && hashSet.Contains(text);
			if (flag)
			{
				_lastSeenByNode[nodeKey] = utcNow;
			}
			else if (!_lastSeenByNode.ContainsKey(nodeKey))
			{
				_lastSeenByNode[nodeKey] = utcNow;
			}
			TimeSpan timeSpan = utcNow - _lastSeenByNode[nodeKey];
			if (flag || !(timeSpan >= StaleRetention))
			{
				TopologyNodeResponse value = null;
				if (!string.IsNullOrWhiteSpace(node3.MacAddress))
				{
					dictionary2.TryGetValue(node3.MacAddress.Trim(), out value);
				}
				if (value == null && !string.IsNullOrWhiteSpace(text))
				{
					dictionary.TryGetValue(text, out value);
				}
				List<MeshNeighborResponse> list3 = FindMatchingNeighbors(node3, list);
				MeshNeighborResponse meshNeighborResponse = SelectBestNeighbor(list3);
				list2.Add(new OpenMANET_NodeInfo
				{
					Name = (string.IsNullOrWhiteSpace(node3.Hostname) ? "Unknown node" : node3.Hostname),
					Address = (string.IsNullOrWhiteSpace(node3.IpAddress) ? "No IP address" : node3.IpAddress),
					MacAddress = (string.IsNullOrWhiteSpace(node3.MacAddress) ? "--" : node3.MacAddress),
					IsOnline = flag,
					IsSelf = (value?.IsSelf ?? false),
					IsGateway = (value?.IsGateway ?? false),
					Segment = value?.Segment,
					HopsFromSelf = value?.HopsFromSelf,
					RouteInterface = value?.MyHardInterfaceName,
					RemoteGatewayMac = value?.RemoteGatewayMac,
					GossipStale = (value?.GossipStale ?? false),
					GossipAgeSeconds = value?.GossipAgeSeconds,
					LastSuccessfulPollUtc = _lastSeenByNode[nodeKey],
					Rssi = GetAvailableSignal(meshNeighborResponse?.SignalStrength),
					CurrentRssi = GetAvailableSignal(meshNeighborResponse?.Signal),
					ThroughputKbps = GetAvailableThroughput(meshNeighborResponse?.Throughput),
					LinkAgeMilliseconds = GetLastSeenMilliseconds(meshNeighborResponse?.LastSeen),
					NeighborIdentifier = meshNeighborResponse?.Neighbor,
					LinkHardwareAddress = meshNeighborResponse?.HardwareAddress,
					DirectLinkCount = ((value != null && value.IsSelf) ? list.Count : list3.Count),
					HasDirectLinkMetrics = (meshNeighborResponse != null)
				});
			}
		}
		RemoveMissingTrackedNodes(hashSet2);
		return list2;
	}

	public async Task<OpenMANET_MeshTrafficCounters> GetMeshTrafficCountersAsync(CancellationToken cancellationToken)
	{
		EnsureConnected();
		ListNetworkInterfacesResponse listNetworkInterfacesResponse = await PostAsync<ListNetworkInterfacesResponse>("openmanet.network_interface.v1.NetworkInterfaceService/ListNetworkInterfaces", "Unable to retrieve OpenMANET traffic counters", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		OpenMANET_MeshTrafficCounters openMANET_MeshTrafficCounters = new OpenMANET_MeshTrafficCounters
		{
			CapturedAtUtc = DateTimeOffset.UtcNow
		};
		List<NetworkInterfaceResponse> interfaces = listNetworkInterfacesResponse?.Interfaces ?? new List<NetworkInterfaceResponse>();
		AddTrafficCountersByType(interfaces, "INTERFACE_TYPE_HALOW_MESH", openMANET_MeshTrafficCounters);
		if (!openMANET_MeshTrafficCounters.HasMeshInterface)
		{
			AddTrafficCountersByType(interfaces, "INTERFACE_TYPE_BATMAN", openMANET_MeshTrafficCounters);
		}
		return openMANET_MeshTrafficCounters;
	}

	public async Task<OpenMANET_MeshBandwidth> GetMeshBandwidthAsync(CancellationToken cancellationToken)
	{
		EnsureConnected();
		ListMeshNeighborsResponse listMeshNeighborsResponse = await PostAsync<ListMeshNeighborsResponse>("openmanet.service.v1.MeshNeighborService/ListMeshNeighbors", "Unable to retrieve OpenMANET bandwidth", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		OpenMANET_MeshBandwidth openMANET_MeshBandwidth = new OpenMANET_MeshBandwidth
		{
			CapturedAtUtc = DateTimeOffset.UtcNow
		};
		List<MeshNeighborResponse> obj = listMeshNeighborsResponse?.Neighbors ?? new List<MeshNeighborResponse>();
		int num = int.MaxValue;
		foreach (MeshNeighborResponse item in obj)
		{
			if (item != null && item.Throughput > 0)
			{
				num = Math.Min(num, item.Throughput);
				openMANET_MeshBandwidth.DirectLinkCount++;
			}
		}
		if (openMANET_MeshBandwidth.DirectLinkCount > 0)
		{
			openMANET_MeshBandwidth.HasEstimate = true;
			openMANET_MeshBandwidth.MegabitsPerSecond = (double)num / 1000.0;
		}
		return openMANET_MeshBandwidth;
	}

	public void Disconnect()
	{
		if (_sessionCancellation != null)
		{
			_sessionCancellation.Cancel();
			_sessionCancellation.Dispose();
			_sessionCancellation = null;
		}
		_token = null;
		_usesSecureFrontend = false;
		IsConnected = false;
		LastTopology = null;
		LastSystemDetails = null;
		LastRefreshFailures = new Dictionary<string, string>();
		_lastKnownNodes = null;
		_lastTopologyResponse = null;
		_lastNeighbors = null;
		_lastSeenByNode.Clear();
		if (_httpClient != null)
		{
			_httpClient.Dispose();
			_httpClient = null;
		}
	}

	public async Task<bool> LogoutAsync(CancellationToken cancellationToken)
	{
		HttpClient httpClient = _httpClient;
		string token = _token;
		if (!IsConnected || httpClient == null || string.IsNullOrWhiteSpace(token))
		{
			Disconnect();
			return true;
		}
		bool serverConfirmedRevocation = false;
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "auth/logout");
			using CancellationTokenSource logoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			logoutCancellation.CancelAfter(OpenMANET_Security.LogoutTimeout);
			using HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, logoutCancellation.Token).ConfigureAwait(continueOnCapturedContext: false);
			serverConfirmedRevocation = httpResponseMessage.IsSuccessStatusCode;
		}
		catch (OperationCanceledException)
		{
		}
		catch (HttpRequestException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
		finally
		{
			Disconnect();
		}
		return serverConfirmedRevocation;
	}

	public void Dispose()
	{
		Disconnect();
	}

	private async Task<T> PostAsync<T>(string path, string errorMessage, CancellationToken cancellationToken)
	{
		EnsureConnected();
		string requestUri = (_usesSecureFrontend ? ("rpc/" + path) : path);
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUri);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
		request.Headers.Add("Connect-Protocol-Version", "1");
		request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
		using CancellationTokenSource requestCancellation = CreateRequestCancellation(cancellationToken);
		using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCancellation.Token).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			throw CreateHttpException(errorMessage, response);
		}
		T val = DeserializeJson<T>(await OpenMANET_Security.ReadBoundedContentAsync(response.Content, requestCancellation.Token).ConfigureAwait(continueOnCapturedContext: false));
		ValidateAndNormalizeResponse(val);
		return val;
	}

	private CancellationTokenSource CreateRequestCancellation(CancellationToken cancellationToken)
	{
		CancellationToken token = ((_sessionCancellation != null) ? _sessionCancellation.Token : CancellationToken.None);
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, token);
		cancellationTokenSource.CancelAfter(OpenMANET_Security.RequestTimeout);
		return cancellationTokenSource;
	}

	/// <summary>
	/// Returns why a refresh endpoint failed, or null if it succeeded. Rethrows failures that
	/// end the whole refresh: an expired session, or a disconnect/session cancellation.
	/// Per-request timeouts are reported as an ordinary endpoint failure.
	/// </summary>
	private static Exception GetEndpointFailure(Task task, CancellationToken cancellationToken, CancellationToken sessionToken)
	{
		if (task.Status == TaskStatus.RanToCompletion)
		{
			return null;
		}
		Exception exception = task.Exception?.GetBaseException() ?? new OperationCanceledException("The OpenMANET request was cancelled.");
		if (exception is OpenMANET_AuthenticationException || cancellationToken.IsCancellationRequested || sessionToken.IsCancellationRequested)
		{
			ExceptionDispatchInfo.Capture(exception).Throw();
		}
		return exception;
	}

	private void EnsureConnected()
	{
		if (!IsConnected || string.IsNullOrWhiteSpace(_token) || _httpClient == null)
		{
			throw new InvalidOperationException("Connect to OpenMANET before requesting data.");
		}
	}

	private void RemoveMissingTrackedNodes(HashSet<string> currentNodeKeys)
	{
		List<string> list = new List<string>();
		foreach (string key in _lastSeenByNode.Keys)
		{
			if (!currentNodeKeys.Contains(key))
			{
				list.Add(key);
			}
		}
		foreach (string item in list)
		{
			_lastSeenByNode.Remove(item);
		}
	}

	private static string GetNodeKey(NodeResponse node)
	{
		if (!string.IsNullOrWhiteSpace(node.MacAddress))
		{
			return "mac:" + node.MacAddress.Trim().ToLowerInvariant();
		}
		if (!string.IsNullOrWhiteSpace(node.Hostname))
		{
			return "host:" + node.Hostname.Trim().ToLowerInvariant();
		}
		if (!string.IsNullOrWhiteSpace(node.IpAddress))
		{
			return "ip:" + node.IpAddress.Trim().ToLowerInvariant();
		}
		return "unknown";
	}

	private static List<MeshNeighborResponse> FindMatchingNeighbors(NodeResponse node, List<MeshNeighborResponse> neighbors)
	{
		List<MeshNeighborResponse> list = new List<MeshNeighborResponse>();
		if (node == null || neighbors == null)
		{
			return list;
		}
		foreach (MeshNeighborResponse neighbor in neighbors)
		{
			if (neighbor != null)
			{
				bool num = !string.IsNullOrWhiteSpace(node.MacAddress) && !string.IsNullOrWhiteSpace(neighbor.HardwareAddress) && string.Equals(node.MacAddress.Trim(), neighbor.HardwareAddress.Trim(), StringComparison.OrdinalIgnoreCase);
				bool flag = NeighborIdentifierMatchesHostname(neighbor.Neighbor, node.Hostname);
				if (num | flag)
				{
					list.Add(neighbor);
				}
			}
		}
		return list;
	}

	private static bool NeighborIdentifierMatchesHostname(string neighborIdentifier, string hostname)
	{
		if (string.IsNullOrWhiteSpace(neighborIdentifier) || string.IsNullOrWhiteSpace(hostname))
		{
			return false;
		}
		string text = neighborIdentifier.Trim();
		string text2 = hostname.Trim();
		if (!string.Equals(text, text2, StringComparison.OrdinalIgnoreCase) && !text.StartsWith(text2 + ".", StringComparison.OrdinalIgnoreCase))
		{
			return text.StartsWith(text2 + "_", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static MeshNeighborResponse SelectBestNeighbor(List<MeshNeighborResponse> neighbors)
	{
		MeshNeighborResponse meshNeighborResponse = null;
		int num = int.MinValue;
		foreach (MeshNeighborResponse neighbor in neighbors)
		{
			int num2 = ((neighbor.SignalStrength != 0) ? neighbor.SignalStrength : neighbor.Signal);
			if (meshNeighborResponse == null || num2 > num)
			{
				meshNeighborResponse = neighbor;
				num = num2;
			}
		}
		return meshNeighborResponse;
	}

	private static int? GetAvailableSignal(int? signal)
	{
		if (!signal.HasValue || signal.Value == 0)
		{
			return null;
		}
		return signal;
	}

	private static int? GetAvailableThroughput(int? throughput)
	{
		if (!throughput.HasValue || throughput.Value <= 0)
		{
			return null;
		}
		return throughput;
	}

	private static void AddTrafficCountersByType(List<NetworkInterfaceResponse> interfaces, string interfaceType, OpenMANET_MeshTrafficCounters result)
	{
		foreach (NetworkInterfaceResponse @interface in interfaces)
		{
			if (@interface != null && string.Equals(@interface.Type, interfaceType, StringComparison.OrdinalIgnoreCase))
			{
				result.HasMeshInterface = true;
				result.RxBytes += Math.Max(0L, ParseInt64(@interface.RxBytes));
				result.TxBytes += Math.Max(0L, ParseInt64(@interface.TxBytes));
				if (!string.IsNullOrWhiteSpace(@interface.Name))
				{
					result.InterfaceNames.Add(@interface.Name.Trim());
				}
			}
		}
	}

	private static long? GetLastSeenMilliseconds(object value)
	{
		if (value == null)
		{
			return null;
		}
		if (long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result >= 0)
		{
			return result;
		}
		return null;
	}

	private static OpenMANET_TopologyInfo CreateTopologyInfo(MeshTopology topology)
	{
		if (topology == null)
		{
			return null;
		}
		OpenMANET_TopologyInfo openMANET_TopologyInfo = new OpenMANET_TopologyInfo
		{
			SelfMac = topology.SelfMac,
			SelfHostname = topology.SelfHostname,
			Algorithm = topology.Algorithm,
			CollectedAtUtc = ParseTimestamp(topology.CollectedAt),
			GossipPublished = (topology.GossipCoverage?.Published ?? 0),
			GossipTotal = (topology.GossipCoverage?.Total ?? 0)
		};
		Dictionary<string, OpenMANET_TopologyNodeInfo> dictionary = new Dictionary<string, OpenMANET_TopologyNodeInfo>(StringComparer.OrdinalIgnoreCase);
		if (topology.Nodes != null)
		{
			foreach (TopologyNodeResponse node in topology.Nodes)
			{
				if (node == null)
				{
					continue;
				}
				OpenMANET_TopologyNodeInfo openMANET_TopologyNodeInfo = new OpenMANET_TopologyNodeInfo
				{
					MacAddress = node.MacAddress,
					SecondaryMacs = (node.SecondaryMacs ?? new List<string>()),
					Hostname = node.Hostname,
					Segment = node.Segment,
					HopsFromSelf = (node.HopsFromSelf ?? 99),
					RouteInterface = node.MyHardInterfaceName,
					IsSelf = node.IsSelf,
					IsGateway = node.IsGateway,
					GossipStale = node.GossipStale,
					GossipAgeSeconds = node.GossipAgeSeconds.GetValueOrDefault(),
					RemoteGatewayMac = node.RemoteGatewayMac
				};
				openMANET_TopologyInfo.Nodes.Add(openMANET_TopologyNodeInfo);
				AddMacLookup(dictionary, openMANET_TopologyNodeInfo.MacAddress, openMANET_TopologyNodeInfo);
				foreach (string secondaryMac in openMANET_TopologyNodeInfo.SecondaryMacs)
				{
					AddMacLookup(dictionary, secondaryMac, openMANET_TopologyNodeInfo);
				}
			}
		}
		openMANET_TopologyInfo.Nodes.Sort((OpenMANET_TopologyNodeInfo left, OpenMANET_TopologyNodeInfo right) =>
		{
			int num = left.HopsFromSelf.CompareTo(right.HopsFromSelf);
			return (num == 0) ? string.Compare(left.NameDisplay, right.NameDisplay, StringComparison.OrdinalIgnoreCase) : num;
		});
		if (topology.Edges != null)
		{
			foreach (TopologyEdgeResponse edge in topology.Edges)
			{
				if (edge != null)
				{
					dictionary.TryGetValue(edge.FromMac ?? string.Empty, out var value);
					dictionary.TryGetValue(edge.ToMac ?? string.Empty, out var value2);
					openMANET_TopologyInfo.Edges.Add(new OpenMANET_TopologyEdgeInfo
					{
						FromMac = edge.FromMac,
						ToMac = edge.ToMac,
						FromName = value?.NameDisplay,
						ToName = value2?.NameDisplay,
						Metric = edge.Metric,
						IsBlos = edge.IsBlos,
						IsOnMyPath = edge.IsOnMyPath
					});
				}
			}
		}
		openMANET_TopologyInfo.Edges.Sort((OpenMANET_TopologyEdgeInfo left, OpenMANET_TopologyEdgeInfo right) => string.Compare(left.LinkDisplay, right.LinkDisplay, StringComparison.OrdinalIgnoreCase));
		return openMANET_TopologyInfo;
	}

	private static OpenMANET_SystemInfo CreateSystemInfo(DashboardStatusResponse response)
	{
		if (response == null)
		{
			return null;
		}
		DashboardSystemResources systemResources = response.SystemResources;
		OpenMANET_SystemInfo openMANET_SystemInfo = new OpenMANET_SystemInfo
		{
			Hostname = response.DeviceInfo?.Hostname,
			Model = response.DeviceInfo?.Model,
			Firmware = response.DeviceInfo?.Firmware,
			Kernel = response.DeviceInfo?.Kernel,
			Architecture = response.DeviceInfo?.Architecture,
			Uptime = ParseDuration(systemResources?.Uptime),
			DeviceTimeUtc = ParseTimestamp(systemResources?.LocalTime),
			CpuLoadPercent = (systemResources?.CpuLoadPercent).GetValueOrDefault(),
			MemoryTotalBytes = ParseInt64(systemResources?.MemoryTotalBytes),
			MemoryUsedBytes = ParseInt64(systemResources?.MemoryUsedBytes),
			OverlayTotalBytes = ParseInt64(systemResources?.OverlayTotalBytes),
			OverlayUsedBytes = ParseInt64(systemResources?.OverlayUsedBytes),
			CpuTemperatureCelsius = (systemResources?.CpuTemperatureCelsius ?? (-1.0)),
			CollectedAtUtc = DateTimeOffset.UtcNow
		};
		if (response.NetworkSummary?.Entries != null)
		{
			foreach (DashboardNetworkEntry entry in response.NetworkSummary.Entries)
			{
				if (entry != null)
				{
					openMANET_SystemInfo.NetworkEntries.Add(new OpenMANET_NetworkSummaryInfo
					{
						InterfaceName = entry.InterfaceName,
						DisplayName = entry.DisplayName,
						State = entry.State,
						Detail = entry.Detail,
						RxBytes = ParseInt64(entry.RxBytes),
						TxBytes = ParseInt64(entry.TxBytes)
					});
				}
			}
		}
		openMANET_SystemInfo.NetworkEntries.Sort((OpenMANET_NetworkSummaryInfo left, OpenMANET_NetworkSummaryInfo right) => string.Compare(left.NameDisplay, right.NameDisplay, StringComparison.OrdinalIgnoreCase));
		if (response.ActiveServices != null)
		{
			foreach (DashboardServiceInfo activeService in response.ActiveServices)
			{
				if (activeService != null)
				{
					openMANET_SystemInfo.Services.Add(new OpenMANET_ServiceInfo
					{
						Name = activeService.Name,
						Status = activeService.Status,
						ProcessId = activeService.ProcessId
					});
				}
			}
		}
		openMANET_SystemInfo.Services.Sort((OpenMANET_ServiceInfo left, OpenMANET_ServiceInfo right) =>
		{
			int num = left.IsRunning.CompareTo(right.IsRunning);
			return (num == 0) ? string.Compare(left.NameDisplay, right.NameDisplay, StringComparison.OrdinalIgnoreCase) : num;
		});
		return openMANET_SystemInfo;
	}

	private static void AddMacLookup(Dictionary<string, OpenMANET_TopologyNodeInfo> lookup, string macAddress, OpenMANET_TopologyNodeInfo node)
	{
		if (!string.IsNullOrWhiteSpace(macAddress))
		{
			lookup[macAddress.Trim()] = node;
		}
	}

	private static DateTimeOffset? ParseTimestamp(string value)
	{
		if (!string.IsNullOrWhiteSpace(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var result))
		{
			return result;
		}
		return null;
	}

	private static TimeSpan? ParseDuration(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		string text = value.Trim();
		if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Substring(0, text.Length - 1);
		}
		if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || result < 0.0 || result > TimeSpan.MaxValue.TotalSeconds)
		{
			return null;
		}
		return TimeSpan.FromSeconds(result);
	}

	private static long ParseInt64(object value)
	{
		if (value == null)
		{
			return 0L;
		}
		if (!long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return 0L;
		}
		return result;
	}

	private static void ValidateAndNormalizeResponse<T>(T response)
	{
		if (response is ListNodesResponse listNodesResponse)
		{
			EnsureCollectionLimit(listNodesResponse.Nodes, 256, "nodes");
			if (listNodesResponse.Nodes == null)
			{
				return;
			}
			{
				foreach (NodeResponse node in listNodesResponse.Nodes)
				{
					if (node != null)
					{
						node.Hostname = NormalizeIdentifier(node.Hostname, "node hostname");
						node.IpAddress = NormalizeIdentifier(node.IpAddress, "node IP address", 64);
						node.MacAddress = NormalizeIdentifier(node.MacAddress, "node MAC address", 64);
					}
				}
				return;
			}
		}
		if (response is ListMeshNeighborsResponse listMeshNeighborsResponse)
		{
			EnsureCollectionLimit(listMeshNeighborsResponse.Neighbors, 256, "mesh neighbors");
			if (listMeshNeighborsResponse.Neighbors == null)
			{
				return;
			}
			{
				foreach (MeshNeighborResponse neighbor in listMeshNeighborsResponse.Neighbors)
				{
					if (neighbor != null)
					{
						neighbor.Neighbor = NormalizeIdentifier(neighbor.Neighbor, "neighbor identifier");
						neighbor.HardwareAddress = NormalizeIdentifier(neighbor.HardwareAddress, "neighbor MAC address", 64);
						ValidateSignal(neighbor.SignalStrength);
						ValidateSignal(neighbor.Signal);
						ValidateRange(neighbor.Throughput, 0, 1000000000, "neighbor throughput");
						ValidateCounterObject(neighbor.LastSeen, "neighbor last-seen age");
						if (ParseInt64(neighbor.LastSeen) > 2678400000u)
						{
							throw new SerializationException("OpenMANET returned an invalid neighbor last-seen age.");
						}
					}
				}
				return;
			}
		}
		if (response is ListNetworkInterfacesResponse listNetworkInterfacesResponse)
		{
			EnsureCollectionLimit(listNetworkInterfacesResponse.Interfaces, 256, "network interfaces");
			if (listNetworkInterfacesResponse.Interfaces == null)
			{
				return;
			}
			{
				foreach (NetworkInterfaceResponse @interface in listNetworkInterfacesResponse.Interfaces)
				{
					if (@interface != null)
					{
						@interface.Name = NormalizeIdentifier(@interface.Name, "interface name", 128);
						@interface.Type = NormalizeIdentifier(@interface.Type, "interface type", 128);
						ValidateCounterObject(@interface.RxBytes, "received byte counter");
						ValidateCounterObject(@interface.TxBytes, "transmitted byte counter");
					}
				}
				return;
			}
		}
		if (response is DashboardStatusResponse dashboard)
		{
			NormalizeDashboard(dashboard);
		}
		else if (response is MeshTopologyResponse meshTopologyResponse)
		{
			NormalizeTopology(meshTopologyResponse.Topology);
		}
	}

	private static void NormalizeDashboard(DashboardStatusResponse dashboard)
	{
		if (dashboard.DeviceInfo != null)
		{
			dashboard.DeviceInfo.Hostname = NormalizeIdentifier(dashboard.DeviceInfo.Hostname, "device hostname");
			dashboard.DeviceInfo.Model = NormalizeDetail(dashboard.DeviceInfo.Model, 256);
			dashboard.DeviceInfo.Firmware = NormalizeIdentifier(dashboard.DeviceInfo.Firmware, "firmware version", 128);
			dashboard.DeviceInfo.Kernel = NormalizeIdentifier(dashboard.DeviceInfo.Kernel, "kernel version", 256);
			dashboard.DeviceInfo.Architecture = NormalizeIdentifier(dashboard.DeviceInfo.Architecture, "device architecture", 128);
		}
		if (dashboard.SystemResources != null)
		{
			DashboardSystemResources systemResources = dashboard.SystemResources;
			systemResources.Uptime = NormalizeIdentifier(systemResources.Uptime, "uptime", 128);
			systemResources.LocalTime = NormalizeIdentifier(systemResources.LocalTime, "device time", 128);
			if (systemResources.CpuLoadPercent.HasValue)
			{
				ValidateFiniteRange(systemResources.CpuLoadPercent.Value, 0.0, 100.0, "CPU load");
			}
			if (systemResources.CpuTemperatureCelsius.HasValue)
			{
				ValidateFiniteRange(systemResources.CpuTemperatureCelsius.Value, -40.0, 150.0, "CPU temperature");
			}
			ValidateCounterObject(systemResources.MemoryTotalBytes, "total memory");
			ValidateCounterObject(systemResources.MemoryUsedBytes, "used memory");
			ValidateCounterObject(systemResources.OverlayTotalBytes, "total overlay storage");
			ValidateCounterObject(systemResources.OverlayUsedBytes, "used overlay storage");
			long num = ParseInt64(systemResources.MemoryTotalBytes);
			long num2 = ParseInt64(systemResources.MemoryUsedBytes);
			long num3 = ParseInt64(systemResources.OverlayTotalBytes);
			long num4 = ParseInt64(systemResources.OverlayUsedBytes);
			if ((num > 0 && num2 > num) || (num3 > 0 && num4 > num3))
			{
				throw new SerializationException("OpenMANET returned inconsistent resource counters.");
			}
		}
		List<DashboardNetworkEntry> list = dashboard.NetworkSummary?.Entries;
		EnsureCollectionLimit(list, 256, "dashboard network entries");
		if (list != null)
		{
			foreach (DashboardNetworkEntry item in list)
			{
				if (item != null)
				{
					item.InterfaceName = NormalizeIdentifier(item.InterfaceName, "dashboard interface name", 128);
					item.DisplayName = NormalizeDetail(item.DisplayName, 256);
					item.State = NormalizeIdentifier(item.State, "interface state", 128);
					item.Detail = NormalizeDetail(item.Detail, 1024);
					ValidateCounterObject(item.RxBytes, "dashboard received byte counter");
					ValidateCounterObject(item.TxBytes, "dashboard transmitted byte counter");
				}
			}
		}
		EnsureCollectionLimit(dashboard.ActiveServices, 256, "services");
		if (dashboard.ActiveServices == null)
		{
			return;
		}
		foreach (DashboardServiceInfo activeService in dashboard.ActiveServices)
		{
			if (activeService != null)
			{
				activeService.Name = NormalizeIdentifier(activeService.Name, "service name", 128);
				activeService.Status = NormalizeIdentifier(activeService.Status, "service status", 128);
				ValidateRange(activeService.ProcessId, 0, int.MaxValue, "service process ID");
			}
		}
	}

	private static void NormalizeTopology(MeshTopology topology)
	{
		if (topology == null)
		{
			return;
		}
		topology.SelfMac = NormalizeIdentifier(topology.SelfMac, "topology self MAC address", 64);
		topology.SelfHostname = NormalizeIdentifier(topology.SelfHostname, "topology self hostname");
		topology.Algorithm = NormalizeIdentifier(topology.Algorithm, "topology algorithm", 128);
		topology.CollectedAt = NormalizeIdentifier(topology.CollectedAt, "topology timestamp", 128);
		EnsureCollectionLimit(topology.Nodes, 256, "topology nodes");
		EnsureCollectionLimit(topology.Edges, 1024, "topology edges");
		if (topology.Nodes != null)
		{
			foreach (TopologyNodeResponse node in topology.Nodes)
			{
				if (node == null)
				{
					continue;
				}
				node.MacAddress = NormalizeIdentifier(node.MacAddress, "topology node MAC address", 64);
				node.Hostname = NormalizeIdentifier(node.Hostname, "topology node hostname");
				node.Segment = NormalizeIdentifier(node.Segment, "topology segment", 128);
				node.MyHardInterfaceName = NormalizeIdentifier(node.MyHardInterfaceName, "route interface", 128);
				node.RemoteGatewayMac = NormalizeIdentifier(node.RemoteGatewayMac, "remote gateway MAC address", 64);
				EnsureCollectionLimit(node.SecondaryMacs, 16, "secondary MAC addresses");
				if (node.SecondaryMacs != null)
				{
					for (int i = 0; i < node.SecondaryMacs.Count; i++)
					{
						node.SecondaryMacs[i] = NormalizeIdentifier(node.SecondaryMacs[i], "secondary MAC address", 64);
					}
				}
				if (node.HopsFromSelf.HasValue)
				{
					ValidateRange(node.HopsFromSelf.Value, 0, 99, "topology hop count");
				}
				if (node.GossipAgeSeconds.HasValue)
				{
					ValidateRange(node.GossipAgeSeconds.Value, 0, 2678400, "gossip age");
				}
			}
		}
		if (topology.Edges != null)
		{
			foreach (TopologyEdgeResponse edge in topology.Edges)
			{
				if (edge != null)
				{
					edge.FromMac = NormalizeIdentifier(edge.FromMac, "edge source MAC address", 64);
					edge.ToMac = NormalizeIdentifier(edge.ToMac, "edge destination MAC address", 64);
					ValidateFiniteRange(edge.Metric, 0.0, 1000000000.0, "edge metric");
				}
			}
		}
		if (topology.GossipCoverage != null)
		{
			ValidateRange(topology.GossipCoverage.Published, 0, 256, "published gossip count");
			ValidateRange(topology.GossipCoverage.Total, 0, 256, "total gossip count");
			if (topology.GossipCoverage.Published > topology.GossipCoverage.Total)
			{
				throw new SerializationException("OpenMANET returned invalid gossip coverage.");
			}
		}
	}

	private static string NormalizeIdentifier(string value, string fieldName, int maximumLength = 253)
	{
		return OpenMANET_Security.NormalizeIdentifier(value, fieldName, maximumLength);
	}

	private static string NormalizeDetail(string value, int maximumLength)
	{
		return OpenMANET_Security.NormalizeDetail(value, maximumLength);
	}

	private static void EnsureCollectionLimit<T>(ICollection<T> collection, int maximumCount, string fieldName)
	{
		if (collection != null && collection.Count > maximumCount)
		{
			throw new SerializationException("OpenMANET returned too many " + fieldName + ".");
		}
	}

	private static void ValidateSignal(int value)
	{
		if (value != 0 && (value < -150 || value > 0))
		{
			throw new SerializationException("OpenMANET returned an invalid signal value.");
		}
	}

	private static void ValidateCounterObject(object value, string fieldName)
	{
		if (value != null)
		{
			string text = Convert.ToString(value, CultureInfo.InvariantCulture);
			if (text == null || text.Length > 32 || !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result < 0)
			{
				throw new SerializationException("OpenMANET returned an invalid " + fieldName + ".");
			}
		}
	}

	private static void ValidateRange(int value, int minimum, int maximum, string fieldName)
	{
		if (value < minimum || value > maximum)
		{
			throw new SerializationException("OpenMANET returned an invalid " + fieldName + ".");
		}
	}

	private static void ValidateFiniteRange(double value, double minimum, double maximum, string fieldName)
	{
		if (!OpenMANET_Security.IsFinite(value) || value < minimum || value > maximum)
		{
			throw new SerializationException("OpenMANET returned an invalid " + fieldName + ".");
		}
	}

	private static HttpRequestException CreateHttpException(string message, HttpResponseMessage response)
	{
		string text = Guid.NewGuid().ToString("N").Substring(0, 12);
		string message2 = message + ". HTTP " + (int)response.StatusCode + ". Reference " + text + ".";
		if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
		{
			return new OpenMANET_AuthenticationException(message2);
		}
		return new HttpRequestException(message2);
	}

	private static string SerializeJson<T>(T value)
	{
		DataContractJsonSerializer dataContractJsonSerializer = new DataContractJsonSerializer(typeof(T));
		using MemoryStream memoryStream = new MemoryStream();
		dataContractJsonSerializer.WriteObject((Stream)memoryStream, (object)value);
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}

	private static T DeserializeJson<T>(byte[] json)
	{
		DataContractJsonSerializer dataContractJsonSerializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings
		{
			MaxItemsInObjectGraph = 10000,
			UseSimpleDictionaryFormat = true
		});
		using MemoryStream stream = new MemoryStream(json ?? new byte[0], writable: false);
		object obj = dataContractJsonSerializer.ReadObject((Stream)stream);
		if (!(obj is T))
		{
			throw new SerializationException("Unable to deserialize the OpenMANET response.");
		}
		return (T)obj;
	}
}

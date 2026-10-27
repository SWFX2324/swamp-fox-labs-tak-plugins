using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WinTAK_OpenMANET_Plugin;

public partial class OpenMANET : UserControl, IComponentConnector
{
	private sealed class MeshEventEntry
	{
		public DateTimeOffset CapturedAt { get; set; }

		public string Severity { get; set; }

		public string Message { get; set; }

		public string TimeDisplay => CapturedAt.ToString("HH:mm:ss");

		public string SeverityDisplay
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(Severity))
				{
					return Severity.ToUpperInvariant();
				}
				return "INFO";
			}
		}
	}

	private sealed class HealthFindingEntry
	{
		public string Message { get; set; }

		public Brush IndicatorBrush { get; set; }

		public bool IsCritical { get; set; }
	}

	private sealed class NodeStateSnapshot
	{
		public string Name { get; set; }

		public bool IsOnline { get; set; }

		public int? HopsFromSelf { get; set; }

		public string RouteInterface { get; set; }

		public int? Rssi { get; set; }

		public string RemoteGatewayMac { get; set; }

		public static NodeStateSnapshot FromNode(OpenMANET_NodeInfo node)
		{
			return new NodeStateSnapshot
			{
				Name = (string.IsNullOrWhiteSpace(node.Name) ? "Unknown node" : node.Name),
				IsOnline = node.IsOnline,
				HopsFromSelf = node.HopsFromSelf,
				RouteInterface = node.RouteInterfaceDisplay,
				Rssi = (node.CurrentRssi ?? node.Rssi),
				RemoteGatewayMac = node.RemoteGatewayMac
			};
		}
	}

	private sealed class SystemStateSnapshot
	{
		public bool HighCpu { get; set; }

		public bool HighMemory { get; set; }

		public bool HighStorage { get; set; }

		public bool HighTemperature { get; set; }

		public static SystemStateSnapshot FromSystem(OpenMANET_SystemInfo system)
		{
			return new SystemStateSnapshot
			{
				HighCpu = (system.CpuLoadValue >= 90.0),
				HighMemory = (system.MemoryUsagePercent >= 90.0),
				HighStorage = (system.OverlayUsagePercent >= 90.0),
				HighTemperature = (system.CpuTemperatureCelsius >= 80.0)
			};
		}
	}

	private sealed class NodeTrendSample
	{
		public DateTimeOffset CapturedAtUtc { get; set; }

		public double? Rssi { get; set; }

		public double? BandwidthMbps { get; set; }

		public double? Hops { get; set; }

		public double? AgeSeconds { get; set; }
	}

	private sealed class NodeTrendValue
	{
		public DateTimeOffset CapturedAtUtc { get; set; }

		public double Value { get; set; }
	}

	private sealed class ThroughputSample
	{
		public DateTimeOffset CapturedAtUtc { get; set; }

		public double RxMegabitsPerSecond { get; set; }

		public double TxMegabitsPerSecond { get; set; }
	}

	private sealed class BandwidthSample
	{
		public DateTimeOffset CapturedAtUtc { get; set; }

		public double MegabitsPerSecond { get; set; }
	}

	private const string DefaultHost = "10.41.0.1";

	private const string SecureFrontendPort = "8081";

	private const string LegacyDirectPort = "8087";

	private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromMinutes(2.0);

	private static readonly TimeSpan ThroughputSampleInterval = TimeSpan.FromSeconds(5.0);

	private static readonly TimeSpan ThroughputWindow = TimeSpan.FromSeconds(60.0);

	private static readonly TimeSpan BandwidthWindow = TimeSpan.FromSeconds(60.0);

	private static readonly TimeSpan NodeTrendWindow = TimeSpan.FromMinutes(60.0);

	private const int MaximumEventCount = 100;

	private const double LowBandwidthThresholdMbps = 5.0;

	private const double WarningUtilizationPercent = 60.0;

	private const double CriticalUtilizationPercent = 80.0;

	private const int PoorRssiThresholdDbm = -75;

	private const double BitsPerByte = 8.0;

	private const double BitsPerMegabit = 1000000.0;

	private const double MinimumThroughputChartScale = 0.4;

	private const double MinimumBandwidthChartScale = 0.4;

	private readonly OpenMANET_Client _client;

	private readonly string _settingsFilePath;

	private readonly DispatcherTimer _autoRefreshTimer;

	private readonly DispatcherTimer _throughputTimer;

	private CancellationTokenSource _operationCancellation;

	private readonly List<ThroughputSample> _throughputSamples = new List<ThroughputSample>();

	private readonly List<BandwidthSample> _bandwidthSamples = new List<BandwidthSample>();

	private readonly ObservableCollection<MeshEventEntry> _meshEvents = new ObservableCollection<MeshEventEntry>();

	private readonly ObservableCollection<HealthFindingEntry> _healthFindings = new ObservableCollection<HealthFindingEntry>();

	private readonly Dictionary<string, NodeStateSnapshot> _previousNodeStates = new Dictionary<string, NodeStateSnapshot>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, List<NodeTrendSample>> _nodeTrendHistory = new Dictionary<string, List<NodeTrendSample>>(StringComparer.OrdinalIgnoreCase);

	private List<OpenMANET_NodeInfo> _lastNodes = new List<OpenMANET_NodeInfo>();

	private bool _isConnected;

	private bool _refreshInProgress;

	private bool _throughputSampleInProgress;

	private bool _bandwidthSampleInProgress;

	private bool _nodeSelectionInProgress;

	private bool _topologySelectionInProgress;

	private string _selectedNodeKey;

	private OpenMANET_TopologyInfo _lastTopology;

	private OpenMANET_MeshTrafficCounters _previousThroughputCounters;

	private double? _currentBandwidthMbps;

	private double? _currentThroughputMbps;

	private double? _currentUtilizationPercent;

	private bool? _bandwidthIsLow;

	private bool? _meshInterfaceAvailable;

	private string _utilizationState;

	private bool _nodeBaselineEstablished;

	private bool _refreshFailureActive;

	private string _previousTopologyHealth;

	private SystemStateSnapshot _previousSystemState;

	public OpenMANET()
	{
		InitializeComponent();
		if (!DesignerProperties.GetIsInDesignMode(this))
		{
			_client = new OpenMANET_Client();
			_autoRefreshTimer = new DispatcherTimer
			{
				Interval = AutoRefreshInterval
			};
			_autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
			_throughputTimer = new DispatcherTimer
			{
				Interval = ThroughputSampleInterval
			};
			_throughputTimer.Tick += ThroughputTimer_Tick;
			Unloaded += OpenMANET_Unloaded;
			string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinTAK", "Plugins", "WinTAK_OpenMANET_Plugin");
			_settingsFilePath = System.IO.Path.Combine(path, "connection.settings");
			LoadConnectionSettings();
			ConnectButton.Click += ConnectButton_Click;
			RefreshButton.Click += RefreshButton_Click;
			NodesListBox.SelectionChanged += NodesListBox_SelectionChanged;
			LinkQualityNodeComboBox.SelectionChanged += LinkQualityNodeComboBox_SelectionChanged;
			TopologyNodesListBox.SelectionChanged += TopologyNodesListBox_SelectionChanged;
			TopologyCanvas.SizeChanged += TopologyCanvas_SizeChanged;
			ThroughputChartCanvas.SizeChanged += ThroughputChartCanvas_SizeChanged;
			BandwidthChartCanvas.SizeChanged += BandwidthChartCanvas_SizeChanged;
			NodeRssiTrendCanvas.SizeChanged += NodeTrendCanvas_SizeChanged;
			NodeBandwidthTrendCanvas.SizeChanged += NodeTrendCanvas_SizeChanged;
			NodeHopsTrendCanvas.SizeChanged += NodeTrendCanvas_SizeChanged;
			NodeAgeTrendCanvas.SizeChanged += NodeTrendCanvas_SizeChanged;
			ExportDiagnosticsButton.Click += ExportDiagnosticsButton_Click;
			ExportFullDiagnosticsButton.Click += ExportFullDiagnosticsButton_Click;
			AllowInsecureHttpCheckBox.Checked += AllowInsecureHttpCheckBox_Changed;
			AllowInsecureHttpCheckBox.Unchecked += AllowInsecureHttpCheckBox_Changed;
			MeshEventsListBox.ItemsSource = _meshEvents;
			HealthFindingsItemsControl.ItemsSource = _healthFindings;
			UpdateConnectionState(connected: false);
			ClearNodeDetails();
			ClearTopology();
			ClearSystemDetails();
			ClearBandwidth("Connect to begin sampling");
			ClearThroughput("Connect to begin sampling");
			UpdateMeshEventEmptyState();
			UpdateHealthFindings();
			ClearNodeTrends();
		}
	}

	private async void ConnectButton_Click(object sender, RoutedEventArgs e)
	{
		if (_isConnected)
		{
			ConnectButton.IsEnabled = false;
			try
			{
				await ResetConnectionWithLogoutAsync();
				return;
			}
			finally
			{
				ConnectButton.IsEnabled = true;
			}
		}
		if (!int.TryParse(GatewayPortTextBox.Text, out var port))
		{
			MessageBox.Show("Port must be a valid number.", "OpenMANET", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			GatewayPortTextBox.Focus();
			return;
		}
		string password = PasswordInput.Password;
		PasswordInput.Password = string.Empty;
		if (AllowInsecureHttpCheckBox.IsChecked == true && MessageBox.Show("Legacy HTTP sends the OpenMANET username, password, bearer token, and telemetry without encryption. Continue only across a trusted protected path.", "OpenMANET Security Warning", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			CancelOutstandingOperations();
			_operationCancellation = new CancellationTokenSource();
			ConnectButton.IsEnabled = false;
			ConnectionStatusText.Text = "Connecting...";
			ConnectionStatusIndicator.Fill = Brushes.Goldenrod;
			await _client.LoginAsync(GatewayHostTextBox.Text.Trim(), port, UsernameTextBox.Text.Trim(), password, AllowInsecureHttpCheckBox.IsChecked == true, _operationCancellation.Token);
			SaveConnectionSettings();
			GatewayStatusText.Text = $"{GatewayHostTextBox.Text.Trim()}:{port}";
			UpdateConnectionState(connected: true);
			AddMeshEvent((AllowInsecureHttpCheckBox.IsChecked == true) ? "WARNING" : "INFO", "Connected to OpenMANET gateway " + GatewayHostTextBox.Text.Trim() + ":" + port + ((AllowInsecureHttpCheckBox.IsChecked == true) ? " using INSECURE HTTP. Credentials, the bearer token, and telemetry are not encrypted." : " using trusted HTTPS."));
			_autoRefreshTimer.Start();
			await RefreshAsync();
			ClearBandwidth("Waiting for link estimate...");
			await SampleBandwidthAsync();
			ClearThroughput("Collecting baseline...");
			await SampleThroughputAsync();
			_throughputTimer.Start();
		}
		catch (Exception exception)
		{
			await ResetConnectionWithLogoutAsync();
			MessageBox.Show(OpenMANET_Security.OperatorMessage(exception), "OpenMANET", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			PasswordInput.Password = string.Empty;
			ConnectButton.IsEnabled = true;
		}
	}

	private void AllowInsecureHttpCheckBox_Changed(object sender, RoutedEventArgs e)
	{
		bool valueOrDefault = AllowInsecureHttpCheckBox.IsChecked == true;
		string a = GatewayPortTextBox.Text.Trim();
		if (valueOrDefault && string.Equals(a, "8081", StringComparison.Ordinal))
		{
			GatewayPortTextBox.Text = "8087";
		}
		else if (!valueOrDefault && string.Equals(a, "8087", StringComparison.Ordinal))
		{
			GatewayPortTextBox.Text = "8081";
		}
		TransportModeText.Text = (valueOrDefault ? "Legacy mode: direct HTTP API. Credentials and telemetry are not encrypted." : "Secure mode: trusted HTTPS frontend with the /auth and /rpc proxies and normal Windows certificate validation.");
	}

	private async void RefreshButton_Click(object sender, RoutedEventArgs e)
	{
		await RefreshAsync();
	}

	private async void AutoRefreshTimer_Tick(object sender, EventArgs e)
	{
		await RefreshAsync();
	}

	private async void ThroughputTimer_Tick(object sender, EventArgs e)
	{
		await SampleBandwidthAsync();
		await SampleThroughputAsync();
	}

	private async Task RefreshAsync()
	{
		if (!_isConnected || _refreshInProgress)
		{
			return;
		}
		_refreshInProgress = true;
		try
		{
			RefreshButton.IsEnabled = false;
			RefreshButton.Content = "Refreshing...";
			List<OpenMANET_NodeInfo> list = await _client.ListNodesAsync(GetOperationToken());
			UpdateTopology(_client.LastTopology);
			UpdateSystemDetails(_client.LastSystemDetails);
			UpdateNodeEvents(list);
			UpdateOperationalEvents(_client.LastTopology, _client.LastSystemDetails);
			CaptureNodeTrendSamples(list);
			_lastNodes = list;
			if (_refreshFailureActive)
			{
				AddMeshEvent("INFO", "OpenMANET polling recovered.");
				_refreshFailureActive = false;
				ConnectionStatusText.Text = "Connected";
				ConnectionStatusIndicator.Fill = Brushes.LimeGreen;
			}
			OpenMANET_NodeInfo openMANET_NodeInfo = FindSelectedNode(list);
			NodesListBox.ItemsSource = list;
			if (openMANET_NodeInfo != null)
			{
				NodesListBox.SelectedItem = openMANET_NodeInfo;
			}
			else if (list.Count > 0)
			{
				NodesListBox.SelectedIndex = 0;
			}
			else
			{
				ClearNodeDetails();
			}
			_nodeSelectionInProgress = true;
			try
			{
				LinkQualityNodeComboBox.ItemsSource = list;
				LinkQualityNodeComboBox.SelectedItem = NodesListBox.SelectedItem;
			}
			finally
			{
				_nodeSelectionInProgress = false;
			}
			int count = list.FindAll((OpenMANET_NodeInfo node) => node.IsOnline).Count;
			NodeCountText.Text = count.ToString();
			LastPollText.Text = DateTime.Now.ToString("HH:mm:ss");
			UpdateHealthFindings();
		}
		catch (Exception ex)
		{
			if (ex is OpenMANET_AuthenticationException)
			{
				HandleExpiredSession();
				return;
			}
			string text = OpenMANET_Security.OperatorMessage(ex);
			if (!_refreshFailureActive)
			{
				AddMeshEvent("WARNING", "OpenMANET polling failed: " + text);
				_refreshFailureActive = true;
			}
			UpdateHealthFindings();
			ConnectionStatusText.Text = "Connected - polling delayed";
			ConnectionStatusIndicator.Fill = Brushes.Goldenrod;
		}
		finally
		{
			_refreshInProgress = false;
			RefreshButton.Content = "Refresh";
			RefreshButton.IsEnabled = _isConnected;
		}
	}

	private void NodesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!(NodesListBox.SelectedItem is OpenMANET_NodeInfo openMANET_NodeInfo))
		{
			ClearNodeDetails();
			return;
		}
		_selectedNodeKey = GetNodeSelectionKey(openMANET_NodeInfo);
		if (!_nodeSelectionInProgress)
		{
			_nodeSelectionInProgress = true;
			try
			{
				LinkQualityNodeComboBox.SelectedItem = openMANET_NodeInfo;
			}
			finally
			{
				_nodeSelectionInProgress = false;
			}
		}
		NodeDetailsPanel.DataContext = openMANET_NodeInfo;
		NodeDetailsPlaceholder.Visibility = Visibility.Collapsed;
		NodeDetailsContent.Visibility = Visibility.Visible;
		SelectTopologyNode(openMANET_NodeInfo);
		RenderTopology();
		RenderNodeTrends();
	}

	private void LinkQualityNodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_nodeSelectionInProgress || !(LinkQualityNodeComboBox.SelectedItem is OpenMANET_NodeInfo openMANET_NodeInfo))
		{
			return;
		}
		_nodeSelectionInProgress = true;
		try
		{
			NodesListBox.SelectedItem = openMANET_NodeInfo;
			NodesListBox.ScrollIntoView(openMANET_NodeInfo);
		}
		finally
		{
			_nodeSelectionInProgress = false;
		}
	}

	private void TopologyNodesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_topologySelectionInProgress)
		{
			return;
		}
		if (!(TopologyNodesListBox.SelectedItem is OpenMANET_TopologyNodeInfo topologyNode))
		{
			RenderTopology();
			return;
		}
		foreach (OpenMANET_NodeInfo item in (IEnumerable)NodesListBox.Items)
		{
			if (TopologyNodeMatchesNode(topologyNode, item))
			{
				NodesListBox.SelectedItem = item;
				NodesListBox.ScrollIntoView(item);
				break;
			}
		}
		RenderTopology();
	}

	private void TopologyCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		RenderTopology();
	}

	private void ThroughputChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		RenderThroughputChart();
	}

	private void BandwidthChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		RenderBandwidthChart();
	}

	private void NodeTrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		RenderNodeTrends();
	}

	private async Task SampleBandwidthAsync()
	{
		if (!_isConnected || _bandwidthSampleInProgress)
		{
			return;
		}
		_bandwidthSampleInProgress = true;
		try
		{
			OpenMANET_MeshBandwidth openMANET_MeshBandwidth = await _client.GetMeshBandwidthAsync(GetOperationToken());
			if (!_isConnected)
			{
				return;
			}
			if (openMANET_MeshBandwidth == null || !openMANET_MeshBandwidth.HasEstimate)
			{
				_currentBandwidthMbps = null;
				_bandwidthIsLow = null;
				ClearBandwidth("No direct-link bandwidth estimate reported");
				UpdateUtilization();
				UpdateHealthFindings();
				return;
			}
			BandwidthSample bandwidthSample = new BandwidthSample
			{
				CapturedAtUtc = openMANET_MeshBandwidth.CapturedAtUtc,
				MegabitsPerSecond = openMANET_MeshBandwidth.MegabitsPerSecond
			};
			_bandwidthSamples.Add(bandwidthSample);
			DateTimeOffset dateTimeOffset = bandwidthSample.CapturedAtUtc - BandwidthWindow;
			while (_bandwidthSamples.Count > 0 && _bandwidthSamples[0].CapturedAtUtc < dateTimeOffset)
			{
				_bandwidthSamples.RemoveAt(0);
			}
			BandwidthCurrentValueText.Text = FormatThroughput(bandwidthSample.MegabitsPerSecond);
			_currentBandwidthMbps = bandwidthSample.MegabitsPerSecond;
			bool flag = bandwidthSample.MegabitsPerSecond < 5.0;
			if (_bandwidthIsLow.HasValue && _bandwidthIsLow.Value != flag)
			{
				AddMeshEvent(flag ? "WARNING" : "INFO", flag ? ("Mesh bandwidth fell below " + 5.0.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps.") : ("Mesh bandwidth recovered above " + 5.0.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps."));
			}
			_bandwidthIsLow = flag;
			BandwidthEmptyState.Visibility = Visibility.Collapsed;
			RenderBandwidthChart();
			UpdateUtilization();
			UpdateHealthFindings();
		}
		catch (OpenMANET_AuthenticationException)
		{
			HandleExpiredSession();
		}
		catch
		{
			if (_isConnected)
			{
				_currentBandwidthMbps = null;
				BandwidthCurrentValueText.Text = "—";
				if (_bandwidthSamples.Count == 0)
				{
					BandwidthStatusText.Text = "Bandwidth telemetry unavailable";
					BandwidthEmptyState.Visibility = Visibility.Visible;
				}
				UpdateUtilization();
				UpdateHealthFindings();
			}
		}
		finally
		{
			_bandwidthSampleInProgress = false;
		}
	}

	private void RenderBandwidthChart()
	{
		PointCollection pointCollection = new PointCollection();
		double actualWidth = BandwidthChartCanvas.ActualWidth;
		double actualHeight = BandwidthChartCanvas.ActualHeight;
		if (_bandwidthSamples.Count == 0 || actualWidth <= 0.0 || actualHeight <= 0.0)
		{
			BandwidthLine.Points = pointCollection;
			BandwidthMarker.Visibility = Visibility.Collapsed;
			return;
		}
		double num = 0.4;
		foreach (BandwidthSample bandwidthSample in _bandwidthSamples)
		{
			num = Math.Max(num, bandwidthSample.MegabitsPerSecond);
		}
		num *= 1.1;
		DateTimeOffset capturedAtUtc = _bandwidthSamples[_bandwidthSamples.Count - 1].CapturedAtUtc;
		foreach (BandwidthSample bandwidthSample2 in _bandwidthSamples)
		{
			double totalSeconds = (capturedAtUtc - bandwidthSample2.CapturedAtUtc).TotalSeconds;
			double x = 3.0 + (actualWidth - 6.0) * (1.0 - Math.Min(1.0, Math.Max(0.0, totalSeconds / BandwidthWindow.TotalSeconds)));
			pointCollection.Add(new Point(x, GetThroughputChartY(bandwidthSample2.MegabitsPerSecond, num, actualHeight)));
		}
		BandwidthLine.Points = pointCollection;
		Point point = pointCollection[pointCollection.Count - 1];
		Canvas.SetLeft(BandwidthMarker, point.X - 3.0);
		Canvas.SetTop(BandwidthMarker, point.Y - 3.0);
		BandwidthMarker.Visibility = Visibility.Visible;
	}

	private void ClearBandwidth(string status)
	{
		_bandwidthSamples.Clear();
		_currentBandwidthMbps = null;
		BandwidthCurrentValueText.Text = "—";
		BandwidthStatusText.Text = status;
		BandwidthEmptyState.Visibility = Visibility.Visible;
		RenderBandwidthChart();
	}

	private async Task SampleThroughputAsync()
	{
		if (!_isConnected || _throughputSampleInProgress)
		{
			return;
		}
		_throughputSampleInProgress = true;
		try
		{
			OpenMANET_MeshTrafficCounters openMANET_MeshTrafficCounters = await _client.GetMeshTrafficCountersAsync(GetOperationToken());
			if (!_isConnected)
			{
				return;
			}
			if (openMANET_MeshTrafficCounters == null || !openMANET_MeshTrafficCounters.HasMeshInterface)
			{
				UpdateMeshInterfaceAvailability(available: false);
				_currentThroughputMbps = null;
				ClearThroughput("No HaLow mesh interface reported");
				UpdateUtilization();
				UpdateHealthFindings();
				return;
			}
			UpdateMeshInterfaceAvailability(available: true);
			if (_previousThroughputCounters == null)
			{
				_previousThroughputCounters = openMANET_MeshTrafficCounters;
				ThroughputStatusText.Text = "Collecting baseline...";
				return;
			}
			double totalSeconds = (openMANET_MeshTrafficCounters.CapturedAtUtc - _previousThroughputCounters.CapturedAtUtc).TotalSeconds;
			long num = openMANET_MeshTrafficCounters.RxBytes - _previousThroughputCounters.RxBytes;
			long num2 = openMANET_MeshTrafficCounters.TxBytes - _previousThroughputCounters.TxBytes;
			_previousThroughputCounters = openMANET_MeshTrafficCounters;
			if (totalSeconds <= 0.0 || num < 0 || num2 < 0)
			{
				ThroughputStatusText.Text = "Counters reset; collecting baseline...";
				return;
			}
			ThroughputSample throughputSample = new ThroughputSample
			{
				CapturedAtUtc = openMANET_MeshTrafficCounters.CapturedAtUtc,
				RxMegabitsPerSecond = (double)num * 8.0 / totalSeconds / 1000000.0,
				TxMegabitsPerSecond = (double)num2 * 8.0 / totalSeconds / 1000000.0
			};
			_throughputSamples.Add(throughputSample);
			DateTimeOffset dateTimeOffset = throughputSample.CapturedAtUtc - ThroughputWindow;
			while (_throughputSamples.Count > 0 && _throughputSamples[0].CapturedAtUtc < dateTimeOffset)
			{
				_throughputSamples.RemoveAt(0);
			}
			ThroughputCurrentValueText.Text = FormatThroughput(throughputSample.RxMegabitsPerSecond + throughputSample.TxMegabitsPerSecond);
			_currentThroughputMbps = throughputSample.RxMegabitsPerSecond + throughputSample.TxMegabitsPerSecond;
			ThroughputEmptyState.Visibility = Visibility.Collapsed;
			RenderThroughputChart();
			UpdateUtilization();
			UpdateHealthFindings();
		}
		catch (OpenMANET_AuthenticationException)
		{
			HandleExpiredSession();
		}
		catch
		{
			if (_isConnected)
			{
				UpdateMeshInterfaceAvailability(available: false);
				_currentThroughputMbps = null;
				_previousThroughputCounters = null;
				ThroughputCurrentValueText.Text = "—";
				if (_throughputSamples.Count == 0)
				{
					ThroughputStatusText.Text = "Throughput telemetry unavailable";
					ThroughputEmptyState.Visibility = Visibility.Visible;
				}
				UpdateUtilization();
				UpdateHealthFindings();
			}
		}
		finally
		{
			_throughputSampleInProgress = false;
		}
	}

	private void RenderThroughputChart()
	{
		PointCollection pointCollection = new PointCollection();
		PointCollection pointCollection2 = new PointCollection();
		double actualWidth = ThroughputChartCanvas.ActualWidth;
		double actualHeight = ThroughputChartCanvas.ActualHeight;
		if (_throughputSamples.Count == 0 || actualWidth <= 0.0 || actualHeight <= 0.0)
		{
			ThroughputRxLine.Points = pointCollection;
			ThroughputTxLine.Points = pointCollection2;
			ThroughputRxMarker.Visibility = Visibility.Collapsed;
			ThroughputTxMarker.Visibility = Visibility.Collapsed;
			return;
		}
		double num = 0.4;
		foreach (ThroughputSample throughputSample in _throughputSamples)
		{
			num = Math.Max(num, Math.Max(throughputSample.RxMegabitsPerSecond, throughputSample.TxMegabitsPerSecond));
		}
		num *= 1.1;
		DateTimeOffset capturedAtUtc = _throughputSamples[_throughputSamples.Count - 1].CapturedAtUtc;
		foreach (ThroughputSample throughputSample2 in _throughputSamples)
		{
			double totalSeconds = (capturedAtUtc - throughputSample2.CapturedAtUtc).TotalSeconds;
			double x = 3.0 + (actualWidth - 6.0) * (1.0 - Math.Min(1.0, Math.Max(0.0, totalSeconds / ThroughputWindow.TotalSeconds)));
			pointCollection.Add(new Point(x, GetThroughputChartY(throughputSample2.RxMegabitsPerSecond, num, actualHeight)));
			pointCollection2.Add(new Point(x, GetThroughputChartY(throughputSample2.TxMegabitsPerSecond, num, actualHeight)));
		}
		ThroughputRxLine.Points = pointCollection;
		ThroughputTxLine.Points = pointCollection2;
		Point point = pointCollection[pointCollection.Count - 1];
		Point point2 = pointCollection2[pointCollection2.Count - 1];
		Canvas.SetLeft(ThroughputRxMarker, point.X - 3.0);
		Canvas.SetTop(ThroughputRxMarker, point.Y - 3.0);
		Canvas.SetLeft(ThroughputTxMarker, point2.X - 3.0);
		Canvas.SetTop(ThroughputTxMarker, point2.Y - 3.0);
		ThroughputRxMarker.Visibility = Visibility.Visible;
		ThroughputTxMarker.Visibility = Visibility.Visible;
	}

	private static double GetThroughputChartY(double value, double maximumValue, double height)
	{
		double num = ((maximumValue <= 0.0) ? 0.0 : Math.Min(1.0, Math.Max(0.0, value / maximumValue)));
		return height - 4.0 - num * Math.Max(0.0, height - 8.0);
	}

	private static string FormatThroughput(double value)
	{
		string text = ((value >= 1.0) ? "0.00" : "0.000");
		return Math.Max(0.0, value).ToString(text, CultureInfo.InvariantCulture);
	}

	private void ClearThroughput(string status)
	{
		_previousThroughputCounters = null;
		_throughputSamples.Clear();
		_currentThroughputMbps = null;
		ThroughputCurrentValueText.Text = "—";
		ThroughputStatusText.Text = status;
		ThroughputEmptyState.Visibility = Visibility.Visible;
		RenderThroughputChart();
	}

	private void UpdateMeshInterfaceAvailability(bool available)
	{
		if (_meshInterfaceAvailable.HasValue && _meshInterfaceAvailable.Value != available)
		{
			AddMeshEvent(available ? "INFO" : "CRITICAL", available ? "HaLow mesh interface recovered." : "HaLow mesh interface is unavailable.");
		}
		_meshInterfaceAvailable = available;
	}

	private void UpdateUtilization()
	{
		if (!_currentBandwidthMbps.HasValue || !_currentThroughputMbps.HasValue || _currentBandwidthMbps.Value <= 0.0)
		{
			_currentUtilizationPercent = null;
			UtilizationCurrentValueText.Text = "—";
			UtilizationProgressBar.Value = 0.0;
			UtilizationProgressBar.Foreground = (FindResource("MetricBrush") as Brush) ?? Brushes.Teal;
			UtilizationCurrentValueText.Foreground = UtilizationProgressBar.Foreground;
			UtilizationStatusText.Text = "Waiting for bandwidth and throughput samples";
			return;
		}
		double num = 100.0 * _currentThroughputMbps.Value / _currentBandwidthMbps.Value;
		if (double.IsNaN(num) || double.IsInfinity(num))
		{
			num = 0.0;
		}
		num = Math.Max(0.0, num);
		_currentUtilizationPercent = num;
		UtilizationCurrentValueText.Text = num.ToString("0.0", CultureInfo.InvariantCulture) + "%";
		UtilizationProgressBar.Value = Math.Min(100.0, num);
		Brush foreground;
		string text;
		string text2;
		if (num >= 80.0)
		{
			foreground = (FindResource("DangerBrush") as Brush) ?? Brushes.Tomato;
			text = "High utilization relative to estimated link capacity";
			text2 = "Critical";
		}
		else if (num >= 60.0)
		{
			foreground = (FindResource("WarningBrush") as Brush) ?? Brushes.Goldenrod;
			text = "Elevated utilization relative to estimated link capacity";
			text2 = "Warning";
		}
		else
		{
			foreground = (FindResource("MetricBrush") as Brush) ?? Brushes.Teal;
			text = "Throughput compared with conservative mesh bandwidth";
			text2 = "Normal";
		}
		if (!string.Equals(_utilizationState, text2, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(_utilizationState))
		{
			OpenMANET openMANET = this;
			string severity;
			if (text2 == "Critical")
			{
				severity = "CRITICAL";
			}
			else
			{
				severity = ((text2 == "Warning") ? "WARNING" : "INFO");
			}
			openMANET.AddMeshEvent(severity, (text2 == "Normal") ? ("Mesh utilization returned below " + 60.0.ToString("0", CultureInfo.InvariantCulture) + "%.") : ("Mesh utilization entered the " + text2.ToLowerInvariant() + " range at " + num.ToString("0.0", CultureInfo.InvariantCulture) + "%."));
		}
		_utilizationState = text2;
		UtilizationProgressBar.Foreground = foreground;
		UtilizationCurrentValueText.Foreground = foreground;
		UtilizationStatusText.Text = text;
	}

	private void AddMeshEvent(string severity, string message)
	{
		string text = OpenMANET_Security.NormalizeDetail(message);
		if (!string.IsNullOrWhiteSpace(text))
		{
			string severity2;
			if (string.Equals(severity, "CRITICAL", StringComparison.OrdinalIgnoreCase))
			{
				severity2 = "CRITICAL";
			}
			else
			{
				severity2 = (string.Equals(severity, "WARNING", StringComparison.OrdinalIgnoreCase) ? "WARNING" : "INFO");
			}
			_meshEvents.Insert(0, new MeshEventEntry
			{
				CapturedAt = DateTimeOffset.Now,
				Severity = severity2,
				Message = text
			});
			while (_meshEvents.Count > 100)
			{
				_meshEvents.RemoveAt(_meshEvents.Count - 1);
			}
			UpdateMeshEventEmptyState();
		}
	}

	private void UpdateMeshEventEmptyState()
	{
		MeshEventsEmptyState.Visibility = ((_meshEvents.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void UpdateNodeEvents(List<OpenMANET_NodeInfo> nodes)
	{
		Dictionary<string, NodeStateSnapshot> dictionary = new Dictionary<string, NodeStateSnapshot>(StringComparer.OrdinalIgnoreCase);
		foreach (OpenMANET_NodeInfo node in nodes)
		{
			string nodeSelectionKey = GetNodeSelectionKey(node);
			if (!string.IsNullOrWhiteSpace(nodeSelectionKey))
			{
				dictionary[nodeSelectionKey] = NodeStateSnapshot.FromNode(node);
			}
		}
		if (!_nodeBaselineEstablished)
		{
			_nodeBaselineEstablished = true;
			AddMeshEvent("INFO", "Mesh baseline established with " + nodes.Count.ToString(CultureInfo.InvariantCulture) + ((nodes.Count == 1) ? " node." : " nodes."));
		}
		else
		{
			foreach (KeyValuePair<string, NodeStateSnapshot> item in dictionary)
			{
				if (!_previousNodeStates.TryGetValue(item.Key, out var value))
				{
					AddMeshEvent("INFO", item.Value.Name + " joined the mesh.");
					continue;
				}
				NodeStateSnapshot value2 = item.Value;
				if (value.IsOnline && !value2.IsOnline)
				{
					AddMeshEvent("WARNING", value2.Name + " became stale.");
				}
				else if (!value.IsOnline && value2.IsOnline)
				{
					AddMeshEvent("INFO", value2.Name + " recovered.");
				}
				if (value.HopsFromSelf != value2.HopsFromSelf && value.HopsFromSelf.HasValue && value2.HopsFromSelf.HasValue)
				{
					AddMeshEvent("INFO", value2.Name + " hop count changed from " + value.HopsFromSelf.Value.ToString(CultureInfo.InvariantCulture) + " to " + value2.HopsFromSelf.Value.ToString(CultureInfo.InvariantCulture) + ".");
				}
				if (!string.Equals(value.RouteInterface, value2.RouteInterface, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value.RouteInterface) && !string.IsNullOrWhiteSpace(value2.RouteInterface))
				{
					AddMeshEvent("INFO", value2.Name + " route changed from " + value.RouteInterface + " to " + value2.RouteInterface + ".");
				}
				if (!string.Equals(value.RemoteGatewayMac, value2.RemoteGatewayMac, StringComparison.OrdinalIgnoreCase) && (!string.IsNullOrWhiteSpace(value.RemoteGatewayMac) || !string.IsNullOrWhiteSpace(value2.RemoteGatewayMac)))
				{
					AddMeshEvent("INFO", value2.Name + " remote gateway changed from " + DisplayValue(value.RemoteGatewayMac) + " to " + DisplayValue(value2.RemoteGatewayMac) + ".");
				}
				bool num = value.Rssi.HasValue && value.Rssi.Value < -75;
				bool flag = value2.Rssi.HasValue && value2.Rssi.Value < -75;
				if (num != flag)
				{
					AddMeshEvent(flag ? "WARNING" : "INFO", flag ? (value2.Name + " RSSI degraded below " + (-75).ToString(CultureInfo.InvariantCulture) + " dBm.") : (value2.Name + " RSSI recovered above " + (-75).ToString(CultureInfo.InvariantCulture) + " dBm."));
				}
			}
			foreach (KeyValuePair<string, NodeStateSnapshot> previousNodeState in _previousNodeStates)
			{
				if (!dictionary.ContainsKey(previousNodeState.Key))
				{
					AddMeshEvent("WARNING", previousNodeState.Value.Name + " left the mesh.");
				}
			}
		}
		_previousNodeStates.Clear();
		foreach (KeyValuePair<string, NodeStateSnapshot> item2 in dictionary)
		{
			_previousNodeStates[item2.Key] = item2.Value;
		}
	}

	private void UpdateOperationalEvents(OpenMANET_TopologyInfo topology, OpenMANET_SystemInfo system)
	{
		string text = ((topology == null) ? "Offline" : topology.HealthDisplay);
		if (!string.IsNullOrWhiteSpace(_previousTopologyHealth) && !string.Equals(_previousTopologyHealth, text, StringComparison.OrdinalIgnoreCase))
		{
			OpenMANET openMANET = this;
			string severity;
			if (text == "Healthy")
			{
				severity = "INFO";
			}
			else
			{
				severity = ((text == "Offline") ? "CRITICAL" : "WARNING");
			}
			openMANET.AddMeshEvent(severity, "Mesh topology changed from " + _previousTopologyHealth + " to " + text + ".");
		}
		_previousTopologyHealth = text;
		if (system == null)
		{
			_previousSystemState = null;
			return;
		}
		SystemStateSnapshot systemStateSnapshot = SystemStateSnapshot.FromSystem(system);
		if (_previousSystemState != null)
		{
			AddThresholdTransition("Gateway CPU load", _previousSystemState.HighCpu, systemStateSnapshot.HighCpu, "90%");
			AddThresholdTransition("Gateway memory usage", _previousSystemState.HighMemory, systemStateSnapshot.HighMemory, "90%");
			AddThresholdTransition("Gateway storage usage", _previousSystemState.HighStorage, systemStateSnapshot.HighStorage, "90%");
			AddThresholdTransition("Gateway CPU temperature", _previousSystemState.HighTemperature, systemStateSnapshot.HighTemperature, "80 °C");
		}
		_previousSystemState = systemStateSnapshot;
	}

	private void AddThresholdTransition(string metric, bool previousHigh, bool currentHigh, string threshold)
	{
		if (previousHigh != currentHigh)
		{
			AddMeshEvent(currentHigh ? "WARNING" : "INFO", currentHigh ? (metric + " crossed the " + threshold + " warning threshold.") : (metric + " recovered below the " + threshold + " warning threshold."));
		}
	}

	private static string DisplayValue(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim().ToUpperInvariant();
		}
		return "none";
	}

	private void UpdateHealthFindings()
	{
		_healthFindings.Clear();
		bool flag = false;
		bool flag2 = false;
		if (!_isConnected)
		{
			AddHealthFinding("OpenMANET gateway is not connected.", Brushes.Tomato, critical: true);
			flag = true;
		}
		else
		{
			if (AllowInsecureHttpCheckBox.IsChecked == true)
			{
				AddHealthFinding("Connection uses insecure HTTP; credentials, the bearer token, and telemetry are not encrypted.", Brushes.Goldenrod, critical: false);
				flag2 = true;
			}
			if (_lastNodes.Count == 0)
			{
				AddHealthFinding("No mesh nodes are reporting.", Brushes.Tomato, critical: true);
				flag = true;
			}
			if (_refreshFailureActive)
			{
				AddHealthFinding("The latest OpenMANET poll failed; displayed data may be stale.", Brushes.Goldenrod, critical: false);
				flag2 = true;
			}
			foreach (OpenMANET_NodeInfo lastNode in _lastNodes)
			{
				string text = (string.IsNullOrWhiteSpace(lastNode.Name) ? "Unknown node" : lastNode.Name);
				if (!lastNode.IsOnline)
				{
					AddHealthFinding(text + " is stale.", Brushes.Goldenrod, critical: false);
					flag2 = true;
				}
				if (lastNode.GossipStale && lastNode.IsOnline)
				{
					AddHealthFinding(text + " topology data is stale.", Brushes.Goldenrod, critical: false);
					flag2 = true;
				}
				int? num = lastNode.CurrentRssi ?? lastNode.Rssi;
				if (num.HasValue && num.Value < -75)
				{
					AddHealthFinding(text + " RSSI is " + num.Value.ToString(CultureInfo.InvariantCulture) + " dBm.", Brushes.Goldenrod, critical: false);
					flag2 = true;
				}
			}
			if (_meshInterfaceAvailable.HasValue && !_meshInterfaceAvailable.Value)
			{
				AddHealthFinding("The HaLow mesh interface is unavailable.", Brushes.Tomato, critical: true);
				flag = true;
			}
			if (_currentBandwidthMbps.HasValue && _currentBandwidthMbps.Value < 5.0)
			{
				AddHealthFinding("Mesh bandwidth is " + _currentBandwidthMbps.Value.ToString("0.00", CultureInfo.InvariantCulture) + " Mbps, below the " + 5.0.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps warning threshold.", Brushes.Goldenrod, critical: false);
				flag2 = true;
			}
			if (_currentUtilizationPercent.HasValue && _currentUtilizationPercent.Value >= 60.0)
			{
				bool flag3 = _currentUtilizationPercent.Value >= 80.0;
				AddHealthFinding("Estimated mesh utilization is " + _currentUtilizationPercent.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%.", flag3 ? Brushes.Tomato : Brushes.Goldenrod, flag3);
				flag2 = true;
			}
			OpenMANET_SystemInfo lastSystemDetails = _client.LastSystemDetails;
			if (lastSystemDetails != null)
			{
				if (lastSystemDetails.CpuLoadValue >= 90.0)
				{
					AddHealthFinding("Gateway CPU load is " + lastSystemDetails.CpuLoadValue.ToString("0.0", CultureInfo.InvariantCulture) + "%.", Brushes.Goldenrod, critical: false);
					flag2 = true;
				}
				if (lastSystemDetails.MemoryUsagePercent >= 90.0)
				{
					AddHealthFinding("Gateway memory usage is " + lastSystemDetails.MemoryUsagePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%.", Brushes.Goldenrod, critical: false);
					flag2 = true;
				}
				if (lastSystemDetails.OverlayUsagePercent >= 90.0)
				{
					AddHealthFinding("Gateway storage usage is " + lastSystemDetails.OverlayUsagePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%.", Brushes.Goldenrod, critical: false);
					flag2 = true;
				}
				if (lastSystemDetails.CpuTemperatureCelsius >= 80.0)
				{
					AddHealthFinding("Gateway CPU temperature is " + lastSystemDetails.CpuTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture) + " °C.", Brushes.Goldenrod, critical: false);
					flag2 = true;
				}
			}
		}
		if (_healthFindings.Count == 0)
		{
			AddHealthFinding("No active health findings.", Brushes.LimeGreen, critical: false);
		}
		TextBlock healthSummaryText = HealthSummaryText;
		string text2;
		if (flag)
		{
			text2 = "Offline";
		}
		else
		{
			text2 = (flag2 ? "Degraded" : "Healthy");
		}
		healthSummaryText.Text = text2;
		TextBlock healthSummaryText2 = HealthSummaryText;
		SolidColorBrush foreground;
		if (flag)
		{
			foreground = Brushes.Tomato;
		}
		else
		{
			foreground = (flag2 ? Brushes.Goldenrod : Brushes.LimeGreen);
		}
		healthSummaryText2.Foreground = foreground;
	}

	private void AddHealthFinding(string message, Brush indicatorBrush, bool critical)
	{
		_healthFindings.Add(new HealthFindingEntry
		{
			Message = message,
			IndicatorBrush = indicatorBrush,
			IsCritical = critical
		});
	}

	private void CaptureNodeTrendSamples(List<OpenMANET_NodeInfo> nodes)
	{
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		DateTimeOffset dateTimeOffset = utcNow - NodeTrendWindow;
		foreach (OpenMANET_NodeInfo node in nodes)
		{
			string nodeSelectionKey = GetNodeSelectionKey(node);
			if (!string.IsNullOrWhiteSpace(nodeSelectionKey))
			{
				if (!_nodeTrendHistory.TryGetValue(nodeSelectionKey, out var value))
				{
					value = new List<NodeTrendSample>();
					_nodeTrendHistory[nodeSelectionKey] = value;
				}
				int? num = node.CurrentRssi ?? node.Rssi;
				double? bandwidthMbps = ((node.ThroughputKbps.HasValue && node.ThroughputKbps.Value >= 0) ? new double?((double)node.ThroughputKbps.Value / 1000.0) : ((double?)null));
				double? hops = ((node.HopsFromSelf.HasValue && node.HopsFromSelf.Value >= 0 && node.HopsFromSelf.Value < 99) ? new double?(node.HopsFromSelf.Value) : ((double?)null));
				double? ageSeconds;
				if (node.LinkAgeMilliseconds.HasValue && node.LinkAgeMilliseconds.Value >= 0)
				{
					ageSeconds = (double)node.LinkAgeMilliseconds.Value / 1000.0;
				}
				else
				{
					ageSeconds = ((node.GossipAgeSeconds.HasValue && node.GossipAgeSeconds.Value >= 0) ? new double?(node.GossipAgeSeconds.Value) : ((double?)null));
				}
				value.Add(new NodeTrendSample
				{
					CapturedAtUtc = utcNow,
					Rssi = num,
					BandwidthMbps = bandwidthMbps,
					Hops = hops,
					AgeSeconds = ageSeconds
				});
				while (value.Count > 0 && value[0].CapturedAtUtc < dateTimeOffset)
				{
					value.RemoveAt(0);
				}
			}
		}
		RenderNodeTrends();
	}

	private void RenderNodeTrends()
	{
		if (!(NodesListBox.SelectedItem is OpenMANET_NodeInfo node) || !_nodeTrendHistory.TryGetValue(GetNodeSelectionKey(node), out var value) || value.Count == 0)
		{
			ClearNodeTrends();
			return;
		}
		NodeTrendStatusText.Text = value.Count.ToString(CultureInfo.InvariantCulture) + ((value.Count == 1) ? " sample" : " samples") + " retained in memory";
		RenderNodeTrendLine(NodeRssiTrendCanvas, NodeRssiTrendLine, value, (NodeTrendSample sample) => sample.Rssi);
		RenderNodeTrendLine(NodeBandwidthTrendCanvas, NodeBandwidthTrendLine, value, (NodeTrendSample sample) => sample.BandwidthMbps);
		RenderNodeTrendLine(NodeHopsTrendCanvas, NodeHopsTrendLine, value, (NodeTrendSample sample) => sample.Hops);
		RenderNodeTrendLine(NodeAgeTrendCanvas, NodeAgeTrendLine, value, (NodeTrendSample sample) => sample.AgeSeconds);
		NodeTrendSample nodeTrendSample = value[value.Count - 1];
		NodeRssiTrendValueText.Text = (nodeTrendSample.Rssi.HasValue ? (nodeTrendSample.Rssi.Value.ToString("0", CultureInfo.InvariantCulture) + " dBm") : "—");
		NodeBandwidthTrendValueText.Text = (nodeTrendSample.BandwidthMbps.HasValue ? (nodeTrendSample.BandwidthMbps.Value.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps") : "—");
		NodeHopsTrendValueText.Text = (nodeTrendSample.Hops.HasValue ? nodeTrendSample.Hops.Value.ToString("0", CultureInfo.InvariantCulture) : "—");
		NodeAgeTrendValueText.Text = (nodeTrendSample.AgeSeconds.HasValue ? (nodeTrendSample.AgeSeconds.Value.ToString("0.0", CultureInfo.InvariantCulture) + " s") : "—");
	}

	private static void RenderNodeTrendLine(Canvas canvas, Polyline line, List<NodeTrendSample> samples, Func<NodeTrendSample, double?> valueSelector)
	{
		List<NodeTrendValue> list = new List<NodeTrendValue>();
		foreach (NodeTrendSample sample in samples)
		{
			double? num = valueSelector(sample);
			if (num.HasValue && !double.IsNaN(num.Value) && !double.IsInfinity(num.Value))
			{
				list.Add(new NodeTrendValue
				{
					CapturedAtUtc = sample.CapturedAtUtc,
					Value = num.Value
				});
			}
		}
		PointCollection pointCollection = new PointCollection();
		double actualWidth = canvas.ActualWidth;
		double actualHeight = canvas.ActualHeight;
		if (list.Count == 0 || actualWidth <= 0.0 || actualHeight <= 0.0)
		{
			line.Points = pointCollection;
			return;
		}
		double num2 = list[0].Value;
		double num3 = list[0].Value;
		foreach (NodeTrendValue item in list)
		{
			num2 = Math.Min(num2, item.Value);
			num3 = Math.Max(num3, item.Value);
		}
		if (Math.Abs(num3 - num2) < 0.001)
		{
			num2--;
			num3++;
		}
		DateTimeOffset capturedAtUtc = list[list.Count - 1].CapturedAtUtc;
		foreach (NodeTrendValue item2 in list)
		{
			double totalSeconds = (capturedAtUtc - item2.CapturedAtUtc).TotalSeconds;
			double x = 2.0 + (actualWidth - 4.0) * (1.0 - Math.Min(1.0, Math.Max(0.0, totalSeconds / NodeTrendWindow.TotalSeconds)));
			double num4 = (item2.Value - num2) / (num3 - num2);
			double y = actualHeight - 3.0 - num4 * Math.Max(0.0, actualHeight - 6.0);
			pointCollection.Add(new Point(x, y));
		}
		line.Points = pointCollection;
	}

	private void ClearNodeTrends()
	{
		NodeTrendStatusText.Text = "Trend history begins after the first refresh";
		NodeRssiTrendLine.Points = new PointCollection();
		NodeBandwidthTrendLine.Points = new PointCollection();
		NodeHopsTrendLine.Points = new PointCollection();
		NodeAgeTrendLine.Points = new PointCollection();
		NodeRssiTrendValueText.Text = "—";
		NodeBandwidthTrendValueText.Text = "—";
		NodeHopsTrendValueText.Text = "—";
		NodeAgeTrendValueText.Text = "—";
	}

	private OpenMANET_NodeInfo FindSelectedNode(List<OpenMANET_NodeInfo> nodes)
	{
		if (string.IsNullOrWhiteSpace(_selectedNodeKey))
		{
			return null;
		}
		foreach (OpenMANET_NodeInfo node in nodes)
		{
			if (string.Equals(GetNodeSelectionKey(node), _selectedNodeKey, StringComparison.OrdinalIgnoreCase))
			{
				return node;
			}
		}
		return null;
	}

	private static string GetNodeSelectionKey(OpenMANET_NodeInfo node)
	{
		if (!string.IsNullOrWhiteSpace(node.MacAddress) && node.MacAddress != "--")
		{
			return "mac:" + node.MacAddress.Trim();
		}
		return "host:" + (node.Name ?? string.Empty).Trim();
	}

	private void ClearNodeDetails()
	{
		_selectedNodeKey = null;
		NodeDetailsPanel.DataContext = null;
		NodeDetailsContent.Visibility = Visibility.Collapsed;
		NodeDetailsPlaceholder.Visibility = Visibility.Visible;
		ClearNodeTrends();
	}

	private void UpdateTopology(OpenMANET_TopologyInfo topology)
	{
		_lastTopology = topology;
		TopologyContent.DataContext = topology;
		bool flag = topology != null && topology.Nodes.Count > 0;
		TopologyPlaceholder.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		TopologyContent.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		if (!flag)
		{
			TopologyCanvas.Children.Clear();
			return;
		}
		SelectTopologyNode(NodesListBox.SelectedItem as OpenMANET_NodeInfo);
		Dispatcher.BeginInvoke(new Action(RenderTopology), DispatcherPriority.Loaded);
	}

	private void ClearTopology()
	{
		_lastTopology = null;
		TopologyContent.DataContext = null;
		TopologyCanvas.Children.Clear();
		TopologyContent.Visibility = Visibility.Collapsed;
		TopologyPlaceholder.Visibility = Visibility.Visible;
	}

	private void UpdateSystemDetails(OpenMANET_SystemInfo systemDetails)
	{
		SystemDetailsContent.DataContext = systemDetails;
		bool flag = systemDetails != null;
		SystemDetailsPlaceholder.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		SystemDetailsContent.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void ClearSystemDetails()
	{
		SystemDetailsContent.DataContext = null;
		SystemDetailsContent.Visibility = Visibility.Collapsed;
		SystemDetailsPlaceholder.Visibility = Visibility.Visible;
	}

	private void SelectTopologyNode(OpenMANET_NodeInfo node)
	{
		if (_lastTopology == null || node == null)
		{
			return;
		}
		OpenMANET_TopologyNodeInfo openMANET_TopologyNodeInfo = null;
		foreach (OpenMANET_TopologyNodeInfo node2 in _lastTopology.Nodes)
		{
			if (TopologyNodeMatchesNode(node2, node))
			{
				openMANET_TopologyNodeInfo = node2;
				break;
			}
		}
		_topologySelectionInProgress = true;
		try
		{
			TopologyNodesListBox.SelectedItem = openMANET_TopologyNodeInfo;
			if (openMANET_TopologyNodeInfo != null)
			{
				TopologyNodesListBox.ScrollIntoView(openMANET_TopologyNodeInfo);
			}
		}
		finally
		{
			_topologySelectionInProgress = false;
		}
	}

	private static bool TopologyNodeMatchesNode(OpenMANET_TopologyNodeInfo topologyNode, OpenMANET_NodeInfo node)
	{
		if (topologyNode == null || node == null)
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(node.MacAddress) && node.MacAddress != "--")
		{
			if (string.Equals(topologyNode.MacAddress, node.MacAddress, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			foreach (string secondaryMac in topologyNode.SecondaryMacs)
			{
				if (string.Equals(secondaryMac, node.MacAddress, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		return string.Equals(topologyNode.Hostname, node.Name, StringComparison.OrdinalIgnoreCase);
	}

	private void RenderTopology()
	{
		TopologyCanvas.Children.Clear();
		if (_lastTopology == null || _lastTopology.Nodes.Count == 0)
		{
			return;
		}
		double num = TopologyCanvas.ActualWidth;
		if (double.IsNaN(num) || num < 220.0)
		{
			num = 260.0;
		}
		SortedDictionary<int, List<OpenMANET_TopologyNodeInfo>> sortedDictionary = new SortedDictionary<int, List<OpenMANET_TopologyNodeInfo>>();
		int num2 = 0;
		foreach (OpenMANET_TopologyNodeInfo node in _lastTopology.Nodes)
		{
			if (!node.IsSelf && node.HopsFromSelf >= 0 && node.HopsFromSelf < 99)
			{
				num2 = Math.Max(num2, node.HopsFromSelf);
			}
		}
		foreach (OpenMANET_TopologyNodeInfo node2 in _lastTopology.Nodes)
		{
			int key = ((!node2.IsSelf) ? ((node2.HopsFromSelf >= 0 && node2.HopsFromSelf < 99) ? Math.Max(1, node2.HopsFromSelf) : (num2 + 1)) : 0);
			if (!sortedDictionary.TryGetValue(key, out var value))
			{
				value = (sortedDictionary[key] = new List<OpenMANET_TopologyNodeInfo>());
			}
			value.Add(node2);
		}
		int num3 = 0;
		foreach (int key2 in sortedDictionary.Keys)
		{
			num3 = Math.Max(num3, key2);
		}
		int num4 = 1;
		foreach (List<OpenMANET_TopologyNodeInfo> value3 in sortedDictionary.Values)
		{
			num4 = Math.Max(num4, value3.Count);
		}
		double num5 = Math.Max(82.0, Math.Min(112.0, (num - 28.0) / (double)num4 - 10.0));
		TopologyCanvas.Height = Math.Max(250.0, 100.0 + (double)num3 * 112.0 + 28.0);
		Dictionary<string, Point> positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<int, List<OpenMANET_TopologyNodeInfo>> item in sortedDictionary)
		{
			List<OpenMANET_TopologyNodeInfo> value2 = item.Value;
			double y = 42.0 + (double)item.Key * 112.0;
			for (int i = 0; i < value2.Count; i++)
			{
				OpenMANET_TopologyNodeInfo openMANET_TopologyNodeInfo = value2[i];
				double x;
				if (value2.Count == 1)
				{
					x = num / 2.0;
				}
				else
				{
					double num6 = num5 / 2.0 + 12.0;
					double num7 = (num - num5 / 2.0 - 12.0 - num6) / ((double)value2.Count - 1.0);
					x = num6 + num7 * (double)i;
				}
				Point position = new Point(x, y);
				AddTopologyPosition(positions, openMANET_TopologyNodeInfo.MacAddress, position);
				foreach (string secondaryMac in openMANET_TopologyNodeInfo.SecondaryMacs)
				{
					AddTopologyPosition(positions, secondaryMac, position);
				}
			}
		}
		foreach (OpenMANET_TopologyEdgeInfo edge in _lastTopology.Edges)
		{
			if (TryGetTopologyPosition(positions, edge.FromMac, out var position2) && TryGetTopologyPosition(positions, edge.ToMac, out var position3))
			{
				bool flag = Math.Abs(position3.Y - position2.Y) >= 1.0;
				double num8 = position2.X;
				double num9 = position2.Y;
				double num10 = position3.X;
				double num11 = position3.Y;
				if (flag)
				{
					double num12 = Math.Sign(position3.Y - position2.Y);
					num9 += num12 * 58.0 / 2.0;
					num11 -= num12 * 58.0 / 2.0;
				}
				else
				{
					double num13 = Math.Sign(position3.X - position2.X);
					num8 += num13 * num5 / 2.0;
					num10 -= num13 * num5 / 2.0;
				}
				Brush stroke;
				if (edge.IsOnMyPath)
				{
					stroke = new SolidColorBrush(Color.FromRgb(73, 209, 125));
				}
				else
				{
					stroke = (edge.IsBlos ? new SolidColorBrush(Color.FromRgb(225, 174, 78)) : new SolidColorBrush(Color.FromRgb(98, 107, 117)));
				}
				Line line = new Line();
				line.X1 = num8;
				line.Y1 = num9;
				line.X2 = num10;
				line.Y2 = num11;
				line.Stroke = stroke;
				line.StrokeThickness = (edge.IsOnMyPath ? 2.5 : 1.75);
				line.StrokeStartLineCap = PenLineCap.Round;
				line.StrokeEndLineCap = PenLineCap.Round;
				line.Opacity = (edge.IsOnMyPath ? 0.95 : 0.78);
				line.ToolTip = edge.LinkDisplay + "\r\n" + edge.TypeDisplay + " · Metric " + edge.MetricDisplay;
				Line line2 = line;
				if (edge.IsBlos)
				{
					line2.StrokeDashArray = new DoubleCollection { 5.0, 3.0 };
				}
				TopologyCanvas.Children.Add(line2);
			}
		}
		OpenMANET_TopologyNodeInfo openMANET_TopologyNodeInfo2 = TopologyNodesListBox.SelectedItem as OpenMANET_TopologyNodeInfo;
		foreach (OpenMANET_TopologyNodeInfo node3 in _lastTopology.Nodes)
		{
			if (TryGetTopologyPosition(positions, node3.MacAddress, out var position4))
			{
				bool isSelected = openMANET_TopologyNodeInfo2 != null && string.Equals(openMANET_TopologyNodeInfo2.MacAddress, node3.MacAddress, StringComparison.OrdinalIgnoreCase);
				Border element = CreateTopologyNodeCard(node3, isSelected, num5, 58.0);
				Canvas.SetLeft(element, position4.X - num5 / 2.0);
				Canvas.SetTop(element, position4.Y - 29.0);
				TopologyCanvas.Children.Add(element);
			}
		}
	}

	private static Border CreateTopologyNodeCard(OpenMANET_TopologyNodeInfo node, bool isSelected, double width, double height)
	{
		Brush brush;
		if (node.GossipStale)
		{
			brush = new SolidColorBrush(Color.FromRgb(121, 124, 130));
		}
		else if (node.IsSelf)
		{
			brush = new SolidColorBrush(Color.FromRgb(73, 209, 125));
		}
		else
		{
			brush = (node.IsGateway ? new SolidColorBrush(Color.FromRgb(67, 199, 183)) : new SolidColorBrush(Color.FromRgb(89, 97, 106)));
		}
		Brush borderBrush;
		if (isSelected)
		{
			borderBrush = new SolidColorBrush(Color.FromRgb(214, 166, 74));
		}
		else
		{
			borderBrush = (node.GossipStale ? new SolidColorBrush(Color.FromRgb(121, 124, 130)) : new SolidColorBrush(Color.FromRgb(64, 68, 73)));
		}
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(4.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		Border element = new Border
		{
			Width = 3.0,
			Background = brush,
			CornerRadius = new CornerRadius(2.0),
			Margin = new Thickness(0.0, 1.0, 0.0, 1.0)
		};
		Grid.SetRowSpan(element, 2);
		grid.Children.Add(element);
		Ellipse element2 = new Ellipse
		{
			Width = 7.0,
			Height = 7.0,
			Fill = brush,
			Margin = new Thickness(8.0, 2.0, 6.0, 0.0),
			VerticalAlignment = VerticalAlignment.Top
		};
		Grid.SetColumn(element2, 1);
		grid.Children.Add(element2);
		TextBlock element3 = new TextBlock
		{
			Text = node.NameDisplay,
			FontSize = 10.5,
			FontWeight = FontWeights.SemiBold,
			Foreground = Brushes.White,
			TextTrimming = TextTrimming.CharacterEllipsis,
			ToolTip = node.MacDisplay
		};
		Grid.SetColumn(element3, 2);
		grid.Children.Add(element3);
		TextBlock element4 = new TextBlock
		{
			Text = node.RoleDisplay + " · " + node.HopsDisplay,
			Margin = new Thickness(8.0, 5.0, 0.0, 0.0),
			FontSize = 9.0,
			Foreground = new SolidColorBrush(Color.FromRgb(177, 179, 183)),
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		Grid.SetRow(element4, 1);
		Grid.SetColumn(element4, 1);
		Grid.SetColumnSpan(element4, 2);
		grid.Children.Add(element4);
		Border border = new Border();
		border.Width = width;
		border.Height = height;
		border.Padding = new Thickness(8.0);
		border.Background = new SolidColorBrush(Color.FromRgb(35, 37, 40));
		border.BorderBrush = borderBrush;
		border.BorderThickness = new Thickness(isSelected ? 2.0 : 1.0);
		border.CornerRadius = new CornerRadius(8.0);
		border.Child = grid;
		border.ToolTip = node.NameDisplay + "\r\n" + node.RoleDisplay + " · " + node.HopsDisplay + "\r\n" + node.GossipDisplay;
		return border;
	}

	private static void AddTopologyPosition(Dictionary<string, Point> positions, string macAddress, Point position)
	{
		if (!string.IsNullOrWhiteSpace(macAddress))
		{
			positions[macAddress.Trim()] = position;
		}
	}

	private static bool TryGetTopologyPosition(Dictionary<string, Point> positions, string macAddress, out Point position)
	{
		if (!string.IsNullOrWhiteSpace(macAddress))
		{
			return positions.TryGetValue(macAddress.Trim(), out position);
		}
		position = default;
		return false;
	}

	private void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
	{
		ExportDiagnosticSnapshot(includeOperationalIdentifiers: false);
	}

	private void ExportFullDiagnosticsButton_Click(object sender, RoutedEventArgs e)
	{
		if (MessageBox.Show("A full snapshot contains operationally sensitive hostnames, IP and MAC addresses, topology, routes, software versions, service names, and process IDs. Review it before sharing.\r\n\r\nCreate the full snapshot?", "OpenMANET Operational Data Warning", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) == MessageBoxResult.Yes)
		{
			ExportDiagnosticSnapshot(includeOperationalIdentifiers: true);
		}
	}

	private void ExportDiagnosticSnapshot(bool includeOperationalIdentifiers)
	{
		try
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Title = (includeOperationalIdentifiers ? "Export Full OpenMANET Operational Snapshot" : "Export Redacted OpenMANET Support Snapshot"),
				Filter = "Text document (*.txt)|*.txt|All files (*.*)|*.*",
				DefaultExt = ".txt",
				AddExtension = true,
				FileName = (includeOperationalIdentifiers ? "OpenMANET_Operational_" : "OpenMANET_Support_Redacted_") + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt",
				InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
			};
			if (saveFileDialog.ShowDialog() == true)
			{
				File.WriteAllText(saveFileDialog.FileName, BuildDiagnosticSnapshot(includeOperationalIdentifiers), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				AddMeshEvent("INFO", includeOperationalIdentifiers ? "Full operational snapshot exported." : "Redacted support snapshot exported.");
				MessageBox.Show(includeOperationalIdentifiers ? "Full operational snapshot exported. Review it before sharing." : "Redacted support snapshot exported successfully.", "OpenMANET", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		}
		catch (Exception exception)
		{
			MessageBox.Show("Unable to export diagnostic snapshot.\r\n\r\n" + OpenMANET_Security.OperatorMessage(exception), "OpenMANET", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private string BuildDiagnosticSnapshot(bool includeOperationalIdentifiers)
	{
		StringBuilder stringBuilder = new StringBuilder();
		OpenMANET_DiagnosticRedactor openMANET_DiagnosticRedactor = new OpenMANET_DiagnosticRedactor();
		Version version = Assembly.GetExecutingAssembly().GetName().Version;
		if (!includeOperationalIdentifiers)
		{
			RegisterDiagnosticIdentifiers(openMANET_DiagnosticRedactor);
		}
		AppendDiagnosticSection(stringBuilder, includeOperationalIdentifiers ? "OPENMANET FULL OPERATIONAL SNAPSHOT" : "OPENMANET REDACTED SUPPORT SNAPSHOT");
		stringBuilder.AppendLine(includeOperationalIdentifiers ? "CLASSIFICATION: OPERATIONALLY SENSITIVE — REVIEW BEFORE SHARING" : "PRIVACY: NETWORK IDENTIFIERS PSEUDONYMIZED; CREDENTIALS EXCLUDED");
		stringBuilder.AppendLine("Generated: " + DateTimeOffset.Now.ToString("O"));
		stringBuilder.AppendLine("Plugin version: " + ((version == null) ? "Unknown" : version.ToString()));
		stringBuilder.AppendLine("Connection: " + (_isConnected ? "Connected" : "Not Connected"));
		stringBuilder.AppendLine("Gateway: " + (includeOperationalIdentifiers ? GatewayHostTextBox.Text.Trim() : openMANET_DiagnosticRedactor.Alias(GatewayHostTextBox.Text.Trim(), "Gateway")) + ":" + GatewayPortTextBox.Text.Trim());
		stringBuilder.AppendLine("Last poll: " + LastPollText.Text);
		stringBuilder.AppendLine("Credentials: intentionally excluded");
		AppendDiagnosticSection(stringBuilder, "MESH SUMMARY");
		stringBuilder.AppendLine("Health: " + HealthSummaryText.Text);
		stringBuilder.AppendLine("Active nodes: " + NodeCountText.Text);
		stringBuilder.AppendLine("Bandwidth: " + FormatNullableMetric(_currentBandwidthMbps, "Mbps"));
		stringBuilder.AppendLine("Throughput: " + FormatNullableMetric(_currentThroughputMbps, "Mbps"));
		stringBuilder.AppendLine("Estimated utilization: " + FormatNullableMetric(_currentUtilizationPercent, "%"));
		AppendDiagnosticSection(stringBuilder, "HEALTH FINDINGS");
		foreach (HealthFindingEntry healthFinding in _healthFindings)
		{
			stringBuilder.AppendLine("- " + DiagnosticText(healthFinding.Message, includeOperationalIdentifiers, openMANET_DiagnosticRedactor));
		}
		AppendDiagnosticSection(stringBuilder, "NODES");
		if (_lastNodes.Count == 0)
		{
			stringBuilder.AppendLine("No node data available.");
		}
		else
		{
			foreach (OpenMANET_NodeInfo lastNode in _lastNodes)
			{
				stringBuilder.AppendLine("- " + (includeOperationalIdentifiers ? DiagnosticText(lastNode.Name ?? "Unknown", includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : openMANET_DiagnosticRedactor.Alias(lastNode.Name ?? lastNode.MacAddress, "Node")) + " | Role=" + lastNode.RoleDisplay + " | Status=" + lastNode.StatusDisplay + " | Address=" + (includeOperationalIdentifiers ? DiagnosticText(lastNode.Address ?? "--", includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : "[redacted]") + " | MAC=" + (includeOperationalIdentifiers ? DiagnosticText(lastNode.MacAddress, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : "[redacted]") + " | Hops=" + lastNode.HopsDisplay + " | RSSI=" + lastNode.RssiDisplay + " | Link=" + lastNode.ThroughputDisplay + " | Route=" + (includeOperationalIdentifiers ? DiagnosticText(lastNode.RouteInterfaceDisplay, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : "[redacted]") + " | LastSeen=" + lastNode.LinkAgeDisplay);
			}
		}
		AppendDiagnosticSection(stringBuilder, "TOPOLOGY");
		if (_lastTopology == null)
		{
			stringBuilder.AppendLine("No topology data available.");
		}
		else
		{
			stringBuilder.AppendLine("Health: " + _lastTopology.HealthDisplay);
			stringBuilder.AppendLine("Algorithm: " + _lastTopology.AlgorithmDisplay);
			stringBuilder.AppendLine("Gossip coverage: " + _lastTopology.GossipCoverageDisplay);
			stringBuilder.AppendLine("Direct links: " + _lastTopology.DirectLinkCount.ToString(CultureInfo.InvariantCulture));
			stringBuilder.AppendLine("BLOS links: " + _lastTopology.BlosLinkCount.ToString(CultureInfo.InvariantCulture));
			foreach (OpenMANET_TopologyEdgeInfo edge in _lastTopology.Edges)
			{
				stringBuilder.AppendLine("- " + (includeOperationalIdentifiers ? DiagnosticText(edge.LinkDisplay, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : (openMANET_DiagnosticRedactor.Alias(edge.FromName ?? edge.FromMac, "Node") + " -> " + openMANET_DiagnosticRedactor.Alias(edge.ToName ?? edge.ToMac, "Node"))) + " | " + edge.TypeDisplay + " | Metric=" + edge.MetricDisplay + " | " + edge.PathDisplay);
			}
		}
		AppendDiagnosticSection(stringBuilder, "GATEWAY SYSTEM");
		OpenMANET_SystemInfo lastSystemDetails = _client.LastSystemDetails;
		if (lastSystemDetails == null)
		{
			stringBuilder.AppendLine("No system data available.");
		}
		else
		{
			stringBuilder.AppendLine("Hostname: " + (includeOperationalIdentifiers ? DiagnosticText(lastSystemDetails.HostnameDisplay, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : openMANET_DiagnosticRedactor.Alias(lastSystemDetails.HostnameDisplay, "Gateway")));
			stringBuilder.AppendLine("Model: " + lastSystemDetails.ModelDisplay);
			stringBuilder.AppendLine("Firmware: " + lastSystemDetails.FirmwareDisplay);
			stringBuilder.AppendLine("Kernel: " + (includeOperationalIdentifiers ? lastSystemDetails.KernelDisplay : "Omitted from support snapshot"));
			stringBuilder.AppendLine("Architecture: " + lastSystemDetails.ArchitectureDisplay);
			stringBuilder.AppendLine("Uptime: " + lastSystemDetails.UptimeDisplay);
			stringBuilder.AppendLine("CPU load: " + lastSystemDetails.CpuLoadDisplay);
			stringBuilder.AppendLine("Memory: " + lastSystemDetails.MemoryDisplay);
			stringBuilder.AppendLine("Storage: " + lastSystemDetails.OverlayDisplay);
			stringBuilder.AppendLine("Temperature: " + lastSystemDetails.TemperatureDisplay);
			stringBuilder.AppendLine("Interfaces:");
			foreach (OpenMANET_NetworkSummaryInfo networkEntry in lastSystemDetails.NetworkEntries)
			{
				stringBuilder.AppendLine("- " + (includeOperationalIdentifiers ? DiagnosticText(networkEntry.NameDisplay, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : openMANET_DiagnosticRedactor.Alias(networkEntry.NameDisplay, "Interface")) + (includeOperationalIdentifiers ? (" (" + DiagnosticText(networkEntry.InterfaceDisplay, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) + ")") : string.Empty) + " | " + networkEntry.StateDisplay + " | " + networkEntry.TrafficDisplay);
			}
			if (includeOperationalIdentifiers)
			{
				stringBuilder.AppendLine("Services:");
				foreach (OpenMANET_ServiceInfo service in lastSystemDetails.Services)
				{
					stringBuilder.AppendLine("- " + service.NameDisplay + " | " + service.StatusDisplay + " | " + service.ProcessDisplay);
				}
			}
			else
			{
				stringBuilder.AppendLine("Services: Omitted from support snapshot");
			}
		}
		AppendDiagnosticSection(stringBuilder, "RECENT MESH EVENTS");
		if (_meshEvents.Count == 0)
		{
			stringBuilder.AppendLine("No mesh events recorded.");
		}
		else
		{
			for (int num = _meshEvents.Count - 1; num >= 0; num--)
			{
				MeshEventEntry meshEventEntry = _meshEvents[num];
				stringBuilder.AppendLine(meshEventEntry.CapturedAt.ToString("O") + " | " + meshEventEntry.SeverityDisplay + " | " + DiagnosticText(meshEventEntry.Message, includeOperationalIdentifiers, openMANET_DiagnosticRedactor));
			}
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("END OF SNAPSHOT");
		return stringBuilder.ToString();
	}

	private void RegisterDiagnosticIdentifiers(OpenMANET_DiagnosticRedactor redactor)
	{
		redactor.Alias(GatewayHostTextBox.Text.Trim(), "Gateway");
		foreach (OpenMANET_NodeInfo lastNode in _lastNodes)
		{
			redactor.Alias(lastNode.Name, "Node");
			redactor.Alias(lastNode.Address, "Node");
			redactor.Alias(lastNode.MacAddress, "Node");
			redactor.Alias(lastNode.RemoteGatewayMac, "Node");
			redactor.Alias(lastNode.LinkHardwareAddress, "Node");
			redactor.Alias(lastNode.RouteInterfaceDisplay, "Interface");
		}
		if (_lastTopology != null)
		{
			foreach (OpenMANET_TopologyNodeInfo node in _lastTopology.Nodes)
			{
				redactor.Alias(node.Hostname, "Node");
				redactor.Alias(node.MacAddress, "Node");
				redactor.Alias(node.RemoteGatewayMac, "Node");
				foreach (string secondaryMac in node.SecondaryMacs)
				{
					redactor.Alias(secondaryMac, "Node");
				}
			}
		}
		OpenMANET_SystemInfo lastSystemDetails = _client.LastSystemDetails;
		if (lastSystemDetails == null)
		{
			return;
		}
		redactor.Alias(lastSystemDetails.HostnameDisplay, "Gateway");
		foreach (OpenMANET_NetworkSummaryInfo networkEntry in lastSystemDetails.NetworkEntries)
		{
			redactor.Alias(networkEntry.NameDisplay, "Interface");
			redactor.Alias(networkEntry.InterfaceDisplay, "Interface");
		}
	}

	private static string DiagnosticText(string value, bool includeOperationalIdentifiers, OpenMANET_DiagnosticRedactor redactor)
	{
		if (!includeOperationalIdentifiers)
		{
			return redactor.RedactText(value);
		}
		return OpenMANET_Security.NormalizeDetail(value) ?? string.Empty;
	}

	private static void AppendDiagnosticSection(StringBuilder output, string title)
	{
		output.AppendLine();
		output.AppendLine(title);
		output.AppendLine(new string('-', title.Length));
	}

	private static string FormatNullableMetric(double? value, string unit)
	{
		if (!value.HasValue)
		{
			return "Unavailable";
		}
		return value.Value.ToString("0.00", CultureInfo.InvariantCulture) + " " + unit;
	}

	private void UpdateConnectionState(bool connected)
	{
		_isConnected = connected;
		bool flag = connected && AllowInsecureHttpCheckBox.IsChecked == true;
		ConnectButton.Content = (connected ? "Disconnect" : "Connect");
		RefreshButton.IsEnabled = connected;
		ConnectionSettingsPanel.IsEnabled = !connected;
		ConnectionSettingsPanel.Opacity = (connected ? 0.5 : 1.0);
		TextBlock connectionStatusText = ConnectionStatusText;
		string text;
		if (flag)
		{
			text = "Connected — INSECURE HTTP";
		}
		else
		{
			text = (connected ? "Connected" : "Not Connected");
		}
		connectionStatusText.Text = text;
		Ellipse connectionStatusIndicator = ConnectionStatusIndicator;
		SolidColorBrush fill;
		if (flag)
		{
			fill = Brushes.Goldenrod;
		}
		else
		{
			fill = (connected ? Brushes.LimeGreen : Brushes.Red);
		}
		connectionStatusIndicator.Fill = fill;
	}

	private void ResetConnection()
	{
		bool isConnected = _isConnected;
		_autoRefreshTimer.Stop();
		_throughputTimer.Stop();
		CancelOutstandingOperations();
		_client.Disconnect();
		PasswordInput.Password = string.Empty;
		_lastNodes.Clear();
		_previousNodeStates.Clear();
		_nodeTrendHistory.Clear();
		_nodeBaselineEstablished = false;
		_refreshFailureActive = false;
		_previousTopologyHealth = null;
		_previousSystemState = null;
		_meshInterfaceAvailable = null;
		_bandwidthIsLow = null;
		_utilizationState = null;
		_currentUtilizationPercent = null;
		NodesListBox.ItemsSource = null;
		NodesListBox.SelectedItem = null;
		LinkQualityNodeComboBox.ItemsSource = null;
		LinkQualityNodeComboBox.SelectedItem = null;
		GatewayStatusText.Text = "Not Connected";
		NodeCountText.Text = "0";
		LastPollText.Text = "Never";
		ClearNodeDetails();
		ClearTopology();
		ClearSystemDetails();
		ClearBandwidth("Connect to begin sampling");
		ClearThroughput("Connect to begin sampling");
		UpdateUtilization();
		ClearNodeTrends();
		MainTabControl.SelectedIndex = 0;
		UpdateConnectionState(connected: false);
		UpdateHealthFindings();
		if (isConnected)
		{
			AddMeshEvent("INFO", "Disconnected from the OpenMANET gateway.");
		}
	}

	private async Task ResetConnectionWithLogoutAsync()
	{
		bool wasConnected = _isConnected;
		_autoRefreshTimer.Stop();
		_throughputTimer.Stop();
		CancelOutstandingOperations();
		bool flag = await _client.LogoutAsync(CancellationToken.None);
		ResetConnection();
		if (wasConnected && !flag)
		{
			AddMeshEvent("WARNING", "Disconnected locally, but OpenMANET did not confirm server-session revocation. The token may remain valid until server expiry.");
		}
	}

	private void HandleExpiredSession()
	{
		if (_isConnected)
		{
			ResetConnection();
			AddMeshEvent("WARNING", "OpenMANET authentication expired or was rejected. Reconnect to continue monitoring.");
			MainTabControl.SelectedIndex = 4;
			ConnectionStatusText.Text = "Authentication expired - reconnect";
			ConnectionStatusIndicator.Fill = Brushes.Goldenrod;
		}
	}

	private async void OpenMANET_Unloaded(object sender, RoutedEventArgs e)
	{
		await ResetConnectionWithLogoutAsync();
	}

	private CancellationToken GetOperationToken()
	{
		if (_operationCancellation == null)
		{
			return CancellationToken.None;
		}
		return _operationCancellation.Token;
	}

	private void CancelOutstandingOperations()
	{
		if (_operationCancellation != null)
		{
			_operationCancellation.Cancel();
			_operationCancellation.Dispose();
			_operationCancellation = null;
		}
	}

	private void LoadConnectionSettings()
	{
		GatewayHostTextBox.Text = "10.41.0.1";
		GatewayPortTextBox.Text = "8081";
		UsernameTextBox.Text = string.Empty;
		AllowInsecureHttpCheckBox.IsChecked = false;
		if (!File.Exists(_settingsFilePath))
		{
			return;
		}
		try
		{
			if (new FileInfo(_settingsFilePath).Length > 4096)
			{
				return;
			}
			string[] array = File.ReadAllLines(_settingsFilePath);
			if (array.Length <= 3 && array.Length >= 2 && int.TryParse(array[1], out var result) && result >= 1 && result <= 65535)
			{
				if (array.Length != 0 && !string.IsNullOrWhiteSpace(array[0]))
				{
					string text = array[0].Trim();
					int num = ((result == int.Parse("8087", CultureInfo.InvariantCulture)) ? int.Parse("8081", CultureInfo.InvariantCulture) : result);
					OpenMANET_Security.BuildEndpoint(text, num, allowInsecureHttp: false);
					GatewayHostTextBox.Text = text;
					result = num;
				}
				GatewayPortTextBox.Text = result.ToString(CultureInfo.InvariantCulture);
				if (array.Length > 2 && !string.IsNullOrWhiteSpace(array[2]))
				{
					UsernameTextBox.Text = OpenMANET_Security.ValidateUsername(array[2]);
				}
			}
		}
		catch
		{
		}
	}

	private void SaveConnectionSettings()
	{
		string directoryName = System.IO.Path.GetDirectoryName(_settingsFilePath);
		if (string.IsNullOrWhiteSpace(directoryName))
		{
			throw new InvalidOperationException("Unable to determine the settings directory.");
		}
		Directory.CreateDirectory(directoryName);
		if (!int.TryParse(GatewayPortTextBox.Text, out var result))
		{
			throw new InvalidOperationException("Port must be a valid number.");
		}
		Uri uri = OpenMANET_Security.BuildEndpoint(GatewayHostTextBox.Text, result, AllowInsecureHttpCheckBox.IsChecked == true);
		string text = OpenMANET_Security.ValidateUsername(UsernameTextBox.Text);
		File.WriteAllLines(_settingsFilePath, new string[3]
		{
			uri.Host,
			GatewayPortTextBox.Text.Trim(),
			text
		});
	}
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

		public bool IsDiscovering { get; set; }

		public bool IsRouted { get; set; }

		public int? HopsFromSelf { get; set; }

		public string RouteInterface { get; set; }

		public int? Rssi { get; set; }

		public string RemoteGatewayMac { get; set; }

		public bool RssiPoor { get; set; }

		public static NodeStateSnapshot FromNode(OpenMANET_NodeInfo node, NodeStateSnapshot previous)
		{
			int? rssi = node.CurrentRssi ?? node.Rssi;
			bool wasPoor = previous?.RssiPoor ?? false;
			return new NodeStateSnapshot
			{
				Name = node.DisplayName,
				IsOnline = node.IsOnline,
				IsDiscovering = node.IsDiscovering,
				IsRouted = node.IsRouted,
				HopsFromSelf = node.HopsFromSelf,
				RouteInterface = node.RouteInterfaceDisplay,
				Rssi = rssi,
				RemoteGatewayMac = node.RemoteGatewayMac,
				// A missing reading keeps the last state instead of counting as a recovery.
				RssiPoor = (rssi.HasValue ? IsBelowWithHysteresis(wasPoor, rssi.Value, PoorRssiThresholdDbm, RssiHysteresisDb) : wasPoor)
			};
		}
	}

	private sealed class SystemStateSnapshot
	{
		public bool HighCpu { get; set; }

		public bool HighMemory { get; set; }

		public bool HighStorage { get; set; }

		public bool HighTemperature { get; set; }

		public static SystemStateSnapshot FromSystem(OpenMANET_SystemInfo system, SystemStateSnapshot previous)
		{
			return new SystemStateSnapshot
			{
				HighCpu = IsAboveWithHysteresis(previous?.HighCpu ?? false, system.CpuLoadValue, HighResourcePercent, ResourceHysteresisPercent),
				HighMemory = IsAboveWithHysteresis(previous?.HighMemory ?? false, system.MemoryUsagePercent, HighResourcePercent, ResourceHysteresisPercent),
				HighStorage = IsAboveWithHysteresis(previous?.HighStorage ?? false, system.OverlayUsagePercent, HighResourcePercent, ResourceHysteresisPercent),
				HighTemperature = IsAboveWithHysteresis(previous?.HighTemperature ?? false, system.CpuTemperatureCelsius, HighTemperatureCelsius, TemperatureHysteresisCelsius)
			};
		}
	}

	private sealed class NodeTimelineEntry
	{
		public DateTimeOffset CapturedAt { get; set; }

		// What changed is compared on this; the detail adds readings that move every poll.
		public string State { get; set; }

		public string Detail { get; set; }

		public static NodeTimelineEntry FromNode(OpenMANET_NodeInfo node, DateTimeOffset now)
		{
			string state = node.StatusDisplay + " | routed " + (node.IsRouted ? "yes" : "no") + " | RF link " + (node.HasLiveLink ? "yes" : "no") + " | neighbor table " + (node.HasDirectLinkMetrics ? "yes" : "no") + " | hops " + node.HopsDisplay;
			string detail = "last heard " + node.LinkAgeDisplay + " | RSSI " + node.RssiDisplay + " | gossip age " + (node.GossipAgeSeconds.HasValue ? (node.GossipAgeSeconds.Value.ToString(CultureInfo.InvariantCulture) + " s") : "--");
			return new NodeTimelineEntry { CapturedAt = now, State = state, Detail = detail };
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

	private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(15.0);

	private static readonly TimeSpan ThroughputSampleInterval = TimeSpan.FromSeconds(5.0);

	private static readonly TimeSpan ThroughputWindow = TimeSpan.FromSeconds(60.0);

	private static readonly TimeSpan BandwidthWindow = TimeSpan.FromSeconds(60.0);

	private static readonly TimeSpan NodeTrendWindow = TimeSpan.FromMinutes(60.0);

	private const int MaximumEventCount = 100;

	private const double LowBandwidthThresholdMbps = 5.0;

	private const double WarningUtilizationPercent = 60.0;

	private const double CriticalUtilizationPercent = 80.0;

	private const int PoorRssiThresholdDbm = -75;

	// Code-behind status colors, matching the Success/Warning/Danger brushes in OpenMANET.xaml.
	private static readonly SolidColorBrush SuccessIndicatorBrush = CreateFrozenBrush(0x3D, 0xD6, 0x8C);

	private static readonly SolidColorBrush WarningIndicatorBrush = CreateFrozenBrush(0xF2, 0xB5, 0x4A);

	private static readonly SolidColorBrush DangerIndicatorBrush = CreateFrozenBrush(0xF2, 0x6D, 0x6D);

	// Hysteresis bands: a warning starts at its threshold but only clears once the value is
	// back past threshold +/- band, so readings hovering near a threshold at the 5-15 s poll
	// rate don't produce a degraded/recovered event pair every sample.
	private const double RssiHysteresisDb = 3.0;

	private const double BandwidthHysteresisMbps = 1.0;

	private const double UtilizationHysteresisPercent = 5.0;

	private const double HighResourcePercent = 90.0;

	private const double ResourceHysteresisPercent = 5.0;

	private const double HighTemperatureCelsius = 80.0;

	private const double TemperatureHysteresisCelsius = 5.0;

	private const double BitsPerByte = 8.0;

	private const double BitsPerMegabit = 1000000.0;

	private const double MinimumThroughputChartScale = 0.4;

	private const double MinimumBandwidthChartScale = 0.4;

	private readonly OpenMANET_Client _client;

	private readonly string _settingsFilePath;

	private readonly OpenMANET_CallsignStore _callsigns;

	private readonly OpenMANET_Preferences _preferences;

	private readonly OpenMANET_MapLayer _mapLayer = new OpenMANET_MapLayer();

	private readonly OpenMANET_CertificatePinStore _certificatePins;

	// Password for automatic reconnect, encrypted with DPAPI for the current Windows user and held
	// only in memory while connected with auto-reconnect on. Never written to disk.
	private byte[] _protectedPassword;

	private CancellationTokenSource _reconnectCancellation;

	private bool _reconnecting;

	private DateTimeOffset _lastSuccessfulRefreshUtc;

	// Set once polling has failed for UnreachableAfter, so stale data is not shown as current.
	private bool _nodesMarkedUnreachable;

	// Another node whose GPS WinTAK's position has matched for consecutive polls, and how many.
	private string _borrowedGpsNodeKey;

	private int _borrowedGpsPolls;

	private OpenMANET_NodeInfo _borrowedGpsSource;

	private bool _poorGpsLogged;

	private static readonly TimeSpan UnreachableAfter = TimeSpan.FromSeconds(45.0);

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

	private readonly Dictionary<string, string> _endpointFailures = new Dictionary<string, string>(StringComparer.Ordinal);

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

	private DateTimeOffset _lastCallsignShareUtc;

	// Per node, each change in how it is seen, for diagnosing slow cold boots.
	private readonly Dictionary<string, List<NodeTimelineEntry>> _nodeTimelines = new Dictionary<string, List<NodeTimelineEntry>>(StringComparer.OrdinalIgnoreCase);

	private const int MaximumTimelineEntries = 40;

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
			// Closing the dock pane only hides its tool window and the same control comes back
			// on reopen, so monitoring keeps running until WinTAK itself shuts down.
			Dispatcher.ShutdownStarted += Dispatcher_ShutdownStarted;
			string path = OpenMANET_PluginHost.SettingsDirectory;
			_settingsFilePath = System.IO.Path.Combine(path, "connection.settings");
			_callsigns = new OpenMANET_CallsignStore(System.IO.Path.Combine(path, "callsigns.settings"));
			_callsigns.Load();
			_preferences = OpenMANET_PluginHost.Preferences;
			_certificatePins = new OpenMANET_CertificatePinStore(System.IO.Path.Combine(path, "trusted-certificates.settings"));
			_certificatePins.Load();
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
			SaveCallsignButton.Click += SaveCallsignButton_Click;
			ClearCallsignButton.Click += ClearCallsignButton_Click;
			CallsignTextBox.KeyDown += CallsignTextBox_KeyDown;
			ExportCallsignsButton.Click += ExportCallsignsButton_Click;
			ImportCallsignsButton.Click += ImportCallsignsButton_Click;
			MergeDistanceComboBox.ItemsSource = OpenMANET_Preferences.MergeDistanceChoices;
			LoadPreferenceControls();
			ForgetCertificateButton.Click += ForgetCertificateButton_Click;
			GatewayHostTextBox.TextChanged += (sender, e) => UpdateCertificatePinText();
			GatewayPortTextBox.TextChanged += (sender, e) => UpdateCertificatePinText();
			UpdateCertificatePinText();
			foreach (CheckBox preferenceCheckBox in new[] { NotifyNodesCheckBox, NotifyLinksCheckBox, NotifyGatewayCheckBox, NotifyConnectionCheckBox, AutoReconnectCheckBox, ShowOnMapCheckBox, HideNodeOwnMarkersCheckBox, ShowUnattendedNodesCheckBox, ShareCallsignsCheckBox, UseSharedCallsignsCheckBox })
			{
				preferenceCheckBox.Checked += PreferenceCheckBox_Changed;
				preferenceCheckBox.Unchecked += PreferenceCheckBox_Changed;
			}
			MergeDistanceComboBox.SelectionChanged += MergeDistanceComboBox_SelectionChanged;
			ForgetSharedCallsignsButton.Click += ForgetSharedCallsignsButton_Click;
			OpenMANET_PluginHost.CallsignSharing.Received += CallsignSharing_Received;
			OpenMANET_PluginHost.CallsignsRequested += PluginHost_CallsignsRequested;
			if (_preferences.UseSharedCallsigns)
			{
				// Names from operators already sharing arrive in seconds instead of at their next rebroadcast.
				OpenMANET_PluginHost.RequestSharedCallsigns();
			}
			OpenMANET_PluginHost.NodeFocusRequested += PluginHost_NodeFocusRequested;
			UpdateCallsignSharingText();
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
		if (_reconnecting)
		{
			StopReconnect("Automatic reconnect stopped.");
			return;
		}
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
			ConnectButton.IsEnabled = false;
			ConnectionStatusText.Text = "Connecting...";
			ConnectionStatusIndicator.Fill = WarningIndicatorBrush;
			try
			{
				await ConnectCoreAsync(port, password, CancellationToken.None);
			}
			catch (OpenMANET_CertificateChangedException changed) when (ConfirmCertificateChange(changed, port))
			{
				await ConnectCoreAsync(port, password, CancellationToken.None);
			}
			RememberPasswordForReconnect(password);
			await FinishConnectAsync();
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

	/// <summary>Logs in with the gateway settings in the form and switches the pane to connected.</summary>
	private async Task ConnectCoreAsync(int port, string password, CancellationToken cancellationToken)
	{
		CancelOutstandingOperations();
		_operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		string host = GatewayHostTextBox.Text.Trim();
		bool secure = AllowInsecureHttpCheckBox.IsChecked != true;
		_client.ExpectedCertificateThumbprint = secure ? _certificatePins.Find(host, port) : null;
		await _client.LoginAsync(host, port, UsernameTextBox.Text.Trim(), password, !secure, _operationCancellation.Token);
		// Trust on first use: remember this node's certificate so a later change is caught.
		if (secure && _client.ExpectedCertificateThumbprint == null && _client.ServerCertificateThumbprint != null)
		{
			_certificatePins.Set(host, port, _client.ServerCertificateThumbprint);
			AddMeshEvent("INFO", "Pinned the HTTPS certificate for " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + " (SHA-256 " + OpenMANET_CertificatePinStore.Format(_client.ServerCertificateThumbprint) + ").");
		}
		UpdateCertificatePinText();
		// Start the unreachable clock at login, so a session that never completes a poll still ages out.
		_lastSuccessfulRefreshUtc = DateTimeOffset.UtcNow;
		SaveConnectionSettings();
		GatewayStatusText.Text = $"{GatewayHostTextBox.Text.Trim()}:{port}";
		UpdateConnectionState(connected: true);
		AddMeshEvent((AllowInsecureHttpCheckBox.IsChecked == true) ? "WARNING" : "INFO", "Connected to OpenMANET gateway " + GatewayHostTextBox.Text.Trim() + ":" + port + ((AllowInsecureHttpCheckBox.IsChecked == true) ? " using INSECURE HTTP. Credentials, the bearer token, and telemetry are not encrypted." : " using trusted HTTPS."));
	}

	/// <summary>
	/// Asks whether to trust a node certificate that no longer matches its pin. On yes, re-pins it
	/// so the caller can retry; on no, the connection stays blocked.
	/// </summary>
	private bool ConfirmCertificateChange(OpenMANET_CertificateChangedException changed, int port)
	{
		string host = GatewayHostTextBox.Text.Trim();
		AddMeshEvent("CRITICAL", "The HTTPS certificate for " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + " changed since it was first trusted. Connection blocked.", OpenMANET_EventCategory.Connection);
		string message = "The HTTPS certificate presented by " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + " does not match the one you trusted before.\n\n" +
			"Trusted:  " + OpenMANET_CertificatePinStore.Format(changed.ExpectedThumbprint) + "\n" +
			"Received: " + OpenMANET_CertificatePinStore.Format(changed.ActualThumbprint) + "\n\n" +
			"If you reissued this node's certificate or reflashed it, this is expected. If not, someone may be intercepting the connection; choose No.\n\n" +
			"Trust the new certificate and connect?";
		if (MessageBox.Show(message, "OpenMANET Certificate Changed", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return false;
		}
		_certificatePins.Set(host, port, changed.ActualThumbprint);
		AddMeshEvent("WARNING", "Trusted the new HTTPS certificate for " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + " (SHA-256 " + OpenMANET_CertificatePinStore.Format(changed.ActualThumbprint) + ").");
		return true;
	}

	private void UpdateCertificatePinText()
	{
		string pin = int.TryParse(GatewayPortTextBox.Text, out int port) ? _certificatePins.Find(GatewayHostTextBox.Text.Trim(), port) : null;
		CertificatePinText.Text = (pin == null) ? "Certificate: not pinned yet. The node's certificate is pinned on the next HTTPS connect." : ("Pinned certificate SHA-256: " + OpenMANET_CertificatePinStore.Format(pin));
		ForgetCertificateButton.IsEnabled = pin != null;
	}

	private void ForgetCertificateButton_Click(object sender, RoutedEventArgs e)
	{
		if (!int.TryParse(GatewayPortTextBox.Text, out int port))
		{
			return;
		}
		string host = GatewayHostTextBox.Text.Trim();
		if (MessageBox.Show("Forget the pinned certificate for " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + "? The next connect will trust whatever certificate Windows accepts and pin it again.", "OpenMANET", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			if (_certificatePins.Remove(host, port))
			{
				AddMeshEvent("INFO", "Forgot the pinned HTTPS certificate for " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + ".");
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			MessageBox.Show(ex.Message, "OpenMANET", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		UpdateCertificatePinText();
	}

	/// <summary>Starts polling and takes the first samples after a successful login.</summary>
	private async Task FinishConnectAsync()
	{
		_autoRefreshTimer.Start();
		await RefreshAsync();
		ClearBandwidth("Waiting for link estimate...");
		await SampleBandwidthAsync();
		ClearThroughput("Collecting baseline...");
		await SampleThroughputAsync();
		_throughputTimer.Start();
	}

	private static readonly TimeSpan[] ReconnectDelays =
	{
		TimeSpan.FromSeconds(2.0),
		TimeSpan.FromSeconds(5.0),
		TimeSpan.FromSeconds(10.0),
		TimeSpan.FromSeconds(20.0),
		TimeSpan.FromSeconds(30.0),
		TimeSpan.FromSeconds(60.0)
	};

	private static readonly byte[] PasswordEntropy = Encoding.UTF8.GetBytes("WinTAK_OpenMANET_Plugin.reconnect");

	private void RememberPasswordForReconnect(string password)
	{
		ClearReconnectPassword();
		if (!_preferences.AutoReconnect || string.IsNullOrEmpty(password))
		{
			return;
		}
		byte[] plain = Encoding.UTF8.GetBytes(password);
		try
		{
			_protectedPassword = ProtectedData.Protect(plain, PasswordEntropy, DataProtectionScope.CurrentUser);
		}
		catch (CryptographicException)
		{
			_protectedPassword = null;
		}
		finally
		{
			Array.Clear(plain, 0, plain.Length);
		}
	}

	private string RecallReconnectPassword()
	{
		if (_protectedPassword == null)
		{
			return null;
		}
		byte[] plain = ProtectedData.Unprotect(_protectedPassword, PasswordEntropy, DataProtectionScope.CurrentUser);
		try
		{
			return Encoding.UTF8.GetString(plain);
		}
		finally
		{
			Array.Clear(plain, 0, plain.Length);
		}
	}

	private void ClearReconnectPassword()
	{
		if (_protectedPassword != null)
		{
			Array.Clear(_protectedPassword, 0, _protectedPassword.Length);
			_protectedPassword = null;
		}
	}

	/// <summary>
	/// Logs back in after the node rejects the session (it rebooted, or the token expired),
	/// backing off between attempts while the node is unreachable. A rejected password stops it.
	/// </summary>
	private async void StartReconnect()
	{
		if (_reconnecting || !int.TryParse(GatewayPortTextBox.Text, out int port))
		{
			return;
		}
		_reconnecting = true;
		_reconnectCancellation = new CancellationTokenSource();
		CancellationToken token = _reconnectCancellation.Token;
		UpdateConnectionState(connected: false);
		UpdateHealthFindings();
		int attempt = 0;
		while (!token.IsCancellationRequested)
		{
			attempt++;
			ConnectionStatusText.Text = "Reconnecting (attempt " + attempt.ToString(CultureInfo.InvariantCulture) + ")...";
			ConnectionStatusIndicator.Fill = WarningIndicatorBrush;
			try
			{
				string password = RecallReconnectPassword();
				if (password == null)
				{
					StopReconnect("Automatic reconnect stopped: no saved credentials.");
					return;
				}
				await ConnectCoreAsync(port, password, token);
				_reconnecting = false;
				_reconnectCancellation.Dispose();
				_reconnectCancellation = null;
				UpdateConnectionState(connected: true);
				AddMeshEvent("INFO", "Reconnected to the OpenMANET gateway after " + attempt.ToString(CultureInfo.InvariantCulture) + ((attempt == 1) ? " attempt." : " attempts."), OpenMANET_EventCategory.Connection);
				await FinishConnectAsync();
				return;
			}
			catch (OpenMANET_AuthenticationException)
			{
				_client.Disconnect();
				StopReconnect("Automatic reconnect stopped: OpenMANET rejected the saved credentials. Sign in again from Settings.");
				return;
			}
			catch (OpenMANET_CertificateChangedException)
			{
				// Never accept a changed certificate without the operator seeing it.
				_client.Disconnect();
				StopReconnect("Automatic reconnect stopped: the node's HTTPS certificate changed. Connect from Settings to review it.");
				return;
			}
			catch (Exception) when (token.IsCancellationRequested)
			{
				return;
			}
			catch (Exception)
			{
				_client.Disconnect();
			}
			try
			{
				await Task.Delay(ReconnectDelays[Math.Min(attempt - 1, ReconnectDelays.Length - 1)], token);
			}
			catch (TaskCanceledException)
			{
				return;
			}
		}
	}

	private void StopReconnect(string reason)
	{
		bool wasReconnecting = _reconnecting;
		_reconnecting = false;
		if (_reconnectCancellation != null)
		{
			_reconnectCancellation.Cancel();
			_reconnectCancellation.Dispose();
			_reconnectCancellation = null;
		}
		ClearReconnectPassword();
		UpdateConnectionState(connected: false);
		UpdateHealthFindings();
		if (wasReconnecting && !string.IsNullOrWhiteSpace(reason))
		{
			AddMeshEvent("WARNING", reason, OpenMANET_EventCategory.Connection);
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
		TransportModeText.Text = (valueOrDefault ? "Legacy mode: direct HTTP API. Credentials and telemetry are not encrypted." : "Secure mode: HTTPS frontend with the /auth and /rpc proxies. The certificate must be trusted by Windows or WinTAK, and is pinned on first connect.");
	}

	private async void RefreshButton_Click(object sender, RoutedEventArgs e)
	{
		await RefreshAsync(showProgress: true);
	}

	private async void AutoRefreshTimer_Tick(object sender, EventArgs e)
	{
		// Rebroadcast now and then so operators who join later get the names too.
		if (_preferences.ShareCallsigns && DateTimeOffset.UtcNow - _lastCallsignShareUtc >= OpenMANET_CallsignSharing.RebroadcastInterval)
		{
			ShareCallsigns(announce: false);
		}
		await RefreshAsync();
	}

	private async void ThroughputTimer_Tick(object sender, EventArgs e)
	{
		await SampleBandwidthAsync();
		await SampleThroughputAsync();
	}

	private async Task RefreshAsync(bool showProgress = false)
	{
		if (!_isConnected || _refreshInProgress)
		{
			return;
		}
		_refreshInProgress = true;
		try
		{
			// Background polls run every few seconds; only a manual refresh shows progress
			// so the button doesn't flicker.
			if (showProgress)
			{
				RefreshButton.IsEnabled = false;
				RefreshButton.Content = "Refreshing...";
			}
			List<OpenMANET_NodeInfo> list = await _client.ListNodesAsync(GetOperationToken());
			ApplyCallsigns(list, _client.LastTopology);
			UpdateEndpointFailures(_client.LastRefreshFailures);
			UpdateTopology(_client.LastTopology);
			UpdateSystemDetails(_client.LastSystemDetails);
			UpdateNodeEvents(list);
			RecordNodeTimelines(list);
			UpdateOperationalEvents(_client.LastTopology, _client.LastSystemDetails);
			CaptureNodeTrendSamples(list);
			_lastNodes = list;
			UpdateGpsChecks(list);
			_lastSuccessfulRefreshUtc = DateTimeOffset.UtcNow;
			if (_nodesMarkedUnreachable)
			{
				_nodesMarkedUnreachable = false;
				AddMeshEvent("INFO", "Contact with the OpenMANET gateway restored.", OpenMANET_EventCategory.Connection);
			}
			UpdateMapLayer();
			if (_refreshFailureActive)
			{
				AddMeshEvent("INFO", "OpenMANET polling recovered.", OpenMANET_EventCategory.Connection);
				_refreshFailureActive = false;
			}
			UpdateConnectionStatusIndicator();
			OpenMANET_NodeInfo openMANET_NodeInfo = FindSelectedNode(list);
			NodesListBox.ItemsSource = list;
			if (openMANET_NodeInfo != null)
			{
				NodesListBox.SelectedItem = openMANET_NodeInfo;
			}
			else if (list.Count > 0)
			{
				// Default to a remote node; the local node is listed first but rarely the one of interest.
				NodesListBox.SelectedItem = list.Find((OpenMANET_NodeInfo node) => !node.IsSelf) ?? list[0];
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
			NodeTotalText.Text = "of " + list.Count.ToString(CultureInfo.InvariantCulture);
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
			if (ex is OpenMANET_CertificateChangedException)
			{
				// The certificate changed mid-session: stop talking to this endpoint at once.
				ClearReconnectPassword();
				ResetConnection();
				AddMeshEvent("CRITICAL", "Disconnected: the node's HTTPS certificate changed during the session. Connect from Settings to review it.", OpenMANET_EventCategory.Connection);
				MainTabControl.SelectedIndex = 4;
				ConnectionStatusText.Text = "Certificate changed - disconnected";
				return;
			}
			string text = OpenMANET_Security.OperatorMessage(ex);
			if (!_refreshFailureActive)
			{
				AddMeshEvent("WARNING", "OpenMANET polling failed: " + text, OpenMANET_EventCategory.Connection);
				_refreshFailureActive = true;
			}
			if (!_nodesMarkedUnreachable && _lastNodes.Count > 0 && DateTimeOffset.UtcNow - _lastSuccessfulRefreshUtc >= UnreachableAfter)
			{
				MarkNodesUnreachable();
			}
			UpdateHealthFindings();
			UpdateConnectionStatusIndicator();
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
		// Polls reselect the node every 15 s; don't overwrite a callsign being typed.
		UpdateCallsignEditor(openMANET_NodeInfo, keepTyping: true);
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
			bool flag = IsBelowWithHysteresis(_bandwidthIsLow == true, bandwidthSample.MegabitsPerSecond, LowBandwidthThresholdMbps, BandwidthHysteresisMbps);
			if (_bandwidthIsLow.HasValue && _bandwidthIsLow.Value != flag)
			{
				AddMeshEvent(flag ? "WARNING" : "INFO", flag ? ("Mesh bandwidth fell below " + LowBandwidthThresholdMbps.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps.") : ("Mesh bandwidth recovered to " + (LowBandwidthThresholdMbps + BandwidthHysteresisMbps).ToString("0.0", CultureInfo.InvariantCulture) + " Mbps or more."), OpenMANET_EventCategory.Links);
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
			AddMeshEvent(available ? "INFO" : "CRITICAL", available ? "HaLow mesh interface recovered." : "HaLow mesh interface is unavailable.", OpenMANET_EventCategory.Gateway);
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
			UtilizationProgressBar.Foreground = (FindResource("MetricBrush") as Brush) ?? SuccessIndicatorBrush;
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
		string text2 = NextUtilizationState(_utilizationState, num);
		if (text2 == "Critical")
		{
			foreground = (FindResource("DangerBrush") as Brush) ?? DangerIndicatorBrush;
			text = "High utilization relative to estimated link capacity";
		}
		else if (text2 == "Warning")
		{
			foreground = (FindResource("WarningBrush") as Brush) ?? WarningIndicatorBrush;
			text = "Elevated utilization relative to estimated link capacity";
		}
		else
		{
			foreground = (FindResource("MetricBrush") as Brush) ?? SuccessIndicatorBrush;
			text = "Throughput compared with conservative mesh bandwidth";
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
			openMANET.AddMeshEvent(severity, (text2 == "Normal") ? ("Mesh utilization returned below " + (WarningUtilizationPercent - UtilizationHysteresisPercent).ToString("0", CultureInfo.InvariantCulture) + "%.") : ("Mesh utilization entered the " + text2.ToLowerInvariant() + " range at " + num.ToString("0.0", CultureInfo.InvariantCulture) + "%."), OpenMANET_EventCategory.Gateway);
		}
		_utilizationState = text2;
		UtilizationProgressBar.Foreground = foreground;
		UtilizationCurrentValueText.Foreground = foreground;
		UtilizationStatusText.Text = text;
	}

	/// <summary>
	/// Steps the Normal/Warning/Critical utilization state. Each level is entered at its
	/// threshold and kept until utilization drops a full hysteresis band below it.
	/// </summary>
	private static string NextUtilizationState(string previous, double percent)
	{
		bool wasCritical = previous == "Critical";
		bool wasWarningOrWorse = wasCritical || previous == "Warning";
		if (IsAboveWithHysteresis(wasCritical, percent, CriticalUtilizationPercent, UtilizationHysteresisPercent))
		{
			return "Critical";
		}
		if (IsAboveWithHysteresis(wasWarningOrWorse, percent, WarningUtilizationPercent, UtilizationHysteresisPercent))
		{
			return "Warning";
		}
		return "Normal";
	}

	private void AddMeshEvent(string severity, string message, OpenMANET_EventCategory category = OpenMANET_EventCategory.None)
	{
		string text = OpenMANET_Security.NormalizeDetail(message);
		if (!string.IsNullOrWhiteSpace(text) && _preferences.ShouldNotify(category))
		{
			OpenMANET_PluginHost.Notify(text);
		}
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
				_previousNodeStates.TryGetValue(nodeSelectionKey, out NodeStateSnapshot previousState);
				dictionary[nodeSelectionKey] = NodeStateSnapshot.FromNode(node, previousState);
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
					AddMeshEvent("INFO", item.Value.Name + " joined the mesh.", OpenMANET_EventCategory.Nodes);
					continue;
				}
				NodeStateSnapshot value2 = item.Value;
				if (value.IsOnline && !value2.IsOnline)
				{
					AddMeshEvent("WARNING", value2.Name + " became stale.", OpenMANET_EventCategory.Nodes);
				}
				else if (!value.IsOnline && value2.IsOnline)
				{
					AddMeshEvent("INFO", value2.Name + (value.IsDiscovering ? " came online." : " recovered."), OpenMANET_EventCategory.Nodes);
				}
				if (value.IsOnline && value2.IsOnline && !value.IsRouted && value2.IsRouted)
				{
					AddMeshEvent("INFO", value2.Name + " route established.");
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
				if (value.RssiPoor != value2.RssiPoor)
				{
					AddMeshEvent(value2.RssiPoor ? "WARNING" : "INFO", value2.RssiPoor ? (value2.Name + " RSSI degraded below " + PoorRssiThresholdDbm.ToString(CultureInfo.InvariantCulture) + " dBm.") : (value2.Name + " RSSI recovered to " + (PoorRssiThresholdDbm + RssiHysteresisDb).ToString("0", CultureInfo.InvariantCulture) + " dBm or better."), OpenMANET_EventCategory.Links);
				}
			}
			foreach (KeyValuePair<string, NodeStateSnapshot> previousNodeState in _previousNodeStates)
			{
				if (!dictionary.ContainsKey(previousNodeState.Key))
				{
					AddMeshEvent("WARNING", previousNodeState.Value.Name + " left the mesh.", OpenMANET_EventCategory.Nodes);
				}
			}
		}
		_previousNodeStates.Clear();
		foreach (KeyValuePair<string, NodeStateSnapshot> item2 in dictionary)
		{
			_previousNodeStates[item2.Key] = item2.Value;
		}
	}

	private void UpdateEndpointFailures(IReadOnlyDictionary<string, string> failures)
	{
		foreach (KeyValuePair<string, string> failure in failures)
		{
			if (!_endpointFailures.ContainsKey(failure.Key))
			{
				AddMeshEvent("WARNING", failure.Key + " unavailable; showing last known data. " + failure.Value);
			}
		}
		foreach (string endpoint in _endpointFailures.Keys)
		{
			if (!failures.ContainsKey(endpoint))
			{
				AddMeshEvent("INFO", endpoint + " recovered.");
			}
		}
		_endpointFailures.Clear();
		foreach (KeyValuePair<string, string> failure in failures)
		{
			_endpointFailures[failure.Key] = failure.Value;
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
			openMANET.AddMeshEvent(severity, "Mesh topology changed from " + _previousTopologyHealth + " to " + text + ".", OpenMANET_EventCategory.Gateway);
		}
		_previousTopologyHealth = text;
		if (system == null)
		{
			_previousSystemState = null;
			return;
		}
		SystemStateSnapshot systemStateSnapshot = SystemStateSnapshot.FromSystem(system, _previousSystemState);
		if (_previousSystemState != null)
		{
			string resourceThreshold = HighResourcePercent.ToString("0", CultureInfo.InvariantCulture) + "%";
			string resourceClear = (HighResourcePercent - ResourceHysteresisPercent).ToString("0", CultureInfo.InvariantCulture) + "%";
			string temperatureThreshold = HighTemperatureCelsius.ToString("0", CultureInfo.InvariantCulture) + " °C";
			string temperatureClear = (HighTemperatureCelsius - TemperatureHysteresisCelsius).ToString("0", CultureInfo.InvariantCulture) + " °C";
			AddThresholdTransition("Gateway CPU load", _previousSystemState.HighCpu, systemStateSnapshot.HighCpu, resourceThreshold, resourceClear);
			AddThresholdTransition("Gateway memory usage", _previousSystemState.HighMemory, systemStateSnapshot.HighMemory, resourceThreshold, resourceClear);
			AddThresholdTransition("Gateway storage usage", _previousSystemState.HighStorage, systemStateSnapshot.HighStorage, resourceThreshold, resourceClear);
			AddThresholdTransition("Gateway CPU temperature", _previousSystemState.HighTemperature, systemStateSnapshot.HighTemperature, temperatureThreshold, temperatureClear);
		}
		_previousSystemState = systemStateSnapshot;
	}

	private void AddThresholdTransition(string metric, bool previousHigh, bool currentHigh, string threshold, string clearThreshold)
	{
		if (previousHigh != currentHigh)
		{
			AddMeshEvent(currentHigh ? "WARNING" : "INFO", currentHigh ? (metric + " crossed the " + threshold + " warning threshold.") : (metric + " recovered below " + clearThreshold + "."), OpenMANET_EventCategory.Gateway);
		}
	}

	/// <summary>True once value reaches threshold; stays true until it falls below threshold - band.</summary>
	private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
	{
		SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
		brush.Freeze();
		return brush;
	}

	private static bool IsAboveWithHysteresis(bool wasAbove, double value, double threshold, double band)
	{
		return value >= (wasAbove ? threshold - band : threshold);
	}

	/// <summary>True once value drops below threshold; stays true until it reaches threshold + band.</summary>
	private static bool IsBelowWithHysteresis(bool wasBelow, double value, double threshold, double band)
	{
		return value < (wasBelow ? threshold + band : threshold);
	}

	private static string DisplayValue(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim().ToUpperInvariant();
		}
		return "none";
	}

	/// <summary>Tracks the connected node's fix quality and whether WinTAK is using another node's GPS, once per poll.</summary>
	private void UpdateGpsChecks(List<OpenMANET_NodeInfo> nodes)
	{
		OpenMANET_NodeInfo connected = nodes.Find((OpenMANET_NodeInfo n) => n.IsSelf);
		bool poor = connected != null && connected.HasPoorGpsFix;
		if (poor && !_poorGpsLogged)
		{
			AddMeshEvent("WARNING", PoorGpsMessage(connected));
		}
		_poorGpsLogged = poor;
		OpenMANET_NodeInfo source = OpenMANET_GpsCheck.FindBorrowedGpsSource(nodes, OpenMANET_Operators.Self());
		string key = (source != null) ? OpenMANET_MapLayer.NodeKey(source) : null;
		_borrowedGpsPolls = (key == null) ? 0 : ((key == _borrowedGpsNodeKey) ? (_borrowedGpsPolls + 1) : 1);
		_borrowedGpsNodeKey = key;
		if (_borrowedGpsPolls == OpenMANET_GpsCheck.PollsBeforeReporting)
		{
			AddMeshEvent("WARNING", BorrowedGpsMessage(source, connected));
		}
		_borrowedGpsSource = (_borrowedGpsPolls >= OpenMANET_GpsCheck.PollsBeforeReporting) ? source : null;
	}

	private static string PoorGpsMessage(OpenMANET_NodeInfo node)
	{
		return node.DisplayName + "'s GPS fix is poor (" + node.GpsQualityDisplay.Replace("Poor · ", string.Empty) + "). Positions can be off by tens of meters; give its GPS antenna a clear view of the sky.";
	}

	private static string BorrowedGpsMessage(OpenMANET_NodeInfo source, OpenMANET_NodeInfo connected)
	{
		string connectedName = connected?.DisplayName ?? "the connected node";
		return "WinTAK's position matches " + source.DisplayName + "'s GPS, not " + connectedName + "'s" + ((connected != null && !connected.HasPosition) ? " (" + connectedName + " has no fix)" : string.Empty) + ". With Network Provided GPS, WinTAK takes NMEA from any node that sends it, and nodes send it to every device in their DHCP lease list. See the plugin README's node GPS section.";
	}

	private void UpdateHealthFindings()
	{
		_healthFindings.Clear();
		bool flag = false;
		bool flag2 = false;
		if (!_isConnected)
		{
			AddHealthFinding("OpenMANET gateway is not connected.", DangerIndicatorBrush, critical: true);
			flag = true;
		}
		else
		{
			if (AllowInsecureHttpCheckBox.IsChecked == true)
			{
				AddHealthFinding("Connection uses insecure HTTP; credentials, the bearer token, and telemetry are not encrypted.", WarningIndicatorBrush, critical: false);
				flag2 = true;
			}
			if (!_lastNodes.Exists((OpenMANET_NodeInfo node) => !node.IsSelf))
			{
				AddHealthFinding("No other mesh nodes are reporting.", DangerIndicatorBrush, critical: true);
				flag = true;
			}
			if (_nodesMarkedUnreachable)
			{
				AddHealthFinding("Lost contact with the OpenMANET gateway; node status is unknown.", DangerIndicatorBrush, critical: true);
				flag = true;
			}
			else if (_refreshFailureActive)
			{
				AddHealthFinding("The latest OpenMANET poll failed; displayed data may be stale.", WarningIndicatorBrush, critical: false);
				flag2 = true;
			}
			foreach (KeyValuePair<string, string> endpointFailure in _endpointFailures)
			{
				AddHealthFinding(endpointFailure.Key + " unavailable; showing last known data. " + endpointFailure.Value, WarningIndicatorBrush, critical: false);
				flag2 = true;
			}
			foreach (OpenMANET_NodeInfo lastNode in _lastNodes)
			{
				string text = lastNode.DisplayName;
				if (!lastNode.IsOnline)
				{
					AddHealthFinding(text + (lastNode.IsDiscovering ? " is listed but not reachable yet." : " is stale."), WarningIndicatorBrush, critical: false);
					flag2 = true;
				}
				if (lastNode.GossipStale && lastNode.IsOnline)
				{
					AddHealthFinding(text + " topology data is stale.", WarningIndicatorBrush, critical: false);
					flag2 = true;
				}
				int? num = lastNode.CurrentRssi ?? lastNode.Rssi;
				if (num.HasValue && _previousNodeStates.TryGetValue(GetNodeSelectionKey(lastNode), out NodeStateSnapshot nodeState) && nodeState.RssiPoor)
				{
					AddHealthFinding(text + " RSSI is weak at " + num.Value.ToString(CultureInfo.InvariantCulture) + " dBm.", WarningIndicatorBrush, critical: false);
					flag2 = true;
				}
			}
			if (_meshInterfaceAvailable.HasValue && !_meshInterfaceAvailable.Value)
			{
				AddHealthFinding("The HaLow mesh interface is unavailable.", DangerIndicatorBrush, critical: true);
				flag = true;
			}
			if (_currentBandwidthMbps.HasValue && _bandwidthIsLow == true)
			{
				AddHealthFinding("Mesh bandwidth is low at " + _currentBandwidthMbps.Value.ToString("0.00", CultureInfo.InvariantCulture) + " Mbps.", WarningIndicatorBrush, critical: false);
				flag2 = true;
			}
			if (_currentUtilizationPercent.HasValue && (_utilizationState == "Warning" || _utilizationState == "Critical"))
			{
				bool flag3 = _utilizationState == "Critical";
				AddHealthFinding("Estimated mesh utilization is " + _currentUtilizationPercent.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%.", flag3 ? DangerIndicatorBrush : WarningIndicatorBrush, flag3);
				flag2 = true;
			}
			// GPS findings are advisory: they don't change mesh health or the toolbar badge.
			OpenMANET_NodeInfo connectedNode = _lastNodes.Find((OpenMANET_NodeInfo n) => n.IsSelf);
			if (connectedNode != null && connectedNode.HasPoorGpsFix)
			{
				AddHealthFinding(PoorGpsMessage(connectedNode), WarningIndicatorBrush, critical: false);
			}
			if (_borrowedGpsSource != null)
			{
				AddHealthFinding(BorrowedGpsMessage(_borrowedGpsSource, connectedNode), WarningIndicatorBrush, critical: false);
			}
			OpenMANET_SystemInfo lastSystemDetails = _client.LastSystemDetails;
			SystemStateSnapshot systemState = _previousSystemState;
			if (lastSystemDetails != null && systemState != null)
			{
				if (systemState.HighCpu)
				{
					AddHealthFinding("Gateway CPU load is " + lastSystemDetails.CpuLoadValue.ToString("0.0", CultureInfo.InvariantCulture) + "%.", WarningIndicatorBrush, critical: false);
					flag2 = true;
				}
				if (systemState.HighMemory)
				{
					AddHealthFinding("Gateway memory usage is " + lastSystemDetails.MemoryUsagePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%.", WarningIndicatorBrush, critical: false);
					flag2 = true;
				}
				if (systemState.HighStorage)
				{
					AddHealthFinding("Gateway storage usage is " + lastSystemDetails.OverlayUsagePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%.", WarningIndicatorBrush, critical: false);
					flag2 = true;
				}
				if (systemState.HighTemperature)
				{
					AddHealthFinding("Gateway CPU temperature is " + lastSystemDetails.CpuTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture) + " °C.", WarningIndicatorBrush, critical: false);
					flag2 = true;
				}
			}
		}
		if (_healthFindings.Count == 0)
		{
			AddHealthFinding("No active health findings.", SuccessIndicatorBrush, critical: false);
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
			foreground = DangerIndicatorBrush;
		}
		else
		{
			foreground = (flag2 ? WarningIndicatorBrush : SuccessIndicatorBrush);
		}
		healthSummaryText2.Foreground = foreground;
		OpenMANET_MeshHealth health;
		if (!_isConnected)
		{
			// A deliberate disconnect clears the toolbar badge; a dropped session shows red while reconnecting.
			health = _reconnecting ? OpenMANET_MeshHealth.Critical : OpenMANET_MeshHealth.None;
		}
		else if (flag)
		{
			health = OpenMANET_MeshHealth.Critical;
		}
		else
		{
			health = flag2 ? OpenMANET_MeshHealth.Degraded : OpenMANET_MeshHealth.Healthy;
		}
		OpenMANET_PluginHost.SetHealth(health);
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

	private void ApplyCallsigns(List<OpenMANET_NodeInfo> nodes, OpenMANET_TopologyInfo topology)
	{
		foreach (OpenMANET_NodeInfo node in nodes)
		{
			string[] macs = { node.MacAddress, node.LinkHardwareAddress };
			node.Callsign = _callsigns.Find(macs, node.Name);
			node.CallsignSharedBy = null;
			// Names set on this computer always win; shared ones only fill the gaps.
			OpenMANET_SharedCallsign shared = (node.HasCallsign || !_preferences.UseSharedCallsigns) ? null : OpenMANET_PluginHost.CallsignSharing.Find(macs, node.Name);
			if (shared != null)
			{
				node.Callsign = shared.Callsign;
				node.CallsignSharedBy = shared.SharedBy;
			}
		}
		if (topology == null)
		{
			return;
		}
		Dictionary<string, OpenMANET_TopologyNodeInfo> byMac = new Dictionary<string, OpenMANET_TopologyNodeInfo>(StringComparer.OrdinalIgnoreCase);
		foreach (OpenMANET_TopologyNodeInfo topologyNode in topology.Nodes)
		{
			List<string> macs = new List<string>(topologyNode.SecondaryMacs) { topologyNode.MacAddress };
			topologyNode.Callsign = _callsigns.Find(macs, topologyNode.Hostname) ?? (_preferences.UseSharedCallsigns ? OpenMANET_PluginHost.CallsignSharing.Find(macs, topologyNode.Hostname)?.Callsign : null);
			foreach (string mac in macs)
			{
				if (!string.IsNullOrWhiteSpace(mac))
				{
					byMac[mac.Trim()] = topologyNode;
				}
			}
		}
		foreach (OpenMANET_TopologyEdgeInfo edge in topology.Edges)
		{
			if (edge.FromMac != null && byMac.TryGetValue(edge.FromMac.Trim(), out OpenMANET_TopologyNodeInfo from))
			{
				edge.FromName = from.NameDisplay;
			}
			if (edge.ToMac != null && byMac.TryGetValue(edge.ToMac.Trim(), out OpenMANET_TopologyNodeInfo to))
			{
				edge.ToName = to.NameDisplay;
			}
		}
	}

	/// <summary>
	/// After polling has failed long enough, stops presenting the last poll as current: every
	/// node becomes Stale until a poll succeeds again, including on the map.
	/// </summary>
	private void MarkNodesUnreachable()
	{
		_nodesMarkedUnreachable = true;
		foreach (OpenMANET_NodeInfo node in _lastNodes)
		{
			node.IsOnline = false;
			node.IsRouted = false;
			node.HasLiveLink = false;
			node.IsDiscovering = false;
		}
		NodeCountText.Text = "0";
		AddMeshEvent("CRITICAL", "Lost contact with the OpenMANET gateway; all nodes are shown as stale until polling recovers.", OpenMANET_EventCategory.Connection);
		if (NodeDetailsPanel.DataContext is OpenMANET_NodeInfo selected)
		{
			RefreshNodeNameViews(selected);
		}
		else
		{
			NodesListBox.Items.Refresh();
			LinkQualityNodeComboBox.Items.Refresh();
			UpdateMapLayer();
		}
		UpdateHealthFindings();
	}

	private void UpdateMapLayer()
	{
		if (_isConnected && _preferences.ShowOnMap)
		{
			_mapLayer.Update(_lastNodes, _lastTopology, OpenMANET_Operators.Current(DateTime.UtcNow), _preferences.MergeDistanceMeters, _preferences.ShowUnattendedNodes);
		}
		else
		{
			_mapLayer.Clear(synchronous: false);
		}
	}

	private void LoadPreferenceControls()
	{
		NotifyNodesCheckBox.IsChecked = _preferences.NotifyNodes;
		NotifyLinksCheckBox.IsChecked = _preferences.NotifyLinks;
		NotifyGatewayCheckBox.IsChecked = _preferences.NotifyGateway;
		NotifyConnectionCheckBox.IsChecked = _preferences.NotifyConnection;
		AutoReconnectCheckBox.IsChecked = _preferences.AutoReconnect;
		ShowOnMapCheckBox.IsChecked = _preferences.ShowOnMap;
		HideNodeOwnMarkersCheckBox.IsChecked = _preferences.HideNodeOwnMarkers;
		ShowUnattendedNodesCheckBox.IsChecked = _preferences.ShowUnattendedNodes;
		MergeDistanceComboBox.SelectedItem = _preferences.MergeDistanceMeters;
		ShareCallsignsCheckBox.IsChecked = _preferences.ShareCallsigns;
		UseSharedCallsignsCheckBox.IsChecked = _preferences.UseSharedCallsigns;
	}

	private void MergeDistanceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!(MergeDistanceComboBox.SelectedItem is int meters) || meters == _preferences.MergeDistanceMeters)
		{
			return;
		}
		_preferences.MergeDistanceMeters = meters;
		try
		{
			_preferences.Save();
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			AddMeshEvent("WARNING", "Could not save map preferences: " + ex.Message);
		}
		UpdateMapLayer();
	}

	private void PreferenceCheckBox_Changed(object sender, RoutedEventArgs e)
	{
		_preferences.NotifyNodes = NotifyNodesCheckBox.IsChecked == true;
		_preferences.NotifyLinks = NotifyLinksCheckBox.IsChecked == true;
		_preferences.NotifyGateway = NotifyGatewayCheckBox.IsChecked == true;
		_preferences.NotifyConnection = NotifyConnectionCheckBox.IsChecked == true;
		_preferences.AutoReconnect = AutoReconnectCheckBox.IsChecked == true;
		_preferences.ShowOnMap = ShowOnMapCheckBox.IsChecked == true;
		_preferences.HideNodeOwnMarkers = HideNodeOwnMarkersCheckBox.IsChecked == true;
		_preferences.ShowUnattendedNodes = ShowUnattendedNodesCheckBox.IsChecked == true;
		bool wasSharing = _preferences.ShareCallsigns;
		bool wasUsingShared = _preferences.UseSharedCallsigns;
		_preferences.ShareCallsigns = ShareCallsignsCheckBox.IsChecked == true;
		_preferences.UseSharedCallsigns = UseSharedCallsignsCheckBox.IsChecked == true;
		if (_preferences.ShareCallsigns != wasSharing)
		{
			if (_preferences.ShareCallsigns)
			{
				ShareCallsigns(announce: true);
			}
			else
			{
				// An empty list tells other operators to drop the names this computer shared.
				OpenMANET_PluginHost.BroadcastCallsigns(new List<OpenMANET_CallsignEntry>());
				AddMeshEvent("INFO", "Stopped sharing callsigns; other operators running the plugin remove the ones you shared.");
			}
		}
		if (_preferences.UseSharedCallsigns != wasUsingShared)
		{
			if (_preferences.UseSharedCallsigns)
			{
				OpenMANET_PluginHost.RequestSharedCallsigns();
			}
			ReapplyCallsigns();
		}
		UpdateCallsignSharingText();
		UpdateMapLayer();
		if (!_preferences.AutoReconnect)
		{
			// Turning it off drops the in-memory password right away.
			if (_reconnecting)
			{
				StopReconnect("Automatic reconnect turned off.");
			}
			ClearReconnectPassword();
		}
		try
		{
			_preferences.Save();
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			AddMeshEvent("WARNING", "Could not save alert preferences: " + ex.Message);
		}
	}

	private void ExportCallsignsButton_Click(object sender, RoutedEventArgs e)
	{
		if (_callsigns.Count == 0)
		{
			MessageBox.Show("No callsigns have been assigned yet. Set them on the Nodes tab.", "OpenMANET", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = "Export callsigns",
			FileName = "openmanet-callsigns.csv",
			Filter = "Callsign files (*.csv)|*.csv|All files (*.*)|*.*",
			DefaultExt = ".csv"
		};
		if (dialog.ShowDialog() != true)
		{
			return;
		}
		try
		{
			_callsigns.ExportTo(dialog.FileName);
			AddMeshEvent("INFO", "Exported " + _callsigns.Count.ToString(CultureInfo.InvariantCulture) + " callsigns.");
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			MessageBox.Show(ex.Message, "OpenMANET", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void ImportCallsignsButton_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "Import callsigns",
			Filter = "Callsign files (*.csv)|*.csv|All files (*.*)|*.*",
			CheckFileExists = true
		};
		if (dialog.ShowDialog() != true)
		{
			return;
		}
		int imported;
		int skipped;
		try
		{
			imported = _callsigns.ImportFrom(dialog.FileName, out skipped);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
		{
			MessageBox.Show(ex.Message, "OpenMANET", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		ReapplyCallsigns();
		if (_preferences.ShareCallsigns)
		{
			ShareCallsigns(announce: false);
		}
		string summary = "Imported " + imported.ToString(CultureInfo.InvariantCulture) + ((imported == 1) ? " callsign" : " callsigns") + ((skipped > 0) ? ("; skipped " + skipped.ToString(CultureInfo.InvariantCulture) + " invalid " + ((skipped == 1) ? "line" : "lines")) : string.Empty) + ".";
		AddMeshEvent("INFO", summary);
		MessageBox.Show(summary, "OpenMANET", MessageBoxButton.OK, MessageBoxImage.Information);
	}

	private void SaveCallsignButton_Click(object sender, RoutedEventArgs e)
	{
		SetSelectedNodeCallsign(CallsignTextBox.Text);
	}

	private void ClearCallsignButton_Click(object sender, RoutedEventArgs e)
	{
		SetSelectedNodeCallsign(null);
	}

	private void CallsignTextBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			SetSelectedNodeCallsign(CallsignTextBox.Text);
			e.Handled = true;
		}
	}

	private void SetSelectedNodeCallsign(string callsign)
	{
		if (!(NodeDetailsPanel.DataContext is OpenMANET_NodeInfo node))
		{
			return;
		}
		try
		{
			_callsigns.Set(node.MacAddress, node.Name, callsign);
		}
		catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
		{
			MessageBox.Show(ex.Message, "OpenMANET", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		string oldName = node.DisplayName;
		ApplyCallsigns(_lastNodes, _lastTopology);
		if (!string.Equals(oldName, node.DisplayName, StringComparison.Ordinal))
		{
			AddMeshEvent("INFO", node.HasCallsign ? (node.Name + " is now shown as " + node.Callsign + ".") : (oldName + " callsign cleared; showing " + node.DisplayName + "."));
		}
		RefreshNodeNameViews(node);
		if (_preferences.ShareCallsigns)
		{
			ShareCallsigns(announce: false);
		}
	}

	/// <summary>Reapplies saved and shared callsigns to the current nodes and redraws everything that shows names.</summary>
	private void ReapplyCallsigns()
	{
		ApplyCallsigns(_lastNodes, _lastTopology);
		if (NodeDetailsPanel.DataContext is OpenMANET_NodeInfo selected)
		{
			RefreshNodeNameViews(selected);
		}
		else
		{
			NodesListBox.Items.Refresh();
			LinkQualityNodeComboBox.Items.Refresh();
			RenderTopology();
			UpdateMapLayer();
		}
	}

	/// <summary>Shows only this computer's callsign in the editor; a shared one is described below it instead.</summary>
	private void UpdateCallsignEditor(OpenMANET_NodeInfo node, bool keepTyping)
	{
		if (!keepTyping || !CallsignTextBox.IsKeyboardFocusWithin)
		{
			CallsignTextBox.Text = (node.CallsignSharedBy == null) ? (node.Callsign ?? string.Empty) : string.Empty;
		}
		CallsignCaptionText.Text = (node.CallsignSharedBy == null)
			? "Vanity name shown instead of the hostname. Saved on this computer only."
			: ("Shown as " + node.Callsign + ", shared by " + node.CallsignSharedBy + ". Save a callsign here to use your own instead.");
	}

	/// <summary>Broadcasts this computer's callsigns to other operators on the TAK network.</summary>
	private void ShareCallsigns(bool announce)
	{
		_lastCallsignShareUtc = DateTimeOffset.UtcNow;
		List<OpenMANET_CallsignEntry> saved = _callsigns.Entries;
		List<OpenMANET_CallsignEntry> entries = ExpandForSharing(saved);
		bool sent;
		try
		{
			sent = OpenMANET_PluginHost.BroadcastCallsigns(entries);
		}
		catch (Exception ex)
		{
			AddMeshEvent("WARNING", "Could not share callsigns: " + OpenMANET_Security.OperatorMessage(ex));
			return;
		}
		if (!sent)
		{
			if (announce)
			{
				AddMeshEvent("WARNING", "Callsigns can't be shared: WinTAK's network service isn't available.");
			}
			return;
		}
		if (announce)
		{
			AddMeshEvent("INFO", "Sharing " + saved.Count.ToString(CultureInfo.InvariantCulture) + ((saved.Count == 1) ? " callsign" : " callsigns") + " with other operators on your TAK network.");
		}
	}

	/// <summary>
	/// Each node shows different MACs depending on which node you ask (the connected node sees its
	/// own interface MACs, others see its mesh MACs), so a shared callsign carries every MAC this
	/// plugin knows for the node, plus its hostname, letting other operators match it from their side.
	/// </summary>
	private List<OpenMANET_CallsignEntry> ExpandForSharing(List<OpenMANET_CallsignEntry> saved)
	{
		List<OpenMANET_CallsignEntry> expanded = new List<OpenMANET_CallsignEntry>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (OpenMANET_CallsignEntry entry in saved)
		{
			OpenMANET_NodeInfo node = _lastNodes.Find((OpenMANET_NodeInfo n) => (entry.Mac != null && (OpenMANET_CallsignStore.NormalizeMac(n.MacAddress) == entry.Mac || OpenMANET_CallsignStore.NormalizeMac(n.LinkHardwareAddress) == entry.Mac)) || (entry.Mac == null && entry.Hostname != null && OpenMANET_CallsignStore.NormalizeHostname(n.Name) == entry.Hostname));
			string hostname = entry.Hostname ?? OpenMANET_CallsignStore.NormalizeHostname(node?.Name);
			// Naming the node you're connected to marks you as its operator, which outranks other operators' names.
			bool fromNodeOperator = node != null && node.IsSelf;
			List<string> macs = new List<string> { entry.Mac };
			if (node != null)
			{
				macs.Add(node.MacAddress);
				macs.Add(node.LinkHardwareAddress);
				OpenMANET_TopologyNodeInfo topologyNode = _lastTopology?.Nodes.FirstOrDefault((OpenMANET_TopologyNodeInfo t) => (node.IsSelf && t.IsSelf) || (hostname != null && OpenMANET_CallsignStore.NormalizeHostname(t.Hostname) == hostname) || (OpenMANET_CallsignStore.NormalizeMac(t.MacAddress) != null && OpenMANET_CallsignStore.NormalizeMac(t.MacAddress) == OpenMANET_CallsignStore.NormalizeMac(node.MacAddress)));
				if (topologyNode != null)
				{
					macs.Add(topologyNode.MacAddress);
					macs.AddRange(topologyNode.SecondaryMacs);
				}
			}
			bool addedAny = false;
			foreach (string mac in macs.Select(OpenMANET_CallsignStore.NormalizeMac).Where((string m) => m != null).Distinct())
			{
				if (seen.Add(mac))
				{
					expanded.Add(new OpenMANET_CallsignEntry { Mac = mac, Hostname = hostname, Callsign = entry.Callsign, UpdatedUtc = entry.UpdatedUtc, FromNodeOperator = fromNodeOperator });
					addedAny = true;
				}
			}
			if (!addedAny && hostname != null)
			{
				expanded.Add(new OpenMANET_CallsignEntry { Mac = null, Hostname = hostname, Callsign = entry.Callsign, UpdatedUtc = entry.UpdatedUtc, FromNodeOperator = fromNodeOperator });
			}
		}
		return expanded;
	}

	private void CallsignSharing_Received(object sender, OpenMANET_CallsignsReceivedEventArgs e)
	{
		// Arrives on WinTAK's message thread.
		Dispatcher.BeginInvoke((Action)(() =>
		{
			if (!_preferences.UseSharedCallsigns)
			{
				return;
			}
			AddMeshEvent("INFO", (e.Count == 0) ? (e.SharedBy + " stopped sharing callsigns.") : ("Received " + e.Count.ToString(CultureInfo.InvariantCulture) + ((e.Count == 1) ? " callsign" : " callsigns") + " from " + e.SharedBy + "."));
			ReapplyCallsigns();
			UpdateCallsignSharingText();
		}));
	}

	private void PluginHost_CallsignsRequested(object sender, EventArgs e)
	{
		// Arrives on WinTAK's message thread.
		Dispatcher.BeginInvoke((Action)(() =>
		{
			if (_preferences.ShareCallsigns && DateTimeOffset.UtcNow - _lastCallsignShareUtc >= OpenMANET_CallsignSharing.RequestAnswerInterval)
			{
				ShareCallsigns(announce: false);
			}
		}));
	}

	private void ForgetSharedCallsignsButton_Click(object sender, RoutedEventArgs e)
	{
		OpenMANET_PluginHost.CallsignSharing.Clear();
		ReapplyCallsigns();
		UpdateCallsignSharingText();
		AddMeshEvent("INFO", "Forgot callsigns shared by other operators. They return when those operators share again.");
	}

	private void UpdateCallsignSharingText()
	{
		OpenMANET_CallsignSharing sharing = OpenMANET_PluginHost.CallsignSharing;
		int senders = sharing.SenderCount;
		int entries = sharing.EntryCount;
		SharedCallsignsStatusText.Text = (senders == 0)
			? "No callsigns received from other operators."
			: ("Holding " + entries.ToString(CultureInfo.InvariantCulture) + ((entries == 1) ? " callsign" : " callsigns") + " from " + senders.ToString(CultureInfo.InvariantCulture) + ((senders == 1) ? " operator" : " operators") + (_preferences.UseSharedCallsigns ? "." : " (not shown while turned off)."));
		ForgetSharedCallsignsButton.IsEnabled = senders > 0;
	}

	/// <summary>Opens the Nodes tab on the node whose map marker the operator picked.</summary>
	private void PluginHost_NodeFocusRequested(object sender, string nodeKey)
	{
		OpenMANET_NodeInfo node = _lastNodes.Find((OpenMANET_NodeInfo n) => string.Equals(OpenMANET_MapLayer.NodeKey(n), nodeKey, StringComparison.Ordinal));
		if (node == null)
		{
			return;
		}
		MainTabControl.SelectedIndex = 2;
		NodesListBox.SelectedItem = node;
		NodesListBox.ScrollIntoView(node);
	}

	/// <summary>Records each change in how a node is seen (status, route, RF link, neighbor table, hops).</summary>
	private void RecordNodeTimelines(List<OpenMANET_NodeInfo> nodes)
	{
		DateTimeOffset now = DateTimeOffset.Now;
		foreach (OpenMANET_NodeInfo node in nodes)
		{
			string key = GetNodeSelectionKey(node);
			if (!_nodeTimelines.TryGetValue(key, out List<NodeTimelineEntry> timeline))
			{
				timeline = new List<NodeTimelineEntry>();
				_nodeTimelines[key] = timeline;
			}
			NodeTimelineEntry entry = NodeTimelineEntry.FromNode(node, now);
			if (timeline.Count == 0 || timeline[timeline.Count - 1].State != entry.State)
			{
				// Keep the first entry: it anchors the time since the node was first seen.
				if (timeline.Count >= MaximumTimelineEntries)
				{
					timeline.RemoveAt(1);
				}
				timeline.Add(entry);
			}
		}
	}

	/// <summary>Node view models aren't observable, so rebind the views that show names.</summary>
	private void RefreshNodeNameViews(OpenMANET_NodeInfo selected)
	{
		_nodeSelectionInProgress = true;
		try
		{
			NodesListBox.Items.Refresh();
			LinkQualityNodeComboBox.Items.Refresh();
			LinkQualityNodeComboBox.SelectedItem = selected;
		}
		finally
		{
			_nodeSelectionInProgress = false;
		}
		NodeDetailsPanel.DataContext = null;
		NodeDetailsPanel.DataContext = selected;
		UpdateCallsignEditor(selected, keepTyping: false);
		if (_lastTopology != null)
		{
			TopologyContent.DataContext = null;
			TopologyContent.DataContext = _lastTopology;
			SelectTopologyNode(selected);
		}
		RenderTopology();
		UpdateMapLayer();
	}

	private static string GetNodeSelectionKey(OpenMANET_NodeInfo node)
	{
		if (!string.IsNullOrWhiteSpace(node.MacAddress) && node.MacAddress != "--")
		{
			return "mac:" + node.MacAddress.Trim();
		}
		return "host:" + (node.Name ?? string.Empty).Trim();
	}

	private OpenMANET_NodeInfo FindNodeBySelectionKey(string key)
	{
		return _lastNodes.Find((OpenMANET_NodeInfo node) => string.Equals(GetNodeSelectionKey(node), key, StringComparison.OrdinalIgnoreCase));
	}

	private void ClearNodeDetails()
	{
		_selectedNodeKey = null;
		NodeDetailsPanel.DataContext = null;
		CallsignTextBox.Text = string.Empty;
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
					stroke = SuccessIndicatorBrush;
				}
				else
				{
					stroke = (edge.IsBlos ? WarningIndicatorBrush : CreateFrozenBrush(0x4A, 0x52, 0x62));
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
			brush = CreateFrozenBrush(0x6B, 0x72, 0x80);
		}
		else if (node.IsSelf)
		{
			brush = SuccessIndicatorBrush;
		}
		else
		{
			brush = (node.IsGateway ? CreateFrozenBrush(0x2D, 0xD4, 0xBF) : CreateFrozenBrush(0x5B, 0x64, 0x75));
		}
		Brush borderBrush;
		if (isSelected)
		{
			borderBrush = CreateFrozenBrush(0x4C, 0x9A, 0xFF);
		}
		else
		{
			borderBrush = (node.GossipStale ? CreateFrozenBrush(0x6B, 0x72, 0x80) : CreateFrozenBrush(0x2A, 0x2F, 0x3A));
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
			Foreground = CreateFrozenBrush(0xA3, 0xA9, 0xB5),
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
		border.Background = CreateFrozenBrush(0x1E, 0x22, 0x2A);
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
				stringBuilder.AppendLine("- " + (includeOperationalIdentifiers ? DiagnosticText(lastNode.Name ?? "Unknown", includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : openMANET_DiagnosticRedactor.Alias(lastNode.Name ?? lastNode.MacAddress, "Node")) + ((includeOperationalIdentifiers && lastNode.HasCallsign) ? (" | Callsign=" + lastNode.Callsign) : string.Empty) + " | Role=" + lastNode.RoleDisplay + " | Status=" + lastNode.StatusDisplay + " | Address=" + (includeOperationalIdentifiers ? DiagnosticText(lastNode.Address ?? "--", includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : "[redacted]") + " | MAC=" + (includeOperationalIdentifiers ? DiagnosticText(lastNode.MacAddress, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : "[redacted]") + " | Hops=" + lastNode.HopsDisplay + " | RSSI=" + lastNode.RssiDisplay + " | Link=" + lastNode.ThroughputDisplay + " | Route=" + (includeOperationalIdentifiers ? DiagnosticText(lastNode.RouteInterfaceDisplay, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : "[redacted]") + " | LastSeen=" + lastNode.LinkAgeDisplay + " | Routed=" + (lastNode.IsRouted ? "yes" : "no") + " | LiveLink=" + (lastNode.HasLiveLink ? "yes" : "no"));
			}
		}
		AppendDiagnosticSection(stringBuilder, "MESH NEIGHBORS (RAW)");
		if (_client.LastNeighborDiagnostics.Count == 0)
		{
			stringBuilder.AppendLine("No radio neighbors reported.");
		}
		foreach (string neighborLine in _client.LastNeighborDiagnostics)
		{
			stringBuilder.AppendLine("- " + DiagnosticText(neighborLine, includeOperationalIdentifiers, openMANET_DiagnosticRedactor));
		}
		if (includeOperationalIdentifiers && _lastTopology != null)
		{
			stringBuilder.AppendLine("Topology node MACs:");
			foreach (OpenMANET_TopologyNodeInfo topologyNode in _lastTopology.Nodes)
			{
				stringBuilder.AppendLine("- " + DiagnosticText(topologyNode.NameDisplay, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) + " | MAC=" + topologyNode.MacDisplay + " | Secondary=" + ((topologyNode.SecondaryMacs.Count == 0) ? "--" : string.Join(", ", topologyNode.SecondaryMacs)) + " | Hops=" + topologyNode.HopsDisplay);
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
				stringBuilder.AppendLine("- " + (includeOperationalIdentifiers ? DiagnosticText(networkEntry.NameDisplay, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : openMANET_DiagnosticRedactor.Alias(networkEntry.NameDisplay, "Interface")) + ((includeOperationalIdentifiers && (networkEntry.NameDisplay ?? string.Empty).IndexOf("(" + networkEntry.InterfaceDisplay + ")", StringComparison.OrdinalIgnoreCase) < 0) ? (" (" + DiagnosticText(networkEntry.InterfaceDisplay, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) + ")") : string.Empty) +" | " + networkEntry.StateDisplay + " | " + networkEntry.TrafficDisplay);
			}
			if (includeOperationalIdentifiers)
			{
				stringBuilder.AppendLine("Services:");
				if (lastSystemDetails.Services.Count > 0 && lastSystemDetails.StoppedServiceCount == lastSystemDetails.Services.Count)
				{
					// The node is answering API calls, so openmanetd cannot really be stopped.
					stringBuilder.AppendLine("  Note: the node reports every service stopped while answering API calls; its service status is likely unreliable.");
				}
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
		AppendDiagnosticSection(stringBuilder, "NODE STATUS TIMELINE");
		stringBuilder.AppendLine("Each line is a change in how the node is seen; +s is time since the node was first seen this session.");
		if (_nodeTimelines.Count == 0)
		{
			stringBuilder.AppendLine("No node status recorded.");
		}
		foreach (KeyValuePair<string, List<NodeTimelineEntry>> timeline in _nodeTimelines)
		{
			OpenMANET_NodeInfo node = FindNodeBySelectionKey(timeline.Key);
			string rawName = node?.Name ?? timeline.Key.Substring(timeline.Key.IndexOf(':') + 1);
			stringBuilder.AppendLine(includeOperationalIdentifiers ? DiagnosticText(rawName, includeOperationalIdentifiers: true, openMANET_DiagnosticRedactor) : openMANET_DiagnosticRedactor.Alias(rawName, "Node"));
			DateTimeOffset firstSeen = timeline.Value[0].CapturedAt;
			foreach (NodeTimelineEntry entry in timeline.Value)
			{
				stringBuilder.AppendLine("  " + entry.CapturedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " (+" + ((int)(entry.CapturedAt - firstSeen).TotalSeconds).ToString(CultureInfo.InvariantCulture) + " s) " + entry.State + " | " + entry.Detail);
			}
		}
		AppendDiagnosticSection(stringBuilder, "CALLSIGN SHARING");
		stringBuilder.AppendLine("Share mine over TAK: " + (_preferences.ShareCallsigns ? "on" : "off") + (OpenMANET_PluginHost.CanShareCallsigns ? string.Empty : " (WinTAK network service unavailable)"));
		stringBuilder.AppendLine("Use shared callsigns: " + (_preferences.UseSharedCallsigns ? "on" : "off"));
		stringBuilder.AppendLine("Saved on this computer: " + _callsigns.Count.ToString(CultureInfo.InvariantCulture));
		stringBuilder.AppendLine("Received: " + OpenMANET_PluginHost.CallsignSharing.EntryCount.ToString(CultureInfo.InvariantCulture) + " from " + OpenMANET_PluginHost.CallsignSharing.SenderCount.ToString(CultureInfo.InvariantCulture) + " operators");
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
			redactor.Alias(lastNode.Callsign, "Callsign");
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
		bool locked = connected || _reconnecting;
		ConnectButton.Content = (connected ? "Disconnect" : (_reconnecting ? "Stop Reconnecting" : "Connect"));
		RefreshButton.IsEnabled = connected;
		ConnectionSettingsPanel.IsEnabled = !locked;
		ConnectionSettingsPanel.Opacity = (locked ? 0.5 : 1.0);
		if (connected)
		{
			UpdateConnectionStatusIndicator();
		}
		else if (_reconnecting)
		{
			ConnectionStatusText.Text = "Reconnecting...";
			ConnectionStatusIndicator.Fill = WarningIndicatorBrush;
		}
		else
		{
			ConnectionStatusText.Text = "Not Connected";
			ConnectionStatusIndicator.Fill = DangerIndicatorBrush;
		}
	}

	/// <summary>Sets the header status for a live connection, worst condition first.</summary>
	private void UpdateConnectionStatusIndicator()
	{
		if (!_isConnected)
		{
			return;
		}
		string text;
		SolidColorBrush fill = WarningIndicatorBrush;
		if (_refreshFailureActive)
		{
			text = "Connected - polling delayed";
		}
		else if (_endpointFailures.Count > 0)
		{
			text = "Connected - partial data";
		}
		else if (AllowInsecureHttpCheckBox.IsChecked == true)
		{
			text = "Connected — INSECURE HTTP";
		}
		else
		{
			text = "Connected";
			fill = SuccessIndicatorBrush;
		}
		ConnectionStatusText.Text = text;
		ConnectionStatusIndicator.Fill = fill;
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
		_endpointFailures.Clear();
		_previousNodeStates.Clear();
		_nodeTrendHistory.Clear();
		_nodeBaselineEstablished = false;
		_refreshFailureActive = false;
		_nodesMarkedUnreachable = false;
		_previousTopologyHealth = null;
		_previousSystemState = null;
		_meshInterfaceAvailable = null;
		_bandwidthIsLow = null;
		_utilizationState = null;
		_currentUtilizationPercent = null;
		_borrowedGpsNodeKey = null;
		_borrowedGpsPolls = 0;
		_borrowedGpsSource = null;
		_poorGpsLogged = false;
		_mapLayer.Clear(synchronous: false);
		NodesListBox.ItemsSource = null;
		NodesListBox.SelectedItem = null;
		LinkQualityNodeComboBox.ItemsSource = null;
		LinkQualityNodeComboBox.SelectedItem = null;
		GatewayStatusText.Text = "Not Connected";
		NodeCountText.Text = "0";
		NodeTotalText.Text = "of 0";
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
		// A deliberate disconnect or a failed manual login ends any automatic reconnect.
		ClearReconnectPassword();
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
			bool canReconnect = _preferences.AutoReconnect && _protectedPassword != null;
			ResetConnection();
			if (canReconnect)
			{
				AddMeshEvent("WARNING", "OpenMANET session expired or was rejected; reconnecting automatically.", OpenMANET_EventCategory.Connection);
				StartReconnect();
				return;
			}
			AddMeshEvent("WARNING", "OpenMANET authentication expired or was rejected. Reconnect to continue monitoring.", OpenMANET_EventCategory.Connection);
			MainTabControl.SelectedIndex = 4;
			ConnectionStatusText.Text = "Authentication expired - reconnect";
			ConnectionStatusIndicator.Fill = WarningIndicatorBrush;
		}
	}

	private void Dispatcher_ShutdownStarted(object sender, EventArgs e)
	{
		Dispatcher.ShutdownStarted -= Dispatcher_ShutdownStarted;
		OpenMANET_PluginHost.CallsignSharing.Received -= CallsignSharing_Received;
		OpenMANET_PluginHost.NodeFocusRequested -= PluginHost_NodeFocusRequested;
		OpenMANET_PluginHost.CallsignsRequested -= PluginHost_CallsignsRequested;
		_autoRefreshTimer.Stop();
		_throughputTimer.Stop();
		CancelOutstandingOperations();
		_reconnectCancellation?.Cancel();
		ClearReconnectPassword();
		OpenMANET_PluginHost.RemoveOwnNotifications();
		_mapLayer.Clear(synchronous: true);
		OpenMANET_PluginHost.SetHealth(OpenMANET_MeshHealth.None);
		// The dispatcher is going away, so the async UI path can't finish. The client never
		// resumes on the UI thread, so a short bounded wait here can't deadlock.
		try
		{
			Task.Run(() => _client.LogoutAsync(CancellationToken.None)).Wait(OpenMANET_Security.LogoutTimeout);
		}
		catch
		{
		}
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

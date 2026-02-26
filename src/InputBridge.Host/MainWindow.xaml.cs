using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InputBridge.Core.Configuration;
using InputBridge.Host.Hooks;
using InputBridge.Host.Services;
using InputBridge.Host.UI;
using InputBridge.Shared.UI.Services;

namespace InputBridge.Host;

public partial class MainWindow : Window
{
    private readonly KeyboardHook _keyboardHook;
    private readonly MouseHook _mouseHook;
    private readonly HotkeyManager _hotkeyManager;
    private readonly InputRouter _inputRouter;
    private readonly ConnectionManager _connectionManager;
    private readonly TrayService _trayService;
    private readonly SettingsManager _settingsManager;
    private AppSettings _appSettings;

    public MainWindow()
    {
        InitializeComponent();

        _settingsManager = new SettingsManager();
        _appSettings = _settingsManager.Load();

        _keyboardHook = new KeyboardHook();
        _mouseHook = new MouseHook();
        _hotkeyManager = new HotkeyManager();

        _keyboardHook.IsRegisteredHotkey = _hotkeyManager.IsRegisteredHotkey;

        _inputRouter = new InputRouter(_keyboardHook, _mouseHook, _hotkeyManager);
        _inputRouter.ModeChanged += OnRoutingModeChanged;

        _connectionManager = new ConnectionManager(_inputRouter, _appSettings.Security.SharedSecret, _appSettings.Network.HostPort, _appSettings.Network.HostPort - 1);
        _connectionManager.StateChanged += OnConnectionStateChanged;
        _connectionManager.LatencyMeasured += OnLatencyMeasured;

        _trayService = new TrayService("InputBridge Host");
        _trayService.SwitchToHostRequested += () => _inputRouter.SwitchMode(RoutingMode.Local);
        _trayService.SwitchToClientRequested += () => _inputRouter.SwitchMode(RoutingMode.Remote);
        _trayService.DoubleClicked += () =>
        {
            Show();
            WindowState = WindowState.Normal;
        };
        _trayService.ExitRequested += ExitApp;

        BtnLocalMode.Click += (s, e) => _inputRouter.SwitchMode(RoutingMode.Local);
        BtnRemoteMode.Click += (s, e) => _inputRouter.SwitchMode(RoutingMode.Remote);

        Loaded += InitializeApp;
        Closing += (s, e) => 
        {
            e.Cancel = true;
            Hide();
        };
    }

    private void InitializeApp(object sender, RoutedEventArgs e)
    {
        _keyboardHook.Install();
        _mouseHook.Install();
        
        RefreshHotkeys();

        UpdateModeUI(RoutingMode.Local);
        
        TxtTcpPort.Text = _appSettings.Network.HostPort.ToString();
        TxtSecret.Text = _appSettings.Security.SharedSecret;
    }

    private string FormatHotkeyForDisplay(string rawHotkey)
    {
        if (string.IsNullOrEmpty(rawHotkey)) return "";
        return rawHotkey
            .Replace("D1", "1").Replace("D2", "2").Replace("D3", "3")
            .Replace("Escape", "Esc");
    }

    private void RefreshHotkeys()
    {
        _hotkeyManager.ReRegister(_appSettings.Hotkeys.SwitchToHost, _appSettings.Hotkeys.SwitchToClient1, _appSettings.Hotkeys.EmergencyRelease);
        TxtLocalHotkeyDisplay.Text = FormatHotkeyForDisplay(_appSettings.Hotkeys.SwitchToHost);
        TxtRemoteHotkeyDisplay.Text = FormatHotkeyForDisplay(_appSettings.Hotkeys.SwitchToClient1);
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new HotkeySettingsWindow(_settingsManager, _appSettings) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _appSettings = _settingsManager.Load();
            RefreshHotkeys();
        }
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        _connectionManager.Stop();
        _connectionManager.SharedSecret = TxtSecret.Text;
        if (int.TryParse(TxtTcpPort.Text, out int port))
        {
            _connectionManager.TcpPort = port;
            _connectionManager.UdpPort = port - 1; // Normally just -1 of TCP port convention if they don't explicitly want UDP port
        }
        _connectionManager.Start();
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        ExitApp();
    }

    private void OnConnectionStateChanged(ConnectionState state)
    {
        Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case ConnectionState.Disconnected:
                    TxtStatus.Text = "🔴 Disconnected";
                    TxtStatus.Foreground = (SolidColorBrush)FindResource("BrushError");
                    _trayService.UpdateState(TrayIconState.Disconnected, "Host - Disconnected");
                    BtnRemoteMode.IsEnabled = false;
                    break;
                case ConnectionState.Discovering:
                    TxtStatus.Text = "🟡 Waiting for Client...";
                    TxtStatus.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                    _trayService.UpdateState(TrayIconState.Connecting, "Host - Waiting");
                    BtnRemoteMode.IsEnabled = false;
                    break;
                case ConnectionState.Connecting:
                    TxtStatus.Text = "🟡 Connecting...";
                    TxtStatus.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                    BtnRemoteMode.IsEnabled = false;
                    break;
                case ConnectionState.Connected:
                    TxtStatus.Text = "🟢 Connected";
                    TxtStatus.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
                    _trayService.UpdateState(TrayIconState.ConnectedLocal, "Host - Connected");
                    BtnRemoteMode.IsEnabled = true;
                    break;
                case ConnectionState.Reconnecting:
                    TxtStatus.Text = "🟡 Reconnecting...";
                    TxtStatus.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                    _trayService.UpdateState(TrayIconState.Connecting, "Host - Waiting");
                    BtnRemoteMode.IsEnabled = false;
                    break;
            }
        });
    }

    private void OnLatencyMeasured(int latencyMs)
    {
        Dispatcher.Invoke(() =>
        {
            TxtDetails.Text = $"Latency: {latencyMs} ms";
            _trayService.UpdateLatency(latencyMs);
        });
    }

    private void OnRoutingModeChanged(RoutingMode mode)
    {
        Dispatcher.Invoke(() => UpdateModeUI(mode));
    }

    private void UpdateModeUI(RoutingMode mode)
    {
        if (mode == RoutingMode.Local)
        {
            TxtLocalActive.Text = "[ACTIVE]";
            TxtLocalActive.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
            TxtRemoteActive.Text = "[ switch ]";
            TxtRemoteActive.Foreground = (SolidColorBrush)FindResource("BrushTextDim");
            _trayService.UpdateState(TrayIconState.ConnectedLocal, "Host - Local Mode");
        }
        else
        {
            TxtRemoteActive.Text = "[ACTIVE]";
            TxtRemoteActive.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
            TxtLocalActive.Text = "[ switch ]";
            TxtLocalActive.Foreground = (SolidColorBrush)FindResource("BrushTextDim");
            _trayService.UpdateState(TrayIconState.ConnectedRemote, "Host - Remote Mode");
        }
    }



    private void ExitApp()
    {
        _hotkeyManager.Dispose();
        _inputRouter.Dispose();
        _connectionManager.Dispose();
        _trayService.Dispose();
        _keyboardHook.Dispose();
        _mouseHook.Dispose();
        Application.Current.Shutdown();
    }
}
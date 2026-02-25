using System;
using System.Windows;
using System.Windows.Media;
using InputBridge.Client.Services;
using InputBridge.Client.Simulation;
using InputBridge.Shared.UI.Services;

namespace InputBridge.Client;

public partial class MainWindow : Window
{
    private readonly ClientConnectionManager _connectionManager;
    private readonly TrayService _trayService;

    public MainWindow()
    {
        InitializeComponent();

        var keyboard = new KeyboardSimulator();
        var mouse = new MouseSimulator();

        _connectionManager = new ClientConnectionManager(keyboard, mouse);
        _connectionManager.StateChanged += OnConnectionStateChanged;

        _trayService = new TrayService("InputBridge Client");
        _trayService.DoubleClicked += () =>
        {
            Show();
            WindowState = WindowState.Normal;
        };
        _trayService.ExitRequested += () => Application.Current.Shutdown();

        // Don't auto-start. Wait for user to click Connect.
        // Loaded += (s, e) => _connectionManager.Start();
        Closing += (s, e) => 
        {
            // Just hide the window by default instead of closing, unless exiting
            e.Cancel = true;
            Hide();
        };
    }

    private void BtnHide_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        _connectionManager.Stop(); // Stop if running
        _connectionManager.SharedSecret = TxtSecret.Text;
        _connectionManager.Start();
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
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
                    _trayService.UpdateState(TrayIconState.Disconnected, "InputBridge Client - Disconnected");
                    break;
                case ConnectionState.Discovering:
                    TxtStatus.Text = "🟡 Searching for Host...";
                    TxtStatus.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                    _trayService.UpdateState(TrayIconState.Connecting, "InputBridge Client - Searching");
                    break;
                case ConnectionState.Connecting:
                    TxtStatus.Text = "🟡 Connecting...";
                    TxtStatus.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                    _trayService.UpdateState(TrayIconState.Connecting, "InputBridge Client - Connecting");
                    break;
                case ConnectionState.Connected:
                    TxtStatus.Text = "🟢 Connected (Control on Host)";
                    TxtStatus.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
                    _trayService.UpdateState(TrayIconState.ConnectedRemote, "InputBridge Client - Connected");
                    break;
                case ConnectionState.Reconnecting:
                    TxtStatus.Text = "🟡 Reconnecting...";
                    TxtStatus.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                    _trayService.UpdateState(TrayIconState.Connecting, "InputBridge Client - Reconnecting");
                    break;
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _connectionManager.Dispose();
        _trayService.Dispose();
        base.OnClosed(e);
    }
}
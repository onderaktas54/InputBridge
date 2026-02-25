using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;

namespace InputBridge.Shared.UI.Services;

public enum TrayIconState
{
    Disconnected,
    Connecting,
    ConnectedLocal,
    ConnectedRemote
}

public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _notifyIcon;
    private TrayIconState _currentState = TrayIconState.Disconnected;

    public event Action? SwitchToHostRequested;
    public event Action? SwitchToClientRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action? DoubleClicked;

    public TrayService(string appName)
    {
        _notifyIcon = new TaskbarIcon
        {
            ToolTipText = appName,
            // Fallback icon (you should load a real .ico file here)
            Icon = SystemIcons.Application
        };

        _notifyIcon.TrayMouseDoubleClick += (s, e) => DoubleClicked?.Invoke();
        
        BuildContextMenu();
        UpdateState(TrayIconState.Disconnected, "Waiting for connection...");
    }

    private void BuildContextMenu()
    {
        var contextMenu = new ContextMenu();

        var mnuHost = new MenuItem { Header = "Switch to This PC (Local)" };
        mnuHost.Click += (s, e) => SwitchToHostRequested?.Invoke();
        
        var mnuClient = new MenuItem { Header = "Switch to Client PC (Remote)" };
        mnuClient.Click += (s, e) => SwitchToClientRequested?.Invoke();

        var mnuSettings = new MenuItem { Header = "Settings..." };
        mnuSettings.Click += (s, e) => SettingsRequested?.Invoke();

        var mnuExit = new MenuItem { Header = "Exit" };
        mnuExit.Click += (s, e) => ExitRequested?.Invoke();

        contextMenu.Items.Add(mnuHost);
        contextMenu.Items.Add(mnuClient);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(mnuSettings);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(mnuExit);

        _notifyIcon.ContextMenu = contextMenu;
    }

    public void UpdateState(TrayIconState state, string tooltipText)
    {
        _currentState = state;
        _notifyIcon.ToolTipText = tooltipText;
        
        // In a real application, switch _notifyIcon.Icon to respective colored .ico files
        // E.g. Disconnected -> Red, Connecting -> Yellow, ConnectedLocal -> Green, ConnectedRemote -> Blue
    }

    public void UpdateLatency(int latencyMs)
    {
        // Add a menu item for latency or update tooltip
        _notifyIcon.ToolTipText = $"InputBridge ({_currentState})\nLatency: {latencyMs}ms";
    }

    public void ShowNotification(string title, string message)
    {
        _notifyIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}

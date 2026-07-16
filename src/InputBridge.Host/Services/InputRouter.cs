using System;
using System.Threading.Tasks;
using InputBridge.Core.Crypto;
using InputBridge.Core.Network;
using InputBridge.Core.Protocol;
using InputBridge.Host.Hooks;

namespace InputBridge.Host.Services;

public enum RoutingMode
{
    Local,
    Remote
}

public sealed class InputRouter : IDisposable
{
    private RoutingMode _currentMode = RoutingMode.Local;

    private readonly KeyboardHook _keyboard;
    private readonly MouseHook _mouse;
    private readonly HotkeyManager _hotkeys;

    // Transports (we assume they are initialized and connected by ConnectionManager)
    // We can inject interfaces here. In reality, ConnectionManager sets these up.
    private ITransport? _udpTransport;
    private ITransport? _tcpTransport;
    private AesTransport? _crypto;
    private readonly object _disconnectLock = new();

    public event Action<RoutingMode>? ModeChanged;
    public event Action<string>? NotificationRequested;

    public InputRouter(KeyboardHook keyboard, MouseHook mouse, HotkeyManager hotkeys)
    {
        _keyboard = keyboard;
        _mouse = mouse;
        _hotkeys = hotkeys;

        // Register event handlers
        _keyboard.KeyEvent += OnKeyEvent;
        _mouse.MouseEvent += OnMouseEvent;

        _hotkeys.SwitchToHost += () => SwitchMode(RoutingMode.Local);
        _hotkeys.SwitchToClient += (_) => SwitchMode(RoutingMode.Remote);
        _hotkeys.EmergencyRelease += () => SwitchMode(RoutingMode.Local);
    }

    public void SetTransports(ITransport udpTransport, ITransport tcpTransport, AesTransport crypto)
    {
        _udpTransport = udpTransport;
        _tcpTransport = tcpTransport;
        _crypto = crypto;
    }

    public void SwitchMode(RoutingMode targetMode)
    {
        if (_currentMode == targetMode) return;

        if (targetMode == RoutingMode.Remote && (_tcpTransport == null || !_tcpTransport.IsConnected))
        {
            NotificationRequested?.Invoke(" No Connection! Cannot switch to client.");
            return;
        }

        _currentMode = targetMode;

        bool isRemote = (_currentMode == RoutingMode.Remote);
        _keyboard.SetRemoteMode(isRemote);
        _mouse.SetRemoteMode(isRemote);

        if (isRemote)
        {
            NotificationRequested?.Invoke(" Control: Client PC");
        }
        else
        {
            NotificationRequested?.Invoke(" Control: This PC");
        }

        ModeChanged?.Invoke(_currentMode);

        // Notify client of mode switch
        SendSwitchNotifyPacket(_currentMode);
    }

    private void SendSwitchNotifyPacket(RoutingMode newMode)
    {
        if (_tcpTransport == null || _crypto == null || !_tcpTransport.IsConnected) return;

        var packet = new InputPacket
        {
            Version = 1,
            Type = InputType.SwitchNotify,
            Data1 = newMode == RoutingMode.Remote ? 1 : 0, // 1 for Remote, 0 for Local
            SequenceNumber = 0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _ = SendTcpPacketAsync(packet);
    }

    private void OnKeyEvent(InputPacket packet)
    {
        if (_currentMode == RoutingMode.Remote && _tcpTransport != null && _crypto != null && _tcpTransport.IsConnected)
        {
            _ = SendTcpPacketAsync(packet);
        }
    }

    private void OnMouseEvent(InputPacket packet)
    {
        if (_currentMode == RoutingMode.Remote && _udpTransport != null && _crypto != null && _udpTransport.IsConnected)
        {
            _ = SendUdpPacketAsync(packet);
        }
    }

    private async Task SendTcpPacketAsync(InputPacket packet)
    {
        ITransport? transport = null;
        try
        {
            transport = _tcpTransport;
            AesTransport? crypto = _crypto;
            if (transport == null || crypto == null) return;

            byte[] encrypted = crypto.Encrypt(PacketSerializer.Serialize(packet));
            await transport.SendAsync(encrypted);
        }
        catch
        {
            if (transport != null) HandleSendFailure(transport);
        }
    }

    private async Task SendUdpPacketAsync(InputPacket packet)
    {
        ITransport? transport = null;
        try
        {
            transport = _udpTransport;
            AesTransport? crypto = _crypto;
            if (transport == null || crypto == null) return;

            byte[] encrypted = crypto.Encrypt(PacketSerializer.Serialize(packet));
            await transport.SendAsync(encrypted);
        }
        catch
        {
            if (transport != null) HandleSendFailure(transport);
        }
    }

    private void HandleSendFailure(ITransport failedTransport)
    {
        lock (_disconnectLock)
        {
            if (!ReferenceEquals(_tcpTransport, failedTransport) &&
                !ReferenceEquals(_udpTransport, failedTransport)) return;
            HandleDisconnectLocked();
        }
    }

    public void HandleDisconnect()
    {
        lock (_disconnectLock)
        {
            HandleDisconnectLocked();
        }
    }

    private void HandleDisconnectLocked()
    {
        if (_currentMode == RoutingMode.Remote)
        {
            _currentMode = RoutingMode.Local;
            _keyboard.SetRemoteMode(false);
            _mouse.SetRemoteMode(false);
            ModeChanged?.Invoke(RoutingMode.Local);
        }

        try { _udpTransport?.Dispose(); } catch { }
        try { _tcpTransport?.Dispose(); } catch { }
        try { _crypto?.Dispose(); } catch { }
        _udpTransport = null;
        _tcpTransport = null;
        _crypto = null;
    }

    public void Dispose()
    {
        _keyboard.KeyEvent -= OnKeyEvent;
        _mouse.MouseEvent -= OnMouseEvent;
    }
}

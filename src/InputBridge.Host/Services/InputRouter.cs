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
        // Don't switch if we can't send data to remote
        if (targetMode == RoutingMode.Remote && (_tcpTransport == null || !_tcpTransport.IsConnected))
        {
            NotificationRequested?.Invoke(" Bağlantı Yok! İstemciye geçilemiyor.");
            return;
        }

        _currentMode = targetMode;

        bool isRemote = (_currentMode == RoutingMode.Remote);
        _keyboard.SetRemoteMode(isRemote);
        _mouse.SetRemoteMode(isRemote);
        
        if (isRemote)
        {
            NotificationRequested?.Invoke(" Kontrol: Client PC");
        }
        else
        {
            NotificationRequested?.Invoke(" Kontrol: Bu PC");
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

        try
        {
            var rawData = PacketSerializer.Serialize(packet);
            var encrypted = _crypto.Encrypt(rawData);
            _ = _tcpTransport.SendAsync(encrypted);
        }
        catch (Exception)
        {
            // If TCP fails, we should drop back to local mode immediately
            SwitchMode(RoutingMode.Local);
        }
    }

    private void OnKeyEvent(InputPacket packet)
    {
        if (_currentMode == RoutingMode.Remote && _tcpTransport != null && _crypto != null && _tcpTransport.IsConnected)
        {
            try
            {
                var rawData = PacketSerializer.Serialize(packet);
                var encrypted = _crypto.Encrypt(rawData);
                _ = _tcpTransport.SendAsync(encrypted); // TCP for keys to prevent drops
            }
            catch
            {
                SwitchMode(RoutingMode.Local);
            }
        }
    }

    private void OnMouseEvent(InputPacket packet)
    {
        if (_currentMode == RoutingMode.Remote && _udpTransport != null && _crypto != null && _udpTransport.IsConnected)
        {
            try
            {
                var rawData = PacketSerializer.Serialize(packet);
                var encrypted = _crypto.Encrypt(rawData);
                _ = _udpTransport.SendAsync(encrypted); // UDP for fast mouse movements
            }
            catch
            {
                // Unlikely to catch exception on UDP send unless socket disposed
            }
        }
    }
    
    public void HandleDisconnect()
    {
        if (_currentMode == RoutingMode.Remote)
        {
            SwitchMode(RoutingMode.Local);
        }
        
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

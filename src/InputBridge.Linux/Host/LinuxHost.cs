using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using InputBridge.Core.Crypto;
using InputBridge.Core.Network;
using InputBridge.Core.Protocol;
using InputBridge.Linux.Native;
using Serilog;

namespace InputBridge.Linux.Host;

/// <summary>
/// Headless Linux host: broadcasts itself on the LAN, accepts a client (Windows or
/// Linux), and streams locally-captured keyboard/mouse events to it. Capture is via
/// evdev; toggle forwarding on/off with <c>Ctrl+Alt+S</c>, emergency release with
/// <c>Ctrl+Alt+Esc</c>. While forwarding, input devices are exclusively grabbed so
/// they no longer act on this machine.
/// </summary>
internal sealed class LinuxHost
{
    private const ushort KeyEsc = 1;
    private const ushort KeyS = 31;
    private const ushort KeyLeftCtrl = 29, KeyRightCtrl = 97;
    private const ushort KeyLeftAlt = 56, KeyRightAlt = 100;

    private readonly string _sharedSecret;
    private readonly int _port;
    private readonly DiscoveryService _discovery = new();
    private readonly HandshakeManager _handshake = new();

    private readonly HashSet<ushort> _pressed = new();
    private readonly object _pressedLock = new();
    private readonly List<EvdevDevice> _devices = new();
    private Channel<(InputPacket packet, bool udp)>? _outbox;
    private volatile bool _forwarding;
    private int _udpSeq;

    public LinuxHost(string sharedSecret, int port)
    {
        _sharedSecret = sharedSecret;
        _port = port;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _ = _discovery.StartBroadcasting(Environment.MachineName, _port, ct);

        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        Log.Information("[Host] Listening on port {Port}. Waiting for a client…", _port);
        Log.Information("[Host] Toggle forwarding: Ctrl+Alt+S   |   Emergency release: Ctrl+Alt+Esc");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(ct);
                string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                Log.Information("[Host] Client connected: {Ip}", clientIp);

                using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                handshakeCts.CancelAfter(TimeSpan.FromSeconds(5));
                SessionInfo? session;
                try
                {
                    session = await _handshake.PerformAsHost(client, _sharedSecret, handshakeCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Log.Warning("[Host] Handshake timed out. Dropping client.");
                    continue;
                }
                if (session == null)
                {
                    Log.Warning("[Host] Handshake failed (wrong secret?). Dropping client.");
                    continue;
                }

                await ServeClientAsync(client, clientIp, session, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Stop();
            StopCapture();
        }
    }

    private async Task ServeClientAsync(TcpClient client, string clientIp, SessionInfo session, CancellationToken ct)
    {
        using var tcp = new TcpTransport(client);
        using var udp = new UdpTransport(_port - 1, clientIp, _port - 1);
        using var crypto = new AesTransport(session.AesKey);
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _outbox = Channel.CreateUnbounded<(InputPacket, bool)>(
            new UnboundedChannelOptions { SingleReader = true });
        _forwarding = false;
        _udpSeq = 0;

        StartCapture();

        var sender = SenderLoop(tcp, udp, crypto, sessionCts.Token);
        int missedHeartbeats = 0;
        var heartbeat = HeartbeatLoop(
            tcp,
            crypto,
            () => Interlocked.Increment(ref missedHeartbeats),
            () => Volatile.Read(ref missedHeartbeats),
            sessionCts.Token);
        var reader = TcpDrainLoop(
            tcp,
            crypto,
            () => Interlocked.Exchange(ref missedHeartbeats, 0),
            sessionCts.Token);

        Log.Information("[Host] ✓ Client authenticated. Press Ctrl+Alt+S to start forwarding.");
        await Task.WhenAny(sender, heartbeat, reader);

        sessionCts.Cancel();
        StopCapture();
        _outbox.Writer.TryComplete();
        try
        {
            await Task.WhenAll(sender, heartbeat, reader).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        Log.Warning("[Host] Client session ended. Waiting for a new client…");
    }

    // ---- capture ----

    private void StartCapture()
    {
        _devices.AddRange(EvdevDevice.EnumerateKeyboardsAndMice());
        if (_devices.Count == 0)
        {
            Log.Error("[Host] No input devices readable. Run as root (sudo) to access /dev/input.");
            return;
        }

        foreach (var dev in _devices)
        {
            dev.OnEvent += e => OnDeviceEvent(dev, e);
            dev.Start();
        }
        Log.Information("[Host] Capturing {Count} device(s).", _devices.Count);
    }

    private void StopCapture()
    {
        foreach (var dev in _devices) dev.Dispose();
        _devices.Clear();
        lock (_pressedLock) _pressed.Clear();
    }

    private void OnDeviceEvent(EvdevDevice dev, EvdevDevice.Event e)
    {
        if (e.Type == NativeMethods.EV_KEY)
        {
            TrackModifier(e.Code, e.Value);

            // Hotkeys are handled locally and never forwarded.
            if (e.Value == 1 && CtrlAltHeld())
            {
                if (e.Code == KeyS) { ToggleForwarding(); return; }
                if (e.Code == KeyEsc) { SetForwarding(false); return; }
            }
        }

        if (!_forwarding) return;

        InputPacket? packet = Translate(e, out bool udp);
        if (packet.HasValue) Enqueue(packet.Value, udp);
    }

    private InputPacket? Translate(EvdevDevice.Event e, out bool udp)
    {
        udp = false;
        switch (e.Type)
        {
            case NativeMethods.EV_KEY:
                if (e.Value == 2) return null; // ignore autorepeat; remote repeats on its own
                int buttonId = MouseButtonId(e.Code);
                if (buttonId >= 0)
                {
                    return Make(e.Value == 1 ? InputType.MouseButtonDown : InputType.MouseButtonUp, buttonId, 0);
                }
                int vk = KeyMap.EvdevToVk(e.Code);
                if (vk < 0) return null;
                return Make(e.Value == 1 ? InputType.KeyDown : InputType.KeyUp, vk, 0);

            case NativeMethods.EV_REL:
                udp = true;
                if (e.Code == NativeMethods.REL_X) return Make(InputType.MouseMove, e.Value, 0);
                if (e.Code == NativeMethods.REL_Y) return Make(InputType.MouseMove, 0, e.Value);
                if (e.Code == NativeMethods.REL_WHEEL) { udp = true; return Make(InputType.MouseScroll, e.Value * 120, 0); }
                return null;

            default:
                return null;
        }
    }

    private static int MouseButtonId(ushort code) => code switch
    {
        NativeMethods.BTN_LEFT => 0,
        NativeMethods.BTN_RIGHT => 1,
        NativeMethods.BTN_MIDDLE => 2,
        NativeMethods.BTN_SIDE => 3,
        NativeMethods.BTN_EXTRA => 4,
        _ => -1,
    };

    private static InputPacket Make(InputType type, int data1, int data2) => new()
    {
        Version = 1,
        Type = type,
        Flags = ModifierFlags.None,
        Data1 = data1,
        Data2 = data2,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };

    private void Enqueue(InputPacket packet, bool udp)
    {
        if (udp) packet.SequenceNumber = unchecked((uint)Interlocked.Increment(ref _udpSeq));
        _outbox?.Writer.TryWrite((packet, udp));
    }

    // ---- modifier / hotkey state ----

    private void TrackModifier(ushort code, int value)
    {
        bool isMod = code is KeyLeftCtrl or KeyRightCtrl or KeyLeftAlt or KeyRightAlt;
        if (!isMod) return;
        lock (_pressedLock)
        {
            if (value == 1) _pressed.Add(code);
            else if (value == 0) _pressed.Remove(code);
        }
    }

    private bool CtrlAltHeld()
    {
        lock (_pressedLock)
        {
            bool ctrl = _pressed.Contains(KeyLeftCtrl) || _pressed.Contains(KeyRightCtrl);
            bool alt = _pressed.Contains(KeyLeftAlt) || _pressed.Contains(KeyRightAlt);
            return ctrl && alt;
        }
    }

    private void ToggleForwarding() => SetForwarding(!_forwarding);

    private void SetForwarding(bool on)
    {
        if (_forwarding == on) return;

        if (on)
        {
            foreach (var dev in _devices)
            {
                if (dev.TryGrab()) continue;

                foreach (var grabbed in _devices) grabbed.Ungrab();
                Log.Error("[Host] Could not exclusively grab {Device}; forwarding stays OFF.", dev.Path);
                return;
            }

            _forwarding = true;
            Log.Information("[Host] ▶ Forwarding ON — input now goes to the CLIENT.");
        }
        else
        {
            _forwarding = false;
            foreach (var dev in _devices) dev.Ungrab();
            Log.Information("[Host] ⏹ Forwarding OFF — input back on THIS machine.");
            // Tell the client to release everything it is holding.
            Enqueue(Make(InputType.SwitchNotify, 0, 0), udp: false);
        }
    }

    // ---- network loops ----

    private async Task SenderLoop(ITransport tcp, ITransport udp, AesTransport crypto, CancellationToken ct)
    {
        var reader = _outbox!.Reader;
        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var item))
                {
                    byte[] payload = crypto.Encrypt(PacketSerializer.Serialize(item.packet));
                    ITransport t = item.udp ? udp : tcp;
                    await t.SendAsync(payload, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug(ex, "[Host] sender stopped"); }
    }

    private static async Task HeartbeatLoop(
        ITransport tcp,
        AesTransport crypto,
        Action markSent,
        Func<int> missedCount,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);
                markSent();
                if (missedCount() >= 5)
                {
                    Log.Warning("[Host] Five heartbeat replies missed; closing the session.");
                    return;
                }

                var hb = Make(InputType.Heartbeat, 0, 0);
                byte[] payload = crypto.Encrypt(PacketSerializer.Serialize(hb));
                await tcp.SendAsync(payload, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* peer gone */ }
    }

    private static async Task TcpDrainLoop(
        ITransport tcp,
        AesTransport crypto,
        Action heartbeatReceived,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && tcp.IsConnected)
            {
                byte[] encrypted = await tcp.ReceiveAsync(ct);
                byte[] decrypted = crypto.Decrypt(encrypted);
                InputPacket packet = PacketSerializer.Deserialize(decrypted);
                if (packet.Type == InputType.Heartbeat) heartbeatReceived();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }
}

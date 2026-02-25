using System;
using System.Threading;
using System.Threading.Tasks;
using InputBridge.Client.Simulation;
using InputBridge.Core.Crypto;
using InputBridge.Core.Network;
using InputBridge.Core.Protocol;

namespace InputBridge.Client.Services;

public sealed class PacketListener : IDisposable
{
    private readonly KeyboardSimulator _keyboard;
    private readonly MouseSimulator _mouse;
    private readonly ITransport _udpTransport;
    private readonly ITransport _tcpTransport;
    private readonly AesTransport _crypto;

    private CancellationTokenSource? _cts;
    private uint _lastUdpSeq = 0;

    public event Action<int>? LatencyMeasured;
    public event Action<int>? SwitchModeRequested; // 0 for Local (Client control), 1 for Remote (Host control)

    public PacketListener(
        KeyboardSimulator keyboard, 
        MouseSimulator mouse, 
        ITransport udpTransport, 
        ITransport tcpTransport, 
        AesTransport crypto)
    {
        _keyboard = keyboard;
        _mouse = mouse;
        _udpTransport = udpTransport;
        _tcpTransport = tcpTransport;
        _crypto = crypto;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = UdpListenLoop(_cts.Token);
        _ = TcpListenLoop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task UdpListenLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _udpTransport.IsConnected)
            {
                var encrypted = await _udpTransport.ReceiveAsync(ct);
                var decrypted = _crypto.Decrypt(encrypted);
                var packet = PacketSerializer.Deserialize(decrypted);

                if (packet.SequenceNumber <= _lastUdpSeq && packet.SequenceNumber != 0) 
                    continue;

                _lastUdpSeq = packet.SequenceNumber;
                MeasureLatency(packet.Timestamp);

                DispatchPacket(packet);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // UDP error
        }
    }

    private async Task TcpListenLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _tcpTransport.IsConnected)
            {
                var encrypted = await _tcpTransport.ReceiveAsync(ct);
                var decrypted = _crypto.Decrypt(encrypted);
                var packet = PacketSerializer.Deserialize(decrypted);

                MeasureLatency(packet.Timestamp);

                if (packet.Type == InputType.Heartbeat)
                {
                    // Echo back the heartbeat over TCP
                    var replyEncrypted = _crypto.Encrypt(PacketSerializer.Serialize(packet));
                    _ = _tcpTransport.SendAsync(replyEncrypted, ct);
                }
                else
                {
                    DispatchPacket(packet);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // TCP disconnected
        }
    }

    private void MeasureLatency(long packetTimestamp)
    {
        // Simple 1-way delay estimate (assuming clocks somewhat synced)
        // Accurate RTT relies on the Heartbeat packet handled in Host
        var delay = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - packetTimestamp;
        if (delay > 0 && delay < 1000)
        {
            LatencyMeasured?.Invoke((int)delay);
        }
    }

    private void DispatchPacket(InputPacket packet)
    {
        switch (packet.Type)
        {
            case InputType.KeyDown:
                _keyboard.SimulateKeyDown(packet.Data1);
                break;
            case InputType.KeyUp:
                _keyboard.SimulateKeyUp(packet.Data1);
                break;
            case InputType.MouseMove:
                // For DPI awareness, we might scale Data1 and Data2 here
                _mouse.SimulateMouseMove(packet.Data1, packet.Data2);
                break;
            case InputType.MouseButtonDown:
                _mouse.SimulateMouseButton(packet.Data1, isDown: true);
                break;
            case InputType.MouseButtonUp:
                _mouse.SimulateMouseButton(packet.Data1, isDown: false);
                break;
            case InputType.MouseScroll:
                _mouse.SimulateScroll(packet.Data1);
                break;
            case InputType.SwitchNotify:
                SwitchModeRequested?.Invoke(packet.Data1);
                break;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

using System.Net.Sockets;
using InputBridge.Core.Crypto;
using InputBridge.Core.Network;
using InputBridge.Core.Protocol;
using InputBridge.Linux.Native;
using Serilog;

namespace InputBridge.Linux.Client;

/// <summary>
/// Headless Linux client: discovers (or directly connects to) an InputBridge Host
/// — Windows or Linux — performs the shared handshake, then injects every received
/// input event locally via <c>/dev/uinput</c>. This is the Linux port of the Windows
/// client's ClientConnectionManager + PacketListener, speaking the identical protocol.
/// </summary>
internal sealed class LinuxClient
{
    private readonly IInputInjector _injector;
    private readonly string _sharedSecret;
    private readonly string? _manualHost;
    private readonly int _manualPort;
    private readonly DiscoveryService _discovery = new();
    private readonly HandshakeManager _handshake = new();
    private readonly UdpSequenceTracker _udpSequence = new();
    private readonly Action<LinuxConnectionStatus, string>? _statusChanged;

    public LinuxClient(
        IInputInjector injector,
        string sharedSecret,
        string? manualHost,
        int manualPort,
        Action<LinuxConnectionStatus, string>? statusChanged = null)
    {
        _injector = injector;
        _sharedSecret = sharedSecret;
        _manualHost = manualHost;
        _manualPort = manualPort;
        _statusChanged = statusChanged;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (ip, port) = await ResolveHostAsync(ct);
                if (ip == null)
                {
                    Report(LinuxConnectionStatus.Discovering, "Host bulunamadı; yerel ağ yeniden taranıyor…");
                    Log.Information("[Client] No host found, retrying…");
                    continue;
                }

                Report(LinuxConnectionStatus.Connecting, $"{ip}:{port} adresine bağlanılıyor…");
                Log.Information("[Client] Connecting to {Ip}:{Port}", ip, port);
                using var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(ip, port, ct);

                using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                handshakeCts.CancelAfter(TimeSpan.FromSeconds(5));
                var session = await _handshake.PerformAsClient(tcpClient, _sharedSecret, handshakeCts.Token);
                if (session == null)
                {
                    Report(LinuxConnectionStatus.Error, "Kimlik doğrulama başarısız. Secret Key eşleşmiyor olabilir.");
                    Log.Warning("[Client] Handshake failed (wrong secret?). Retrying…");
                    await Task.Delay(1000, ct);
                    continue;
                }

                Report(LinuxConnectionStatus.Connected, $"{session.RemoteHostname} cihazına güvenli bağlantı kuruldu.");
                Log.Information("[Client] ✓ Connected & authenticated to {Host}", session.RemoteHostname);

                using var tcp = new TcpTransport(tcpClient);
                using var udp = new UdpTransport(port - 1, ip, port - 1);
                using var crypto = new AesTransport(session.AesKey);
                _udpSequence.Reset();

                using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var udpTask = PacketReceiveLoop.RunAsync(
                    udp,
                    crypto,
                    isUdp: true,
                    _udpSequence,
                    Dispatch,
                    loopCts.Token);
                var tcpTask = PacketReceiveLoop.RunAsync(
                    tcp,
                    crypto,
                    isUdp: false,
                    _udpSequence,
                    Dispatch,
                    loopCts.Token);

                // TCP carries the heartbeat and defines session health. UDP is deliberately
                // best-effort; a transient UDP receive/decrypt error must not end the session.
                await tcpTask;
                loopCts.Cancel();
                try
                {
                    await Task.WhenAll(udpTask, tcpTask).WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
                _injector.ReleaseAll();
                Report(LinuxConnectionStatus.Reconnecting, "Bağlantı kesildi; yeniden deneniyor…");
                Log.Warning("[Client] ⚠ Connection lost — reconnecting…");
                await Task.Delay(1500, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                Report(LinuxConnectionStatus.Reconnecting, "Bağlantı zaman aşımına uğradı; yeniden deneniyor…");
                Log.Warning("[Client] Connection or handshake timed out; retrying…");
                _injector.ReleaseAll();
                await SafeDelay(1000, ct);
            }
            catch (Exception ex)
            {
                Report(LinuxConnectionStatus.Error, ex.Message);
                Log.Error(ex, "[Client] loop error");
                _injector.ReleaseAll();
                await SafeDelay(2000, ct);
            }
        }

        _injector.ReleaseAll();
        Report(LinuxConnectionStatus.Stopped, "Client durduruldu.");
    }

    private async Task<(string? ip, int port)> ResolveHostAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_manualHost))
        {
            return (_manualHost, _manualPort);
        }

        Report(LinuxConnectionStatus.Discovering, "Yerel ağda Windows/Linux Host aranıyor…");
        Log.Information("[Client] Discovering hosts on the LAN…");
        var hosts = await _discovery.ListenForHosts(TimeSpan.FromSeconds(5), ct);
        if (hosts.Count == 0) return (null, 0);
        var host = hosts[0];
        return (host.IpAddress, host.Port);
    }

    private void Dispatch(InputPacket packet)
    {
        switch (packet.Type)
        {
            case InputType.KeyDown: _injector.KeyDown(packet.Data1); break;
            case InputType.KeyUp: _injector.KeyUp(packet.Data1); break;
            case InputType.MouseMove: _injector.MouseMove(packet.Data1, packet.Data2); break;
            case InputType.MouseButtonDown: _injector.MouseButton(packet.Data1, isDown: true); break;
            case InputType.MouseButtonUp: _injector.MouseButton(packet.Data1, isDown: false); break;
            case InputType.MouseScroll: _injector.Scroll(packet.Data1); break;
            case InputType.SwitchNotify:
                if (packet.Data1 == 0) _injector.ReleaseAll();
                break;
        }
    }

    private static async Task SafeDelay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
    }

    private void Report(LinuxConnectionStatus status, string message) =>
        _statusChanged?.Invoke(status, message);
}

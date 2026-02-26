using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using InputBridge.Client.Simulation;
using InputBridge.Core.Crypto;
using InputBridge.Core.Network;
using InputBridge.Core.Protocol;

namespace InputBridge.Client.Services;

public enum ConnectionState
{
    Disconnected,
    Discovering,
    Connecting,
    Connected,
    Reconnecting
}

public sealed class ClientConnectionManager : IDisposable
{
    private ConnectionState _state = ConnectionState.Disconnected;
    public ConnectionState State 
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                Log.Information("[Client] State: {OldState} → {NewState}", _state, value);
                _state = value;
                StateChanged?.Invoke(_state);
            }
        }
    }

    public event Action<ConnectionState>? StateChanged;

    private readonly KeyboardSimulator _keyboard;
    private readonly MouseSimulator _mouse;
    public string SharedSecret { get; set; }
    private readonly DiscoveryService _discovery;
    private readonly HandshakeManager _handshake;

    private CancellationTokenSource? _mainCts;
    private PacketListener? _listener;

    public ClientConnectionManager(KeyboardSimulator keyboard, MouseSimulator mouse, string sharedSecret = "default_secret")
    {
        _keyboard = keyboard;
        _mouse = mouse;
        SharedSecret = sharedSecret;
        _discovery = new DiscoveryService();
        _handshake = new HandshakeManager();
    }

    public void Start()
    {
        if (State != ConnectionState.Disconnected && State != ConnectionState.Reconnecting) return;
        _mainCts = new CancellationTokenSource();
        _ = RunClientLoopAsync(_mainCts.Token);
    }

    public void Stop()
    {
        _mainCts?.Cancel();
        _listener?.Stop();
        try { _keyboard.ReleaseAllKeys(); } catch { }
        try { _keyboard.ReleaseModifierKeys(); } catch { }
        State = ConnectionState.Disconnected;
    }

    private async Task RunClientLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                State = ConnectionState.Discovering;
                var hosts = await _discovery.ListenForHosts(TimeSpan.FromSeconds(5), ct);
                
                if (hosts.Count == 0 || ct.IsCancellationRequested) continue;
                
                var host = hosts[0]; // Connect to the first discovered host
                State = ConnectionState.Connecting;

                var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(host.IpAddress, host.Port, ct);

                var sessionInfo = await _handshake.PerformAsClient(tcpClient, SharedSecret);
                if (sessionInfo == null)
                {
                    await Task.Delay(1000, ct);
                    continue; 
                }

                State = ConnectionState.Connected;

                var tcpTransport = new TcpTransport(tcpClient);
                var udpTransport = new UdpTransport(host.Port - 1, host.IpAddress, host.Port - 1);
                var crypto = new AesTransport(sessionInfo.AesKey);

                _listener = new PacketListener(_keyboard, _mouse, udpTransport, tcpTransport, crypto);
                _listener.Start();

                // Wait while connected
                while (!ct.IsCancellationRequested && tcpTransport.IsConnected)
                {
                    Log.Debug("[Client] TCP IsConnected check: {Status}", tcpTransport.IsConnected);
                    await Task.Delay(1000, ct);
                }

                Log.Warning("[Client] ⚠ Connection lost. TCP IsConnected = false");
                // If loop exits, connection is lost.
                _listener.Stop();
                _listener.Dispose();
                udpTransport.Dispose();
                tcpTransport.Dispose();
                tcpClient.Dispose(); // explicit dispose immediately
                State = ConnectionState.Reconnecting;
                try { await Task.Delay(2000, ct); } catch { }
            }
            catch (OperationCanceledException)
            {
                break; // Graceful exit
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Client] RunClientLoopAsync error");
                _listener?.Stop();
                try { _keyboard.ReleaseAllKeys(); } catch { }
                try { _keyboard.ReleaseModifierKeys(); } catch { }
                State = ConnectionState.Reconnecting;
                try { await Task.Delay(2000, ct); } catch { }
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

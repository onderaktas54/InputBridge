using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using InputBridge.Core.Crypto;
using InputBridge.Core.Network;
using InputBridge.Core.Protocol;
using InputBridge.Host.Services;

namespace InputBridge.Host.Services;

public enum ConnectionState
{
    Disconnected,
    Discovering,
    Connecting,
    Connected,
    Reconnecting
}

public sealed class ConnectionManager : IDisposable
{
    private ConnectionState _state = ConnectionState.Disconnected;
    public ConnectionState State 
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(_state);
            }
        }
    }

    public event Action<ConnectionState>? StateChanged;
    public event Action<int>? LatencyMeasured;

    private readonly InputRouter _router;
    public string SharedSecret { get; set; }
    public int TcpPort { get; set; }
    public int UdpPort { get; set; }
    private readonly DiscoveryService _discovery;
    private readonly HandshakeManager _handshake;

    private CancellationTokenSource? _mainCts;
    private TcpListener? _tcpListener;

    public ConnectionManager(InputRouter router, string sharedSecret = "default_secret", int tcpPort = 7201, int udpPort = 7200)
    {
        _router = router;
        SharedSecret = sharedSecret;
        TcpPort = tcpPort;
        UdpPort = udpPort;
        _discovery = new DiscoveryService();
        _handshake = new HandshakeManager();
    }

    public void Start()
    {
        if (State != ConnectionState.Disconnected) return;
        
        _mainCts = new CancellationTokenSource();
        _ = RunHostLoopAsync(_mainCts.Token);
    }

    public void Stop()
    {
        _mainCts?.Cancel();
        _tcpListener?.Stop();
        State = ConnectionState.Disconnected;
    }

    private async Task RunHostLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                State = ConnectionState.Discovering;
                using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                
                // 1. Broadcaster (background)
                var broadcastTask = _discovery.StartBroadcasting(Environment.MachineName, TcpPort, loopCts.Token);

                // 2. TCP Listener
                _tcpListener = new TcpListener(IPAddress.Any, TcpPort);
                _tcpListener.Start();

                TcpClient client;
                try
                {
                    client = await _tcpListener.AcceptTcpClientAsync(loopCts.Token);
                }
                finally
                {
                    _tcpListener.Stop();
                    loopCts.Cancel(); // Stop broadcasting once a client connects
                }

                State = ConnectionState.Connecting;

                // 3. Handshake
                // Set short timeout for handshake
                using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                handshakeCts.CancelAfter(TimeSpan.FromSeconds(5));
                
                // Note: HandshakeManager currently uses synchronous stream operations underneath in our MVP,
                // but its public interface returns a Task. We rely on the client to cooperate.
                var sessionInfo = await _handshake.PerformAsHost(client, SharedSecret);

                if (sessionInfo == null)
                {
                    client.Dispose();
                    await Task.Delay(1000, ct); // Wait before retrying
                    continue;
                }

                State = ConnectionState.Connected;

                // 4. Setup Transports
                var tcpTransport = new TcpTransport(client);
                
                // For UDP, Host uses local port 0 and sends to ClientIP:UdpPort
                var clientIpStr = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                var udpTransport = new UdpTransport(0, clientIpStr, UdpPort);

                var crypto = new AesTransport(sessionInfo.AesKey);
                
                _router.SetTransports(udpTransport, tcpTransport, crypto);

                // 5. Keep-Alive / Heartbeat loop
                await RunHeartbeatLoopAsync(tcpTransport, crypto, ct);

                // If loop exits (disconnect), cleanup and loop will repeat (Reconnecting)
                _router.HandleDisconnect();
                State = ConnectionState.Reconnecting;
                await Task.Delay(2000, ct); // Wait before discovering again
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                _router.HandleDisconnect();
                State = ConnectionState.Reconnecting;
                try { await Task.Delay(2000, ct); } catch { }
            }
        }
    }

    private async Task RunHeartbeatLoopAsync(ITransport tcpTransport, AesTransport crypto, CancellationToken ct)
    {
        uint sequence = 1;
        int missedHeartbeats = 0;

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Read loop task to catch heartbeat replies
        var readTask = Task.Run(async () => 
        {
            try
            {
                while (!heartbeatCts.Token.IsCancellationRequested)
                {
                    var encryptedData = await tcpTransport.ReceiveAsync(heartbeatCts.Token);
                    var rawData = crypto.Decrypt(encryptedData);
                    var packet = PacketSerializer.Deserialize(rawData);

                    if (packet.Type == InputType.Heartbeat)
                    {
                        Interlocked.Exchange(ref missedHeartbeats, 0);
                        var rtt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - packet.Timestamp;
                        LatencyMeasured?.Invoke((int)(rtt / 2)); // Round Trip Time / 2 = One-way Latency
                    }
                }
            }
            catch
            {
                // Disconnected
            }
        }, heartbeatCts.Token);

        try
        {
            while (!ct.IsCancellationRequested && tcpTransport.IsConnected)
            {
                if (Volatile.Read(ref missedHeartbeats) >= 5)
                {
                    // Disconnect
                    break;
                }

                var heartbeat = new InputPacket
                {
                    Version = 1,
                    Type = InputType.Heartbeat,
                    SequenceNumber = sequence++,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var encData = crypto.Encrypt(PacketSerializer.Serialize(heartbeat));
                await tcpTransport.SendAsync(encData, ct);
                
                Interlocked.Increment(ref missedHeartbeats);
                await Task.Delay(2000, ct);
            }
        }
        catch
        {
            // Socket exception means disconnect
        }
        finally
        {
            heartbeatCts.Cancel();
            await Task.WhenAny(readTask, Task.Delay(500, default)); // wait gracefully
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

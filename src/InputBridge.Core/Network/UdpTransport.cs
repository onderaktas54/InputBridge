using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace InputBridge.Core.Network;

public sealed class UdpTransport : ITransport
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _remoteEndpoint;

    public UdpTransport(int localPort, string remoteIp, int remotePort)
    {
        _client = new UdpClient(localPort);
        _client.Client.ReceiveBufferSize = 65536; // Prevent OS buffer from filling up
        _remoteEndpoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
    }

    public UdpTransport(int localPort)
    {
        _client = new UdpClient(localPort);
        _client.Client.ReceiveBufferSize = 65536;
        _remoteEndpoint = new IPEndPoint(IPAddress.Any, 0); // Will be set on receive or not used for sending
    }

    public UdpTransport(string remoteIp, int remotePort)
    {
        _client = new UdpClient(); // Random local port
        _client.Client.ReceiveBufferSize = 65536;
        _remoteEndpoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
    }

    public bool IsConnected => true; // UDP is connectionless

    public async ValueTask SendAsync(byte[] data, CancellationToken ct = default)
    {
        await _client.SendAsync(data, data.Length, _remoteEndpoint).ConfigureAwait(false);
    }

    public async ValueTask<byte[]> ReceiveAsync(CancellationToken ct = default)
    {
        var result = await _client.ReceiveAsync(ct).ConfigureAwait(false);
        return result.Buffer;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

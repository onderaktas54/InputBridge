using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace InputBridge.Core.Network;

public sealed class TcpTransport : ITransport
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    public TcpTransport(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true; // Disable Nagle's algorithm for minimum latency
        _client.ReceiveBufferSize = 65536;
        _client.SendBufferSize = 65536;
        _stream = _client.GetStream();
    }

    public bool IsConnected => _client.Connected;

    public async ValueTask SendAsync(byte[] data, CancellationToken ct = default)
    {
        // 4-byte length prefix
        byte[] lenBytes = BitConverter.GetBytes(data.Length);
        await _stream.WriteAsync(lenBytes, ct).ConfigureAwait(false);
        await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<byte[]> ReceiveAsync(CancellationToken ct = default)
    {
        byte[] lenBytes = new byte[4];
        
        // ReadExactly / ReadAtLeast are safer here, but let's implement a manual loop for Net Standard compatibility or simple flow
        int totalRead = 0;
        while (totalRead < 4)
        {
            int read = await _stream.ReadAsync(lenBytes.AsMemory(totalRead, 4 - totalRead), ct).ConfigureAwait(false);
            if (read == 0) throw new Exception("Connection closed while reading length prefix.");
            totalRead += read;
        }

        int length = BitConverter.ToInt32(lenBytes, 0);
        byte[] data = new byte[length];
        
        totalRead = 0;
        while (totalRead < length)
        {
            int read = await _stream.ReadAsync(data.AsMemory(totalRead, length - totalRead), ct).ConfigureAwait(false);
            if (read == 0) throw new Exception("Connection closed while reading payload.");
            totalRead += read;
        }

        return data;
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
    }
}

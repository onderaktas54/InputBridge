using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace InputBridge.Core.Network;

public sealed class TcpTransport : ITransport
{
    private const int MaxFrameSize = 1024 * 1024;
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public TcpTransport(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true; // Disable Nagle's algorithm for minimum latency
        _client.ReceiveBufferSize = 65536;
        _client.SendBufferSize = 65536;
        _client.ReceiveTimeout = 5000;
        _client.SendTimeout = 5000;
        _stream = _client.GetStream();
    }

    public bool IsConnected
    {
        get
        {
            try
            {
                if (_client.Client == null || !_client.Connected)
                    return false;

                // Active socket poll check
                if (_client.Client.Poll(1000, SelectMode.SelectRead))
                {
                    // If polled and no data available, connection is dead
                    if (_client.Client.Available == 0) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public async ValueTask SendAsync(byte[] data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > MaxFrameSize)
            throw new ArgumentOutOfRangeException(nameof(data), $"TCP frame exceeds {MaxFrameSize} bytes.");

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Keep the length prefix and payload atomic relative to other senders.
            // Keyboard, heartbeat and mode-switch messages share this transport.
            byte[] lenBytes = BitConverter.GetBytes(data.Length);
            await _stream.WriteAsync(lenBytes, ct).ConfigureAwait(false);
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
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
        if (length < 0 || length > MaxFrameSize)
            throw new InvalidDataException($"Invalid TCP frame length: {length}.");

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

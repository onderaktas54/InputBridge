using System;
using System.Threading;
using System.Threading.Tasks;

namespace InputBridge.Core.Network;

public interface ITransport : IDisposable
{
    ValueTask SendAsync(byte[] data, CancellationToken ct = default);
    ValueTask<byte[]> ReceiveAsync(CancellationToken ct = default);
    bool IsConnected { get; }
}

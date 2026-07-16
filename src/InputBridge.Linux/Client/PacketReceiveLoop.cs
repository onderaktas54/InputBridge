using InputBridge.Core.Crypto;
using InputBridge.Core.Network;
using InputBridge.Core.Protocol;
using Serilog;

namespace InputBridge.Linux.Client;

internal static class PacketReceiveLoop
{
    public static async Task RunAsync(
        ITransport transport,
        AesTransport crypto,
        bool isUdp,
        UdpSequenceTracker udpSequence,
        Action<InputPacket> dispatch,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && transport.IsConnected)
        {
            try
            {
                byte[] encrypted = await transport.ReceiveAsync(ct);
                byte[] decrypted = crypto.Decrypt(encrypted);
                InputPacket packet = PacketSerializer.Deserialize(decrypted);

                if (isUdp)
                {
                    if (!udpSequence.ShouldAccept(packet.SequenceNumber)) continue;
                }
                else if (packet.Type == InputType.Heartbeat)
                {
                    byte[] reply = crypto.Encrypt(PacketSerializer.Serialize(packet));
                    await transport.SendAsync(reply, ct);
                    continue;
                }

                dispatch(packet);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (isUdp)
            {
                // UDP can legally arrive late, duplicated or from the previous encrypted
                // session. A single invalid datagram must never tear down the healthy TCP
                // session and force a reconnect.
                Log.Debug(ex, "[Client] Ignoring invalid/transient UDP datagram.");
                try
                {
                    await Task.Delay(25, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[Client] TCP receive loop stopped.");
                return;
            }
        }
    }
}

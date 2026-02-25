using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InputBridge.Core.Network;

public record DiscoveredHost(string Hostname, string IpAddress, int Port, DateTime DiscoveredAt);

public sealed class DiscoveryService
{
    private const int DiscoveryPort = 7202;
    private const string Magic = "INPUTBRIDGE_DISCOVER";
    private const string ResponseMagic = "INPUTBRIDGE_RESPONSE";

    public async Task StartBroadcasting(string hostname, int listenPort, CancellationToken ct)
    {
        using var udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        
        string message = $"{Magic}:v1:{hostname}:{listenPort}";
        byte[] data = Encoding.UTF8.GetBytes(message);
        var endPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await udpClient.SendAsync(data, data.Length, endPoint).ConfigureAwait(false);
                await Task.Delay(3000, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected gracefully exited when canceled
        }
    }

    public async Task<List<DiscoveredHost>> ListenForHosts(TimeSpan timeout, CancellationToken ct)
    {
        var hosts = new ConcurrentDictionary<string, DiscoveredHost>();
        using var udpClient = new UdpClient(DiscoveryPort);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var result = await udpClient.ReceiveAsync(cts.Token).ConfigureAwait(false);
                string message = Encoding.UTF8.GetString(result.Buffer);

                if (message.StartsWith($"{Magic}:v1:", StringComparison.Ordinal))
                {
                    string[] parts = message.Split(':');
                    if (parts.Length >= 4)
                    {
                        string hostname = parts[2];
                        if (int.TryParse(parts[3], out int port))
                        {
                            var host = new DiscoveredHost(hostname, result.RemoteEndPoint.Address.ToString(), port, DateTime.UtcNow);
                            hosts[host.IpAddress] = host; // Deduplicate by IP
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached or user cancelled
        }

        return hosts.Values.ToList();
    }
}

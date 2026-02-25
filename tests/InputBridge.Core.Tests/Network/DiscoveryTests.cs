using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using InputBridge.Core.Network;
using Xunit;

namespace InputBridge.Core.Tests.Network;

public class DiscoveryTests
{
    [Fact]
    public async Task ListenForHosts_ShouldDiscoverBroadcastingHost()
    {
        // Require manual testing or we simulate it using localhost broadcast (127.255.255.255 / 127.0.0.1)
        var discovery = new DiscoveryService();

        // Start broadcasting in background
        using var cts = new CancellationTokenSource();
        var broadcastTask = Task.Run(async () =>
        {
            await discovery.StartBroadcasting("TestPC", 45000, cts.Token);
        });

        // Act
        // wait to ensure broadcast loop started, then listen for max 4s
        var hosts = await discovery.ListenForHosts(TimeSpan.FromSeconds(4), CancellationToken.None);

        // Stop broadcasting
        cts.Cancel();
        try { await broadcastTask; } catch { }

        // Assert
        hosts.Should().NotBeEmpty();
        var host = hosts.First(h => h.Hostname == "TestPC");
        host.Port.Should().Be(45000);
        host.IpAddress.Should().NotBeNullOrEmpty();
    }
}

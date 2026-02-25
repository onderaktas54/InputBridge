using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using InputBridge.Core.Network;
using Xunit;

namespace InputBridge.Core.Tests.Network;

public class TransportTests
{
    [Fact]
    public async Task UdpTransport_Roundtrip_ShouldWorkAndBeFast()
    {
        // Arrange
        int sendPort = 45000;
        int recvPort = 45001;

        using var sender = new UdpTransport(sendPort, "127.0.0.1", recvPort);
        using var receiver = new UdpTransport(recvPort);

        byte[] original = new byte[52]; // Encrypted packet size
        Random.Shared.NextBytes(original);

        // Act
        var receiveTask = receiver.ReceiveAsync();
        await sender.SendAsync(original);
        
        var received = await receiveTask;

        // Assert
        received.Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task TcpTransport_Roundtrip_ShouldWorkAndBeFast()
    {
        // Arrange
        int tcpPort = 45002;
        var listener = new TcpListener(IPAddress.Loopback, tcpPort);
        listener.Start();

        var clientTask = Task.Run(async () => 
        {
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Loopback, tcpPort);
            return new TcpTransport(tcpClient);
        });

        var serverClient = await listener.AcceptTcpClientAsync();
        using var serverTransport = new TcpTransport(serverClient);
        using var clientTransport = await clientTask;
        
        listener.Stop();

        byte[] original = new byte[100];
        Random.Shared.NextBytes(original);

        // Act
        var receiveTask = serverTransport.ReceiveAsync();
        await clientTransport.SendAsync(original);
        var received = await receiveTask;

        // Assert
        received.Should().BeEquivalentTo(original);
    }
}

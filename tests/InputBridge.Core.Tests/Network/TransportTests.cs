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
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int tcpPort = ((IPEndPoint)listener.LocalEndpoint).Port;

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

    [Fact]
    public async Task TcpTransport_ConcurrentSends_ShouldPreserveFrameBoundaries()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var rawClient = new TcpClient();
        Task connectTask = rawClient.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient rawServer = await listener.AcceptTcpClientAsync();
        await connectTask;
        listener.Stop();

        using var sender = new TcpTransport(rawClient);
        using var receiver = new TcpTransport(rawServer);

        byte[][] frames = Enumerable.Range(0, 100)
            .Select(i => Enumerable.Repeat((byte)i, 256 + i).ToArray())
            .ToArray();

        Task sendTask = Task.WhenAll(frames.Select(frame => sender.SendAsync(frame).AsTask()));
        var received = new List<byte[]>();
        for (int i = 0; i < frames.Length; i++)
            received.Add(await receiver.ReceiveAsync());
        await sendTask;

        received.Should().HaveCount(frames.Length);
        foreach (byte[] frame in frames)
            received.Should().ContainEquivalentOf(frame);
    }

    [Fact]
    public async Task TcpTransport_OversizedIncomingFrame_ShouldBeRejected()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var rawClient = new TcpClient();
        Task connectTask = rawClient.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient rawServer = await listener.AcceptTcpClientAsync();
        await connectTask;
        listener.Stop();

        using var receiver = new TcpTransport(rawServer);
        byte[] invalidLength = BitConverter.GetBytes(1024 * 1024 + 1);
        await rawClient.GetStream().WriteAsync(invalidLength);

        Func<Task> act = async () => await receiver.ReceiveAsync();
        await act.Should().ThrowAsync<InvalidDataException>();
    }
}

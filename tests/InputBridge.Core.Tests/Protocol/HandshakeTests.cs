using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using InputBridge.Core.Protocol;
using Xunit;

namespace InputBridge.Core.Tests.Protocol;

public class HandshakeTests
{
    private const string SharedSecret = "SuperSecretPassword123!";
    private const string WrongSecret = "WrongPassword!";

    [Fact]
    public async Task Handshake_WithCorrectSecret_ShouldExchangeSessionKey()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var manager = new HandshakeManager();

        var clientTask = Task.Run(async () =>
        {
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Loopback, port);
            return await manager.PerformAsClient(tcpClient, SharedSecret);
        });

        var serverClient = await listener.AcceptTcpClientAsync();
        var hostSession = await manager.PerformAsHost(serverClient, SharedSecret);
        var clientSession = await clientTask;

        listener.Stop();

        // Assert
        hostSession.Should().NotBeNull();
        clientSession.Should().NotBeNull();

        hostSession!.AesKey.Should().BeEquivalentTo(clientSession!.AesKey);
    }

    [Fact]
    public async Task Handshake_WithWrongSecret_ShouldFail()
    {
        // Arrange
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var manager = new HandshakeManager();

        var clientTask = Task.Run(async () =>
        {
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Loopback, port);
            return await manager.PerformAsClient(tcpClient, WrongSecret);
        });

        var serverClient = await listener.AcceptTcpClientAsync();
        var hostSession = await manager.PerformAsHost(serverClient, SharedSecret);
        serverClient.Dispose();

        // Attempt to wait for client without throwing to let the assert run
        SessionInfo? clientSession = null;
        try { clientSession = await clientTask; } catch { }

        listener.Stop();

        // Assert
        hostSession.Should().BeNull("Host should reject wrong secret");
        clientSession.Should().BeNull("Client should fail as host closes connection or HMAC mismatch");
    }

    [Fact]
    public async Task Handshake_WhenPeerStalls_ShouldHonorCancellation()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var idlePeer = new TcpClient();
        Task connectTask = idlePeer.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient serverClient = await listener.AcceptTcpClientAsync();
        await connectTask;

        var manager = new HandshakeManager();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        Func<Task> act = () => manager.PerformAsHost(serverClient, SharedSecret, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        listener.Stop();
    }

    [Fact]
    public async Task Handshake_WhenMessageIsOversized_ShouldRejectIt()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var rawClient = new TcpClient();
        Task connectTask = rawClient.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient serverClient = await listener.AcceptTcpClientAsync();
        await connectTask;
        listener.Stop();

        var manager = new HandshakeManager();
        Task<SessionInfo?> hostTask = manager.PerformAsHost(serverClient, SharedSecret);

        using var reader = new StreamReader(rawClient.GetStream(), Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(rawClient.GetStream(), Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        (await reader.ReadLineAsync()).Should().NotBeNull();
        await writer.WriteLineAsync(new string('A', 4097));

        Func<Task> act = async () => await hostTask;
        await act.Should().ThrowAsync<InvalidDataException>();
    }
}

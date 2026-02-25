using System;
using System.Net;
using System.Net.Sockets;
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
        int port = 45005;
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

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
        int port = 45006;
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

        var manager = new HandshakeManager();

        var clientTask = Task.Run(async () =>
        {
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Loopback, port);
            return await manager.PerformAsClient(tcpClient, WrongSecret);
        });

        var serverClient = await listener.AcceptTcpClientAsync();
        var hostSession = await manager.PerformAsHost(serverClient, SharedSecret);

        // Attempt to wait for client without throwing to let the assert run
        SessionInfo? clientSession = null;
        try { clientSession = await clientTask; } catch { }

        listener.Stop();

        // Assert
        hostSession.Should().BeNull("Host should reject wrong secret");
        clientSession.Should().BeNull("Client should fail as host closes connection or HMAC mismatch");
    }
}

using FluentAssertions;
using InputBridge.Core.Crypto;
using InputBridge.Core.Network;
using InputBridge.Core.Protocol;
using InputBridge.Linux.Client;
using Xunit;

namespace InputBridge.Linux.Tests;

public sealed class LinuxBehaviorTests
{
    [Fact]
    public void CliOptions_ShouldRequireAStrongSecret()
    {
        string? previous = Environment.GetEnvironmentVariable("INPUTBRIDGE_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("INPUTBRIDGE_SECRET", null);
            CliOptions.Parse(["client"]).Should().BeNull();
            CliOptions.Parse(["client", "--secret", "too-short"]).Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("INPUTBRIDGE_SECRET", previous);
        }
    }

    [Fact]
    public void CliOptions_ShouldReadSecretFromEnvironment()
    {
        string? previous = Environment.GetEnvironmentVariable("INPUTBRIDGE_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("INPUTBRIDGE_SECRET", "a-unique-secret-with-20-chars");
            CliOptions? options = CliOptions.Parse(["client", "--host", "192.0.2.10"]);

            options.Should().NotBeNull();
            options!.Host.Should().Be("192.0.2.10");
        }
        finally
        {
            Environment.SetEnvironmentVariable("INPUTBRIDGE_SECRET", previous);
        }
    }

    [Fact]
    public void UdpSequenceTracker_Reset_ShouldAcceptNewSessionSequence()
    {
        var tracker = new UdpSequenceTracker();
        tracker.ShouldAccept(0).Should().BeFalse();
        tracker.ShouldAccept(500).Should().BeTrue();
        tracker.ShouldAccept(1).Should().BeFalse();

        tracker.Reset();

        tracker.ShouldAccept(1).Should().BeTrue();
    }

    [Theory]
    [InlineData(0xE2, 86)]
    [InlineData(0xAD, 113)]
    [InlineData(0xAF, 115)]
    [InlineData(0xB3, 164)]
    public void KeyMap_ShouldSupportIsoTurkishAndMediaKeys(int virtualKey, int evdevCode)
    {
        KeyMap.VkToEvdev(virtualKey).Should().Be(evdevCode);
        KeyMap.EvdevToVk(evdevCode).Should().Be(virtualKey);
    }

    [Fact]
    public async Task UdpReceiveLoop_ShouldIgnoreInvalidDatagramAndKeepSessionAlive()
    {
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        using var crypto = new AesTransport(key);
        var packet = new InputPacket
        {
            Version = 1,
            Type = InputType.MouseMove,
            Data1 = 12,
            Data2 = -4,
            SequenceNumber = 1,
        };

        byte[] valid = crypto.Encrypt(PacketSerializer.Serialize(packet));
        using var transport = new ScriptedTransport([new byte[] { 1, 2, 3 }, valid]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        InputPacket? received = null;

        await PacketReceiveLoop.RunAsync(
            transport,
            crypto,
            isUdp: true,
            new UdpSequenceTracker(),
            value =>
            {
                received = value;
                cts.Cancel();
            },
            cts.Token);

        received.Should().NotBeNull();
        received!.Value.Type.Should().Be(InputType.MouseMove);
        received.Value.Data1.Should().Be(12);
        transport.ReceiveCount.Should().BeGreaterThanOrEqualTo(2);
    }

    private sealed class ScriptedTransport(IEnumerable<byte[]> packets) : ITransport
    {
        private readonly Queue<byte[]> _packets = new(packets);

        public int ReceiveCount { get; private set; }
        public bool IsConnected => true;

        public ValueTask SendAsync(byte[] data, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public async ValueTask<byte[]> ReceiveAsync(CancellationToken ct = default)
        {
            ReceiveCount++;
            if (_packets.Count > 0) return _packets.Dequeue();
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        }

        public void Dispose()
        {
        }
    }
}

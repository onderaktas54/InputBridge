using System;
using System.Runtime.InteropServices;
using FluentAssertions;
using InputBridge.Core.Protocol;
using Xunit;

namespace InputBridge.Core.Tests.Protocol;

public class PacketSerializerTests
{
    [Fact]
    public void Serialize_Deserialize_Roundtrip_ShouldPreserveAllFields()
    {
        // Arrange
        var original = new InputPacket
        {
            Version = 1,
            Type = InputType.MouseMove,
            Flags = ModifierFlags.Shift | ModifierFlags.Ctrl,
            Data1 = 1920,
            Data2 = -1080,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 42
        };

        // Act
        var bytes = PacketSerializer.Serialize(original);
        var recovered = PacketSerializer.Deserialize(bytes);

        // Assert
        bytes.Should().HaveCount(24);
        recovered.Version.Should().Be(original.Version);
        recovered.Type.Should().Be(original.Type);
        recovered.Flags.Should().Be(original.Flags);
        recovered.Data1.Should().Be(original.Data1);
        recovered.Data2.Should().Be(original.Data2);
        recovered.Timestamp.Should().Be(original.Timestamp);
        recovered.SequenceNumber.Should().Be(original.SequenceNumber);
    }

    [Fact]
    public void Deserialize_WhenDataIsTooShort_ShouldThrowArgumentException()
    {
        // Arrange
        var bytes = new byte[23]; // 1 byte short

        // Act
        Action act = () => PacketSerializer.Deserialize(bytes);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}

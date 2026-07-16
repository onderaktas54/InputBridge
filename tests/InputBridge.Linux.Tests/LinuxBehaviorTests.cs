using FluentAssertions;
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
}

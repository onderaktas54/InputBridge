using System;
using System.IO;
using FluentAssertions;
using InputBridge.Core.Configuration;
using Xunit;

namespace InputBridge.Core.Tests.Configuration;

public class SettingsManagerTests : IDisposable
{
    private readonly string _testConfigPath;

    public SettingsManagerTests()
    {
        _testConfigPath = Path.Combine(Path.GetTempPath(), $"InputBridge_Test_{Guid.NewGuid()}", "settings.json");
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ShouldCreateDefaultFile()
    {
        // Arrange
        var manager = new SettingsManager(_testConfigPath);

        // Act
        var settings = manager.Load();

        // Assert
        File.Exists(_testConfigPath).Should().BeTrue();
        settings.Should().NotBeNull();
        settings.Mode.Should().Be("auto"); // Default
        settings.Ui.Theme.Should().Be("dark"); // Default
    }

    [Fact]
    public void Save_ThenLoad_ShouldPreserveValues()
    {
        // Arrange
        var manager = new SettingsManager(_testConfigPath);
        var settingsToSave = new AppSettings();
        settingsToSave.Network.HostPort = 12345;
        settingsToSave.Hotkeys.SwitchToHost = "Shift+A";

        // Act
        manager.Save(settingsToSave);
        var loadedSettings = manager.Load();

        // Assert
        loadedSettings.Network.HostPort.Should().Be(12345);
        loadedSettings.Hotkeys.SwitchToHost.Should().Be("Shift+A");
    }

    [Fact]
    public void Load_WhenFileIsBrokenJson_ShouldReturnDefaults()
    {
        // Arrange
        var manager = new SettingsManager(_testConfigPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_testConfigPath)!);
        File.WriteAllText(_testConfigPath, "{ broken json ^_^ }");

        // Act
        var loadedSettings = manager.Load();

        // Assert
        loadedSettings.Should().NotBeNull();
        loadedSettings.Mode.Should().Be("auto"); // Default properties will be present
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_testConfigPath);
        if (dir != null && Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }
}

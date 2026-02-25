using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InputBridge.Core.Configuration;

public class NetworkSettings
{
    public int HostPort { get; set; } = 7200;
    public int DiscoveryPort { get; set; } = 7202;
    public int HeartbeatIntervalMs { get; set; } = 1000;
    public int ReconnectDelayMs { get; set; } = 5000;
    public int MaxReconnectAttempts { get; set; } = 10;
    public string SubnetFilter { get; set; } = "";
}

public class HotkeySettings
{
    public string SwitchToHost { get; set; } = "Ctrl+Win+D1";
    public string SwitchToClient1 { get; set; } = "Ctrl+Win+D2";
    public string SwitchToClient2 { get; set; } = "Ctrl+Win+D3";
    public string EmergencyRelease { get; set; } = "Ctrl+Alt+Escape";
}

public class MouseSettings
{
    public double SensitivityMultiplier { get; set; } = 1.0;
    public bool LockCursorOnRemote { get; set; } = true;
    public double ScrollMultiplier { get; set; } = 1.0;
}

public class SecuritySettings
{
    public bool EncryptionEnabled { get; set; } = true;
    public string SharedSecret { get; set; } = "";
    public bool AutoApproveKnownDevices { get; set; } = true;
}

public class UiSettings
{
    public string Theme { get; set; } = "dark";
    public bool StartMinimized { get; set; } = true;
    public bool AutoStartWithWindows { get; set; } = false;
    public bool ShowSwitchNotification { get; set; } = true;
    public bool NotificationSound { get; set; } = true;
}

public class LoggingSettings
{
    public string Level { get; set; } = "Information";
    public bool FileEnabled { get; set; } = true;
    public int MaxFileSizeMb { get; set; } = 10;
    public int MaxFiles { get; set; } = 5;
}

public class AppSettings
{
    public string Mode { get; set; } = "auto";
    public NetworkSettings Network { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public MouseSettings Mouse { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
}

public sealed class SettingsManager
{
    public static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InputBridge");
    
    public string ConfigPath { get; }

    public event Action<AppSettings>? SettingsChanged;

    public SettingsManager(string? customPath = null)
    {
        ConfigPath = customPath ?? Path.Combine(ConfigDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaultSettings = new AppSettings();
            Save(defaultSettings, suppressEvent: true);
            return defaultSettings;
        }

        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
        }
        catch
        {
            // If the file is broken, fallback gracefully
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings, bool suppressEvent = false)
    {
        var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(ConfigPath)!);
        if (!directoryInfo.Exists)
        {
            directoryInfo.Create();
        }

        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);

        if (!suppressEvent)
        {
            SettingsChanged?.Invoke(settings);
        }
    }
}

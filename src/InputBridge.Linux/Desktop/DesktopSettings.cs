using System.Text.Json;

namespace InputBridge.Linux.Desktop;

internal sealed class DesktopSettings
{
    public DesktopRole Role { get; set; } = DesktopRole.Client;
    public string HostAddress { get; set; } = "";
    public int Port { get; set; } = 7201;
    public bool RememberSecret { get; set; }
    public string Secret { get; set; } = "";
}

internal static class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "inputbridge",
            "desktop.json");

    public static DesktopSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new DesktopSettings();
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<DesktopSettings>(json, JsonOptions)
                ?? new DesktopSettings();
        }
        catch
        {
            return new DesktopSettings();
        }
    }

    public static void Save(DesktopSettings settings)
    {
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (directory != null) Directory.CreateDirectory(directory);

        var persisted = new DesktopSettings
        {
            Role = settings.Role,
            HostAddress = settings.HostAddress,
            Port = settings.Port,
            RememberSecret = settings.RememberSecret,
            Secret = settings.RememberSecret ? settings.Secret : "",
        };

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(persisted, JsonOptions));
        try
        {
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    SettingsPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (PlatformNotSupportedException)
        {
            // Linux application, but keep tests portable.
        }
    }
}

namespace InputBridge.Linux;

internal enum AppMode
{
    Client,
    Host,
}

internal sealed class CliOptions
{
    public AppMode Mode { get; private init; }
    public string Secret { get; private init; } = "";
    public string? Host { get; private init; }
    public int Port { get; private init; } = 7201;

    public static CliOptions? Parse(string[] args)
    {
        if (args.Length == 0) return null;

        AppMode mode;
        switch (args[0].ToLowerInvariant())
        {
            case "client": mode = AppMode.Client; break;
            case "host": mode = AppMode.Host; break;
            default: return null;
        }

        string? secret = Environment.GetEnvironmentVariable("INPUTBRIDGE_SECRET");
        string? host = null;
        int port = 7201;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--secret" when i + 1 < args.Length:
                    secret = args[++i];
                    break;
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int p):
                    port = p;
                    i++;
                    break;
                default:
                    return null;
            }
        }

        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16 || port is < 2 or > 65535) return null;
        if (mode == AppMode.Host && host != null) return null;

        return new CliOptions { Mode = mode, Secret = secret, Host = host, Port = port };
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            InputBridge — Linux edition

            USAGE:
              inputbridge-linux                         Open the desktop interface
              inputbridge-linux --gui                   Open the desktop interface
              inputbridge-linux <client|host> [options] Run headless

            MODES:
              client     Let a Host control this machine via /dev/uinput.
              host       Control another machine using evdev capture.

            OPTIONS:
              --secret <text>   Shared secret; minimum 16 characters.
              --host <ip>       Client: connect directly instead of LAN discovery.
              --port <n>        TCP port. Default: 7201.

            HOST HOTKEYS:
              Ctrl+Alt+S        Toggle forwarding on/off.
              Ctrl+Alt+Esc      Emergency release.
            """);
    }
}

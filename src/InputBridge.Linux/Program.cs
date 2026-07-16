using InputBridge.Linux.Client;
using InputBridge.Linux.Host;
using InputBridge.Linux.Native;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var options = CliOptions.Parse(args);
if (options == null)
{
    CliOptions.PrintUsage();
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Log.Information("Shutting down…");
    cts.Cancel();
};

try
{
    switch (options.Mode)
    {
        case AppMode.Client:
        {
            using var injector = UinputInjector.Create();
            var client = new LinuxClient(injector, options.Secret, options.Host, options.Port);
            Log.Information("InputBridge Linux — CLIENT mode. This machine can be controlled by a Host.");
            await client.RunAsync(cts.Token);
            break;
        }
        case AppMode.Host:
        {
            var host = new LinuxHost(options.Secret, options.Port);
            Log.Information("InputBridge Linux — HOST mode. This machine controls a connected client.");
            await host.RunAsync(cts.Token);
            break;
        }
    }
}
catch (InvalidOperationException ex)
{
    Log.Fatal(ex.Message);
    return 2;
}
catch (OperationCanceledException)
{
    // graceful shutdown
}
finally
{
    Log.CloseAndFlush();
}

return 0;

internal enum AppMode { Client, Host }

internal sealed class CliOptions
{
    public AppMode Mode { get; private init; }
    public string Secret { get; private init; } = "default_secret";
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

        string secret = "default_secret";
        string? host = null;
        int port = 7201;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--secret" when i + 1 < args.Length: secret = args[++i]; break;
                case "--host" when i + 1 < args.Length: host = args[++i]; break;
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int p):
                    port = p; i++; break;
                default: return null;
            }
        }

        return new CliOptions { Mode = mode, Secret = secret, Host = host, Port = port };
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            InputBridge — Linux edition

            USAGE:
              inputbridge-linux <client|host> [options]

            MODES:
              client     Let a Host (Windows or Linux) control THIS machine (injects via /dev/uinput).
              host       Control another machine FROM this one (captures via evdev).

            OPTIONS:
              --secret <text>   Shared secret; must match the other side. Default: "default_secret".
              --host <ip>       (client) Connect directly to this Host IP, skipping LAN discovery.
              --port <n>        TCP port. Default: 7201.

            HOST HOTKEYS:
              Ctrl+Alt+S        Toggle forwarding on/off.
              Ctrl+Alt+Esc      Emergency release (stop forwarding).

            NOTE: Needs access to /dev/uinput (client) or /dev/input/event* (host).
                  Run with sudo, or install the provided udev rule (see docs/LINUX.md).

            EXAMPLES:
              sudo ./inputbridge-linux client --secret mypass
              sudo ./inputbridge-linux client --host 192.168.1.20 --secret mypass
              sudo ./inputbridge-linux host --secret mypass
            """);
    }
}

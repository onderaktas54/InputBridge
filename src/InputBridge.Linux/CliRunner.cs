using InputBridge.Linux.Client;
using InputBridge.Linux.Host;
using InputBridge.Linux.Native;
using Serilog;

namespace InputBridge.Linux;

internal static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        CliOptions? options = CliOptions.Parse(args);
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
                    using (var injector = UinputInjector.Create())
                    {
                        var client = new LinuxClient(injector, options.Secret, options.Host, options.Port);
                        Log.Information("InputBridge Linux — CLIENT mode. This machine can be controlled by a Host.");
                        await client.RunAsync(cts.Token);
                    }
                    break;

                case AppMode.Host:
                    var host = new LinuxHost(options.Secret, options.Port);
                    Log.Information("InputBridge Linux — HOST mode. This machine controls a connected client.");
                    await host.RunAsync(cts.Token);
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            Log.Fatal(ex.Message);
            return 2;
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }

        return 0;
    }
}

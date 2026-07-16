using Avalonia;
using InputBridge.Linux.Desktop;

namespace InputBridge.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] != "--gui")
        {
            return CliRunner.RunAsync(args).GetAwaiter().GetResult();
        }

        string[] guiArgs = args.Length > 0 ? args[1..] : [];
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(guiArgs);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DesktopApp>()
            .UsePlatformDetect();
}

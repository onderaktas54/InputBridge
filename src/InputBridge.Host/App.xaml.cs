using System.Configuration;
using System.Data;
using System.Windows;
using System.Runtime.InteropServices;
using System;
using Serilog;

namespace InputBridge.Host;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InputBridge", "logs", "inputbridge-host-.log");
        Serilog.Log.Logger = new Serilog.LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [Host] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logPath, rollingInterval: Serilog.RollingInterval.Day, outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [Host] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}


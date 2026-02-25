using System;
using System.IO;
using InputBridge.Core.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace InputBridge.Core.Logging;

public static class LoggingFactory
{
    public static ILogger CreateLogger(LoggingSettings settings)
    {
        var levelConfig = settings.Level.ToLowerInvariant() switch
        {
            "debug" => LogEventLevel.Debug,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            _ => LogEventLevel.Information
        };

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(levelConfig)
            .WriteTo.Console();

        if (settings.FileEnabled)
        {
            string logFolder = Path.Combine(SettingsManager.ConfigDirectory, "logs");
            if (!Directory.Exists(logFolder))
            {
                Directory.CreateDirectory(logFolder);
            }

            string logPath = Path.Combine(logFolder, "inputbridge-.log");
            config.WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: settings.MaxFileSizeMb * 1024 * 1024,
                retainedFileCountLimit: settings.MaxFiles,
                rollOnFileSizeLimit: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            );
        }

        return config.CreateLogger();
    }
}

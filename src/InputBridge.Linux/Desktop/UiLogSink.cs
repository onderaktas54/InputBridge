using Serilog.Core;
using Serilog.Events;

namespace InputBridge.Linux.Desktop;

internal sealed class UiLogSink : ILogEventSink
{
    public static event Action<string>? MessageReceived;

    public void Emit(LogEvent logEvent)
    {
        string level = logEvent.Level switch
        {
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => "VRB",
        };

        string line =
            $"[{logEvent.Timestamp:HH:mm:ss}] {level}  {logEvent.RenderMessage()}";
        if (logEvent.Exception != null)
        {
            line += Environment.NewLine + logEvent.Exception.Message;
        }

        MessageReceived?.Invoke(line);
    }
}

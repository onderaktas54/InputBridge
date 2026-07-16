namespace InputBridge.Linux;

internal enum LinuxConnectionStatus
{
    Stopped,
    Discovering,
    Waiting,
    Connecting,
    Connected,
    Reconnecting,
    Error,
}

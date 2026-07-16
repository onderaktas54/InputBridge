using InputBridge.Linux.Client;
using InputBridge.Linux.Host;
using InputBridge.Linux.Native;
using Serilog;

namespace InputBridge.Linux.Desktop;

internal enum DesktopRole
{
    Client,
    Host,
}

internal sealed record ConnectionRequest(
    DesktopRole Role,
    string Secret,
    string? HostAddress,
    int Port);

internal sealed class ConnectionController : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public bool IsRunning => _runTask is { IsCompleted: false };

    public event Action<LinuxConnectionStatus, string>? StatusChanged;

    public async Task StartAsync(ConnectionRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            Report(
                request.Role == DesktopRole.Client
                    ? LinuxConnectionStatus.Discovering
                    : LinuxConnectionStatus.Waiting,
                request.Role == DesktopRole.Client
                    ? "Client hazırlanıyor…"
                    : $"Host TCP {request.Port} üzerinde hazırlanıyor…");

            _runTask = Task.Run(
                () => RunAsync(request, token),
                CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunAsync(ConnectionRequest request, CancellationToken token)
    {
        try
        {
            if (request.Role == DesktopRole.Client)
            {
                using var injector = UinputInjector.Create();
                var client = new LinuxClient(
                    injector,
                    request.Secret,
                    request.HostAddress,
                    request.Port,
                    Report);
                Log.Information("InputBridge desktop — CLIENT mode started.");
                await client.RunAsync(token);
            }
            else
            {
                var host = new LinuxHost(request.Secret, request.Port, Report);
                Log.Information("InputBridge desktop — HOST mode started.");
                await host.RunAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected shutdown.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Desktop connection failed");
            Report(LinuxConnectionStatus.Error, ex.Message);
        }
        finally
        {
            Report(LinuxConnectionStatus.Stopped, "Bağlantı kapalı.");
        }
    }

    private async Task StopCoreAsync()
    {
        if (_cts == null)
        {
            Report(LinuxConnectionStatus.Stopped, "Bağlantı kapalı.");
            return;
        }

        _cts.Cancel();
        if (_runTask != null)
        {
            try
            {
                await _runTask.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
            catch (TimeoutException)
            {
                Log.Warning("Desktop connection did not stop within three seconds.");
            }
        }

        _cts.Dispose();
        _cts = null;
        _runTask = null;
        Report(LinuxConnectionStatus.Stopped, "Bağlantı kapalı.");
    }

    private void Report(LinuxConnectionStatus status, string message) =>
        StatusChanged?.Invoke(status, message);

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }
}

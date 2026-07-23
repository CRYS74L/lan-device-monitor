using System.IO;
using System.IO.Pipes;
using System.Text;

namespace LanDeviceMonitor.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\LanDeviceMonitor.6A40A512-75FB-4B96-8DA9-9F09BE6B8254";
    private const string PipeName = "LanDeviceMonitor.Activation.6A40A512";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenerTask;

    public SingleInstanceService()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void StartListening(Action activationRequested)
    {
        if (!IsPrimary || _listenerTask is not null)
        {
            return;
        }

        _listenerTask = ListenAsync(activationRequested, _cancellation.Token);
    }

    public static async Task SignalPrimaryAsync()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                await pipe.ConnectAsync(250);
                var message = Encoding.UTF8.GetBytes("SHOW");
                await pipe.WriteAsync(message);
                await pipe.FlushAsync();
                return;
            }
            catch (TimeoutException)
            {
                await Task.Delay(100);
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }
    }

    private static async Task ListenAsync(Action activationRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);

                var buffer = new byte[16];
                var count = await pipe.ReadAsync(buffer, cancellationToken);
                if (Encoding.UTF8.GetString(buffer, 0, count).Equals("SHOW", StringComparison.Ordinal))
                {
                    activationRequested();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}

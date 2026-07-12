using System.Threading.Channels;

namespace ProjectManagement.Features.MediaLibrary.Outbox;

public interface IPrismMediaOutboxSignal
{
    void Pulse();
    Task WaitAsync(TimeSpan maximumDelay, CancellationToken cancellationToken);
}

/// <summary>
/// Low-latency in-process wake-up for the durable outbox worker. Correctness never depends
/// on the signal because the worker also polls the database and recovers expired locks.
/// </summary>
public sealed class PrismMediaOutboxSignal : IPrismMediaOutboxSignal
{
    private readonly Channel<byte> _channel = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    public void Pulse() => _channel.Writer.TryWrite(1);

    public async Task WaitAsync(TimeSpan maximumDelay, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(maximumDelay);
        try
        {
            await _channel.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Normal polling timeout.
        }
    }
}

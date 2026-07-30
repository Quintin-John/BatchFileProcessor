using System.Threading.Channels;

namespace Common.FileIngestion.Lineage;

/// <summary>
/// <see cref="ILineageEmitter"/> backed by a bounded in-memory channel (design §8, C2). Emitting writes
/// to the channel; a drainer consumes <see cref="Reader"/> and exports asynchronously, so the parse/
/// publish hot path never blocks on export I/O. Overflow policy is <see cref="BoundedChannelFullMode.Wait"/>
/// — block, never drop — so a saturated exporter applies backpressure (export throughput gates ingest)
/// rather than leaving a silent hole in the lineage guarantee.
/// </summary>
public sealed class ChannelLineageEmitter : ILineageEmitter
{
    private readonly Channel<LineageEvent> _channel;

    /// <summary>Creates the emitter with a bounded buffer.</summary>
    /// <param name="capacity">Buffer capacity; must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public ChannelLineageEmitter(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _channel = Channel.CreateBounded<LineageEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // block-on-overflow, never drop (§8)
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>The read side, consumed by the drainer.</summary>
    public ChannelReader<LineageEvent> Reader => _channel.Reader;

    /// <inheritdoc />
    public ValueTask EmitAsync(LineageEvent lineageEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lineageEvent);
        return _channel.Writer.WriteAsync(lineageEvent, cancellationToken);
    }

    /// <summary>Signals that no more events will be emitted, so the drainer can finish. Idempotent.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}

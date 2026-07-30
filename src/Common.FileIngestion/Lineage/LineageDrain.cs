using System.Threading.Channels;

namespace Common.FileIngestion.Lineage;

/// <summary>
/// Drains the bounded lineage channel to an <see cref="ILineageSink"/>, off the parse/publish hot path
/// (design §8). Runs until the channel is completed (all emitted events exported), so a run that calls
/// <see cref="ChannelLineageEmitter.Complete"/> after processing guarantees every event is flushed.
/// </summary>
public sealed class LineageDrain
{
    private readonly ChannelReader<LineageEvent> _reader;
    private readonly ILineageSink _sink;

    /// <summary>Creates the drainer.</summary>
    /// <param name="reader">The lineage channel reader; required.</param>
    /// <param name="sink">The export sink; required.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public LineageDrain(ChannelReader<LineageEvent> reader, ILineageSink sink)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sink);
        _reader = reader;
        _sink = sink;
    }

    /// <summary>Exports every event until the channel completes.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var lineageEvent in _reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await _sink.ExportAsync(lineageEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}

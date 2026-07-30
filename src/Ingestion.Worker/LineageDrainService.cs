using Common.FileIngestion.Lineage;

namespace Ingestion.Worker;

/// <summary>
/// Hosted service that drains the lineage channel to its sink for the process lifetime, off the
/// ingestion hot path (design §8). It must run: without it the bounded lineage channel fills and
/// emission blocks (block-on-overflow), stalling ingestion.
/// </summary>
public sealed class LineageDrainService : BackgroundService
{
    private readonly LineageDrain _drain;

    /// <summary>Creates the service.</summary>
    /// <param name="emitter">The lineage emitter whose channel is drained; required.</param>
    /// <param name="sink">The export sink; required.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public LineageDrainService(ChannelLineageEmitter emitter, ILineageSink sink)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        ArgumentNullException.ThrowIfNull(sink);
        _drain = new LineageDrain(emitter.Reader, sink);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _drain.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }
}

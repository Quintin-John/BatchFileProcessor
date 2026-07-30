using Common.FileIngestion.Lineage;

namespace Ingestion.Worker;

/// <summary>
/// Hosted service that drains the lineage channel to its sink for the process lifetime, off the
/// ingestion hot path (design §8). It must run: without it the bounded lineage channel fills and
/// emission blocks (block-on-overflow), stalling ingestion.
/// </summary>
public sealed class LineageDrainService : BackgroundService
{
    private readonly ChannelLineageEmitter _emitter;
    private readonly LineageDrain _drain;

    /// <summary>Creates the service.</summary>
    /// <param name="emitter">The lineage emitter whose channel is drained; required.</param>
    /// <param name="sink">The export sink; required.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public LineageDrainService(ChannelLineageEmitter emitter, ILineageSink sink)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        ArgumentNullException.ThrowIfNull(sink);
        _emitter = emitter;
        _drain = new LineageDrain(emitter.Reader, sink);
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Drain until the channel is completed by StopAsync — deliberately NOT bound to the stopping token,
        // so buffered lineage is flushed on graceful shutdown rather than dropped (§6.1 / §8 "never drop").
        // A hard process kill still terminates; the host shutdown timeout bounds a slow flush.
        _drain.RunAsync(CancellationToken.None);

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _emitter.Complete(); // no more events will arrive (the pump has already stopped); flush the buffer
        await base.StopAsync(cancellationToken).ConfigureAwait(false); // awaits ExecuteAsync draining to completion
    }
}

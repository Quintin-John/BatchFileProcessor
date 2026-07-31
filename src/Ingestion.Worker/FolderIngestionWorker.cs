using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Health;
using Common.FileIngestion.Sources;
using Common.Observability;
using Ingestion.Worker.Messages;

namespace Ingestion.Worker;

/// <summary>
/// Polls a <see cref="IFileSource"/> and drives each claimed file through the dispatcher. On startup it
/// re-offers orphaned claims (crash recovery), then loops claiming new arrivals. Each file is
/// dispatched as an <see cref="IngestFile"/> command; a successful dispatch completes the file, any
/// failure quarantines it (fail-closed) and the loop continues — one bad file never stalls the run.
/// </summary>
internal sealed partial class FolderIngestionWorker : BackgroundService
{
    // Checkpoint keys are namespaced by profile so two profiles holding a same-named file never collide on
    // one watermark. The separator must be filename-safe: FileCheckpointStore derives a file name from the
    // key and rejects invalid path characters (so "/" cannot be used).
    private const string SourceKeySeparator = "__";

    private readonly IFileSource _source;
    private readonly IIngestFileDispatcher _dispatcher;
    private readonly ReadinessGate _readiness;
    private readonly WorkerOptions _options;
    private readonly ILogger<FolderIngestionWorker> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the worker.</summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public FolderIngestionWorker(
        IFileSource source,
        IIngestFileDispatcher dispatcher,
        ReadinessGate readiness,
        WorkerOptions options,
        ILogger<FolderIngestionWorker> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _source = source;
        _dispatcher = dispatcher;
        _readiness = readiness;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessAsync(_source.RecoverOrphans(), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessAsync(_source.Claim(), stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(_options.PollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Dispatches each file, completing it on success and quarantining it on failure.</summary>
    internal async Task ProcessAsync(IReadOnlyList<ClaimedFile> files, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchAsync(ClaimedFile file, CancellationToken cancellationToken)
    {
        // One run per file. The scope is ambient (AsyncLocal), so it flows through the in-process dispatch
        // into the pipeline: its spans pick up run/correlation ids, and this worker's own logs are enriched
        // via the log scope. The command carries the same correlation id downstream as provenance.
        var run = RunContext.NewRun();
        using var scope = CorrelationScope.Begin(run);
        using var logScope = _logger.BeginCorrelationScope();
        try
        {
            var command = new IngestFile(
                $"{_options.ProfileId}{SourceKeySeparator}{file.Name}",
                file.Name,
                file.ProcessingPath,
                run.CorrelationId,
                _options.ProfileId,
                _options.LayoutVersion);

            await _dispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
            _source.Complete(file);
            _readiness.MarkHealthy(); // a clean publish means downstream is reachable
            LogIngested(file.Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine shutdown: leave the claim in place so the next run re-offers it (crash recovery),
            // and let cancellation unwind the loop. Distinguished by the actual stopping signal, not by the
            // exception type — a non-shutdown cancellation (e.g. a downstream timeout) falls through to the
            // fail-closed handler below rather than propagating and stopping the host.
            throw;
        }
#pragma warning disable CA1031 // fail-closed: any non-shutdown failure quarantines the file and the loop continues
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _source.Fail(file);
            _readiness.MarkDegraded(); // the pipeline could not complete this file (publish/infra impaired)
            LogFailed(ex, file.Name);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Ingested {File}.")]
    private partial void LogIngested(string file);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Ingestion failed for {File}; moved to failed.")]
    private partial void LogFailed(Exception exception, string file);
}

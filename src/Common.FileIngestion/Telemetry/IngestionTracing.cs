using System.Diagnostics;
using Common.Messaging.Contracts;
using Common.Observability;

namespace Common.FileIngestion.Telemetry;

/// <summary>
/// Starts the ingestion trace spans (design §8): a <c>ingest.file</c> span per run and a
/// <c>ingest.batch</c> span per published batch — run/batch granularity only (per-record spans would
/// explode volume at GB scale). A batch span active during publish lets the transport inject
/// <c>traceparent</c> into the message headers, continuing the trace into downstream consumers.
/// Returns null when no listener is sampling, so tracing is free when disabled.
/// </summary>
public sealed class IngestionTracing
{
    private const string FileActivityName = "ingest.file";
    private const string BatchActivityName = "ingest.batch";

    private readonly ObservabilityInstrumentation _instrumentation;

    /// <summary>Creates the tracer.</summary>
    /// <param name="instrumentation">The component's telemetry sources; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="instrumentation"/> is null.</exception>
    public IngestionTracing(ObservabilityInstrumentation instrumentation)
    {
        ArgumentNullException.ThrowIfNull(instrumentation);
        _instrumentation = instrumentation;
    }

    /// <summary>Starts the per-run file span, tagged with file identity.</summary>
    /// <param name="provenance">Run provenance; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provenance"/> is null.</exception>
    public Activity? StartFileActivity(MessageProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        var activity = _instrumentation.StartActivity(FileActivityName, ActivityKind.Consumer);
        activity?.SetTag(IngestionTelemetryTags.FileId, provenance.FileId);
        activity?.SetTag(IngestionTelemetryTags.FileName, provenance.FileName);
        activity?.SetTag(IngestionTelemetryTags.ProfileId, provenance.Profile);
        return activity;
    }

    /// <summary>Starts the per-batch span, tagged with batch identity.</summary>
    /// <param name="batch">The batch being published; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is null.</exception>
    public Activity? StartBatchActivity(IngestBatchMessage batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var activity = _instrumentation.StartActivity(BatchActivityName, ActivityKind.Producer);
        activity?.SetTag(IngestionTelemetryTags.BatchSeq, batch.BatchSeq);
        activity?.SetTag(IngestionTelemetryTags.MessageId, batch.MessageId);
        return activity;
    }
}

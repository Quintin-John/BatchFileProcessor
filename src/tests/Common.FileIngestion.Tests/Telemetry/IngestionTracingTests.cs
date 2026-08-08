using System.Diagnostics;
using Common.FileIngestion.Telemetry;
using Common.Observability;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Telemetry;

public sealed class IngestionTracingTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 1200;

    private const string SourceName = "test-tracing";

    private static MessageProvenance Provenance() => new("run", "FILE1", "f.dat", "g266", "4.8");

    private static IngestBatchMessage Batch() =>
        new("FILE1-3", Provenance(), 3, new[] { new IngestRecord(new RecordLocator(1, 0, RecordExtent, "TRAN"),
            new Dictionary<string, FieldValue> { ["v"] = new ClearFieldValue("x") }) });

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public void StartFileActivity_CreatesFileSpan_WithFileTags()
    {
        using var instrumentation = new ObservabilityInstrumentation(SourceName);
        using var listener = Listen();
        var tracing = new IngestionTracing(instrumentation);

        using var activity = tracing.StartFileActivity(Provenance());

        Assert.NotNull(activity);
        Assert.Equal("ingest.file", activity!.OperationName);
        Assert.Equal("FILE1", activity.GetTagItem(IngestionTelemetryTags.FileId));
        Assert.Equal("g266", activity.GetTagItem(IngestionTelemetryTags.ProfileId));
    }

    [Fact]
    public void StartBatchActivity_CreatesBatchSpan_WithBatchTags()
    {
        using var instrumentation = new ObservabilityInstrumentation(SourceName);
        using var listener = Listen();
        var tracing = new IngestionTracing(instrumentation);

        using var activity = tracing.StartBatchActivity(Batch());

        Assert.NotNull(activity);
        Assert.Equal("ingest.batch", activity!.OperationName);
        Assert.Equal(3L, activity.GetTagItem(IngestionTelemetryTags.BatchSeq));
        Assert.Equal("FILE1-3", activity.GetTagItem(IngestionTelemetryTags.MessageId));
    }

    [Fact]
    public void Constructor_NullInstrumentation_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new IngestionTracing(null!));

    [Fact]
    public void StartFileActivity_NullProvenance_Throws()
    {
        using var instrumentation = new ObservabilityInstrumentation(SourceName);
        Assert.Throws<ArgumentNullException>(() => new IngestionTracing(instrumentation).StartFileActivity(null!));
    }

    [Fact]
    public void StartBatchActivity_NullBatch_Throws()
    {
        using var instrumentation = new ObservabilityInstrumentation(SourceName);
        Assert.Throws<ArgumentNullException>(() => new IngestionTracing(instrumentation).StartBatchActivity(null!));
    }
}

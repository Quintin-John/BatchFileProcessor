using Common.FileIngestion.Abstractions;
using System.Text;
using Common.FileIngestion.Checkpointing;
using Common.FileIngestion.Health;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Parsing;
using Common.FileIngestion.Pipeline;
using Common.FileIngestion.Protection;
using Common.FileIngestion.Reading;
using Common.FileIngestion.Rejecting;
using Common.FileIngestion.Telemetry;
using Common.Observability;
using Common.Security.Encryption;
using Common.Messaging.Contracts;
using Ingestion.Worker.Messages;
using Ingestion.Worker.Profiles;

namespace Ingestion.Worker.Tests;

// Proves the host wiring end to end: PipelineIngestFileDispatcher -> FileIngestionPipeline -> publish.
// Fakes sit only at the parser/protector/publisher edges, each covered by its own component's tests.
public sealed class IngestionEndToEndTests : IDisposable
{
    private const string FileText = "DATA0001\nDATA0002\nREJ00003\nDATA0004\n";
    private readonly string _file = Path.Combine(Path.GetTempPath(), "e2e-" + Guid.NewGuid().ToString("N") + ".dat");
    private readonly CapturingPublisher _publisher = new();
    private readonly InMemoryCheckpointStore _checkpoints = new();

    public void Dispose() => File.Delete(_file);

    [Fact]
    public async Task Dispatch_IngestFile_RunsPipeline_PublishesBatchesAndRejects()
    {
        await File.WriteAllTextAsync(_file, FileText);
        var dispatcher = Dispatcher();

        await dispatcher.DispatchAsync(
            new IngestFile("e2e.dat", "e2e.dat", _file, "run-1", "feed-a"), CancellationToken.None);

        Assert.NotEmpty(_publisher.Batches);
        Assert.IsType<EncryptedFieldValue>(Assert.Single(_publisher.Rejects).RawRecord);
        Assert.All(_publisher.Batches, b => Assert.StartsWith(b.Provenance.FileId, b.MessageId, StringComparison.Ordinal));
        Assert.Null(await _checkpoints.LoadAsync("e2e.dat", CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PipelineIngestFileDispatcher(null!, [new LayoutPipeline(Layout(), BuildPipeline())]));
        Assert.Throws<ArgumentNullException>(() => new PipelineIngestFileDispatcher(new FixedLengthFormat(), null!));
    }

    [Fact]
    public void Constructor_NoLayouts_Throws() =>
        Assert.Throws<ArgumentException>(() => new PipelineIngestFileDispatcher(new FixedLengthFormat(), []));

    [Fact]
    public async Task DispatchAsync_NullCommand_Throws()
    {
        var dispatcher = Dispatcher();

        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.DispatchAsync(null!, CancellationToken.None));
    }

    // The fixture frames 8-byte records with a 1-byte terminator; the layout says so, and the dispatcher
    // reads its version for provenance.
    private static Layout Layout(string version = "1.0", int recordLength = 8) =>
        new(version, recordLength, "ascii", 1, 1, 4, new[]
        {
            new RecordDefinition("r", "DATA", new[] { new FieldDefinition("f", 1, recordLength) }),
        });

    private PipelineIngestFileDispatcher Dispatcher() =>
        new(new FixedLengthFormat(), [new LayoutPipeline(Layout(), BuildPipeline())]);

    private FileIngestionPipeline BuildPipeline()
    {
        var instrumentation = new ObservabilityInstrumentation("e2e");
        var metrics = new IngestionMetrics(instrumentation);
        var lineage = new RecordLineage(new ChannelLineageEmitter(1000), TimeProvider.System, enabled: true);
        var tracing = new IngestionTracing(instrumentation);

        return new FileIngestionPipeline(
            new StreamRecordReader(8, terminatorLength: 1, Encoding.ASCII),
            new FakeParser(),
            new RecordProtector(new PassThroughProtector(), new StubPayloadProtector()),
            new ConfirmedBatchPublisher(
                _publisher, _checkpoints, metrics, lineage, tracing, new Heartbeat(TimeProvider.System), "batches"),
            new RejectSink(_publisher, "rejects"),
            _checkpoints,
            metrics,
            lineage,
            tracing,
            new IngestionOptions(2, 100_000, 64, 1, 64));
    }

    private sealed class FakeParser : IRecordParser
    {
        public RecordParseResult Parse(FramedRecord framed)
        {
            var content = framed.Content;
            if (content.StartsWith("REJ", StringComparison.Ordinal))
            {
                return RecordParseResult.Rejected("REJ", content,
                    [new RejectReason("v", "rule", "CODE", null, content)]);
            }

            return RecordParseResult.Success(new IngestRecord(
                new RecordLocator(framed.RecordSeq, framed.ByteOffset, framed.ByteLength, "DATA"),
                new Dictionary<string, FieldValue> { ["v"] = new ClearFieldValue(content) }));
        }
    }

    private sealed class PassThroughProtector : IFieldProtector
    {
        public FieldValue Protect(FieldProtectionContext context, FieldValue value) => value;

        public FieldValue Unprotect(FieldProtectionContext context, FieldValue value) => value;
    }

    private sealed class StubPayloadProtector : IPayloadProtector
    {
        public EncryptedFieldValue Protect(FieldProtectionContext context, string payload) =>
            new(new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn"));

        public string Unprotect(FieldProtectionContext context, EncryptedFieldValue payload) => payload.Value.Ciphertext;
    }
}

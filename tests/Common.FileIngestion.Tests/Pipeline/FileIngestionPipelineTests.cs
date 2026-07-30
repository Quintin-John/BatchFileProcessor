using System.Text;
using Common.FileIngestion.Checkpointing;
using Common.FileIngestion.Health;
using Common.FileIngestion.Parsing;
using Common.FileIngestion.Pipeline;
using Common.FileIngestion.Protection;
using Common.FileIngestion.Reading;
using Common.FileIngestion.Rejecting;
using Common.FileIngestion.Telemetry;
using Common.Observability;
using Common.Security.DataProtection;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Pipeline;

public sealed class FileIngestionPipelineTests
{
    // Four 8-byte records + LF terminators: three parse ("DATA...") and one is rejected ("REJ...").
    private const string FileText = "DATA0001\nDATA0002\nREJ00003\nDATA0004\n";
    private const int RecordLength = 8;
    private const int Stride = RecordLength + 1;

    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

    private sealed class Harness
    {
        public CapturingPublisher Publisher { get; } = new();
        public InMemoryCheckpointStore Checkpoints { get; } = new();

        public FileIngestionPipeline Build(int maxRecords = 2) => new(
            new StreamRecordReader(RecordLength, terminatorLength: 1, Encoding.ASCII),
            new FakeParser(),
            new RecordProtector(new PassThroughProtector()),
            Publisher,
            new RejectSink(Publisher),
            Checkpoints,
            new IngestionMetrics(new ObservabilityInstrumentation("test-pipeline")),
            new Heartbeat(TimeProvider.System),
            new IngestionOptions(maxRecords, maxContentBytesPerBatch: 100_000));
    }

    private static IngestRequest Request(Func<Stream> openStream) =>
        new("g266.dat", "g266.dat", "run-1", "profile-1", "4.8", openStream);

    [Fact]
    public async Task Ingest_FullFile_AcceptsBatchesAndRejects_ThenClearsWatermark()
    {
        var harness = new Harness();
        var pipeline = harness.Build(maxRecords: 2);
        var bytes = Bytes(FileText);

        var outcome = await pipeline.IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);

        Assert.Equal(3, outcome.RecordsAccepted);
        Assert.Equal(1, outcome.RecordsRejected);
        Assert.Equal(2, outcome.BatchesPublished);

        Assert.Equal(2, harness.Publisher.Batches.Count);
        Assert.Equal($"{outcome.FileId}-0", harness.Publisher.Batches[0].MessageId);
        Assert.Equal($"{outcome.FileId}-1", harness.Publisher.Batches[1].MessageId);

        var reject = Assert.Single(harness.Publisher.Rejects);
        Assert.Equal($"{outcome.FileId}-3-reject", reject.MessageId);

        Assert.Null(await harness.Checkpoints.LoadAsync("g266.dat", CancellationToken.None));
    }

    [Fact]
    public async Task Ingest_FileId_IsFullContentSha256()
    {
        var harness = new Harness();
        var bytes = Bytes(FileText);
        var expected = await FileIdHasher.ComputeAsync(new MemoryStream(bytes), CancellationToken.None);

        var outcome = await harness.Build().IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);

        Assert.Equal(expected, outcome.FileId);
    }

    [Fact]
    public async Task Ingest_Resume_MatchingFileId_SkipsConfirmedPrefix_AndContinuesBatchSeq()
    {
        var harness = new Harness();
        var bytes = Bytes(FileText);
        var fileId = await FileIdHasher.ComputeAsync(new MemoryStream(bytes), CancellationToken.None);
        // Prior run confirmed records 1-2 (offsets 0, 9) against THIS content; resume from offset 18, next seq 1.
        await harness.Checkpoints.SaveAsync(new Watermark("g266.dat", fileId, 2 * Stride, 2, 0), CancellationToken.None);

        var outcome = await harness.Build().IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);

        Assert.Equal(1, outcome.RecordsAccepted); // only record 4
        Assert.Equal(1, outcome.RecordsRejected); // only record 3
        Assert.Equal(1, outcome.BatchesPublished);

        var batch = Assert.Single(harness.Publisher.Batches);
        Assert.Equal($"{outcome.FileId}-1", batch.MessageId);
        Assert.Equal(4, batch.Records[0].Locator.RecordSeq);
    }

    [Fact]
    public async Task Ingest_Resume_StaleWatermarkForDifferentContent_IsIgnored_NoRecordsSkipped()
    {
        var harness = new Harness();
        // A prior, DIFFERENT file reused the same name and left a watermark; its FileId cannot match.
        await harness.Checkpoints.SaveAsync(
            new Watermark("g266.dat", "STALE-HASH-FROM-A-DIFFERENT-FILE", 2 * Stride, 2, 0), CancellationToken.None);
        var bytes = Bytes(FileText);

        var outcome = await harness.Build().IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);

        // Stale watermark ignored → whole file processed, nothing silently skipped (BUG-1 guard).
        Assert.Equal(3, outcome.RecordsAccepted);
        Assert.Equal(1, outcome.RecordsRejected);
        Assert.Equal(2, outcome.BatchesPublished);
        Assert.Equal($"{outcome.FileId}-0", harness.Publisher.Batches[0].MessageId);
    }

    [Fact]
    public async Task Ingest_ContentChangesBetweenPasses_FailsClosed()
    {
        var harness = new Harness();
        var first = Bytes(FileText);
        var second = Bytes("XXXX0001\nXXXX0002\nXXXX0003\nXXXX0004\n"); // same length, different content
        var calls = 0;
        Stream Open() => new MemoryStream(calls++ == 0 ? first : second);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => harness.Build().IngestAsync(Request(Open), CancellationToken.None));
    }

    [Fact]
    public async Task Ingest_NullRequest_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new Harness().Build().IngestAsync(null!, CancellationToken.None));
    }

    private sealed class FakeParser : IRecordParser
    {
        public RecordParseResult Parse(long recordSeq, long byteOffset, ReadOnlySpan<char> record)
        {
            var content = record.ToString();
            if (content.StartsWith("REJ", StringComparison.Ordinal))
            {
                return RecordParseResult.Rejected("REJ", content,
                    [new RejectReason("v", "rule", "CODE", null, content)]);
            }

            var ingest = new IngestRecord(
                new RecordLocator(recordSeq, byteOffset, "DATA"),
                new Dictionary<string, FieldValue> { ["v"] = new ClearFieldValue(content) });
            return RecordParseResult.Success(ingest);
        }
    }

    private sealed class PassThroughProtector : IFieldProtector
    {
        public FieldValue Protect(FieldProtectionContext context, FieldValue value) => value;

        public FieldValue Unprotect(FieldProtectionContext context, FieldValue value) => value;

    }

    private sealed class CapturingPublisher : IMessagePublisher
    {
        public List<IngestBatchMessage> Batches { get; } = new();
        public List<RejectMessage> Rejects { get; } = new();

        public Task PublishBatchAsync(IngestBatchMessage batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }

        public Task PublishRejectAsync(RejectMessage reject, CancellationToken cancellationToken)
        {
            Rejects.Add(reject);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCheckpointStore : ICheckpointStore
    {
        private readonly Dictionary<string, Watermark> _watermarks = new(StringComparer.Ordinal);

        public Task<Watermark?> LoadAsync(string sourceKey, CancellationToken cancellationToken) =>
            Task.FromResult(_watermarks.GetValueOrDefault(sourceKey));

        public Task SaveAsync(Watermark watermark, CancellationToken cancellationToken)
        {
            _watermarks[watermark.SourceKey] = watermark;
            return Task.CompletedTask;
        }

        public Task ClearAsync(string sourceKey, CancellationToken cancellationToken)
        {
            _watermarks.Remove(sourceKey);
            return Task.CompletedTask;
        }
    }
}

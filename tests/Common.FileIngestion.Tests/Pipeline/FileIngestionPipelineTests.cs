using System.Diagnostics;
using System.Text;
using Common.FileIngestion.Checkpointing;
using Common.FileIngestion.Health;
using Common.FileIngestion.Lineage;
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
        public ChannelLineageEmitter Lineage { get; } = new(capacity: 1000); // large enough not to block small tests

        public int BatchChannelCapacity { get; set; } = 64; // large enough not to gate small tests
        public int PublisherConcurrency { get; set; } = 1;  // deterministic by default; fan-out tests raise it
        public int PublisherConfirmWindow { get; set; } = 64; // large enough not to gate; window test lowers it

        public FileIngestionPipeline Build(int maxRecords = 2)
        {
            var instrumentation = new ObservabilityInstrumentation("test-pipeline");
            return new FileIngestionPipeline(
                new StreamRecordReader(RecordLength, terminatorLength: 1, Encoding.ASCII),
                new FakeParser(),
                new RecordProtector(new PassThroughProtector(), new StubPayloadProtector()),
                Publisher,
                new RejectSink(Publisher),
                Checkpoints,
                new IngestionMetrics(instrumentation),
                new RecordLineage(Lineage, TimeProvider.System),
                new IngestionTracing(instrumentation),
                new Heartbeat(TimeProvider.System),
                new IngestionOptions(
                    maxRecords, maxContentBytesPerBatch: 100_000, BatchChannelCapacity, PublisherConcurrency,
                    PublisherConfirmWindow));
        }
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
        Assert.IsType<EncryptedFieldValue>(reject.RawRecord); // raw record encrypted, never clear

        Assert.Null(await harness.Checkpoints.LoadAsync("g266.dat", CancellationToken.None));
    }

    [Fact]
    public async Task Ingest_WithTinyChannelCapacity_BackpressuresButCompletesCorrectly()
    {
        // Capacity 1 forces the reader to wait on the publisher for almost every batch; the bounded
        // channel must neither lose a batch nor deadlock (GS1 backpressure).
        var harness = new Harness { BatchChannelCapacity = 1 };
        var bytes = Bytes(FileText);

        var outcome = await harness.Build(maxRecords: 1)
            .IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);

        Assert.Equal(3, outcome.RecordsAccepted);
        Assert.Equal(1, outcome.RecordsRejected);
        Assert.Equal(3, outcome.BatchesPublished); // maxRecords 1 -> one batch per accepted record
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
    public async Task Ingest_PublishFaultMidRun_FailsClosed_WatermarkStaysAtLastConfirmedBatch()
    {
        var harness = new Harness();
        harness.Publisher.FailOnBatchNumber = 2; // the final-flush batch faults; batch 0 already confirmed
        var bytes = Bytes(FileText);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Build(maxRecords: 2).IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None));

        var watermark = await harness.Checkpoints.LoadAsync("g266.dat", CancellationToken.None);
        Assert.NotNull(watermark);                    // not cleared — the file did not complete
        Assert.Equal(0, watermark!.BatchSeq);          // advanced only across the broker-confirmed batch 0
        Assert.Equal(2 * Stride, watermark.ByteOffset); // one stride past batch 0's last record
    }

    [Fact]
    public async Task Ingest_ResumesAfterPublishFault_CompletesWithoutLossOrDuplication()
    {
        var harness = new Harness();
        var bytes = Bytes(FileText);

        // Run 1: the second batch's publish faults after batch 0 is broker-confirmed.
        harness.Publisher.FailOnBatchNumber = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Build(maxRecords: 2).IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None));

        // Run 2: broker recovered; resume from the batch-0 watermark and finish.
        harness.Publisher.FailOnBatchNumber = int.MaxValue;
        var outcome = await harness.Build(maxRecords: 2)
            .IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);

        Assert.Equal(1, outcome.RecordsAccepted); // only the unconfirmed tail (record 4)
        Assert.Equal(1, outcome.RecordsRejected); // record 3
        Assert.Equal(1, outcome.BatchesPublished);
        // batch 0 (run 1) is never re-published and the sequence continues at 1 → no duplication, no loss.
        Assert.Equal($"{outcome.FileId}-0", harness.Publisher.Batches[0].MessageId);
        Assert.Equal($"{outcome.FileId}-1", harness.Publisher.Batches[1].MessageId);
        Assert.Null(await harness.Checkpoints.LoadAsync("g266.dat", CancellationToken.None)); // completed, cleared
    }

    [Fact]
    public async Task Ingest_RejectPublishFault_FailsClosed()
    {
        var harness = new Harness();
        harness.Publisher.FailOnReject = true;
        var bytes = Bytes(FileText);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Build().IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None));
    }

    [Fact]
    public async Task Ingest_CheckpointSaveFault_FailsClosed()
    {
        var harness = new Harness();
        harness.Checkpoints.FailOnSave = true;
        var bytes = Bytes(FileText);

        await Assert.ThrowsAsync<IOException>(
            () => harness.Build().IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None));
    }

    private static string ManyRecords(int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"DATA{i:D4}\n"));

    [Fact]
    public async Task Ingest_FanOut_PublishesEveryBatchOnce_AndCompletesCleanly()
    {
        var harness = new Harness { PublisherConcurrency = 4, BatchChannelCapacity = 4 };
        var bytes = Bytes(ManyRecords(20)); // 20 records, maxRecords 1 -> 20 batches across 4 publishers

        var outcome = await harness.Build(maxRecords: 1)
            .IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);

        Assert.Equal(20, outcome.RecordsAccepted);
        Assert.Equal(20, outcome.BatchesPublished);
        // Every batch seq 0..19 published exactly once, regardless of publisher order (no loss, no dup).
        Assert.Equal(
            Enumerable.Range(0, 20).Select(i => (long)i),
            harness.Publisher.Batches.Select(b => b.BatchSeq).OrderBy(s => s));
        Assert.Null(await harness.Checkpoints.LoadAsync("g266.dat", CancellationToken.None)); // completed, cleared
    }

    [Fact]
    public async Task Ingest_FanOut_PublishFault_NeverAdvancesWatermarkPastTheFailedBatch()
    {
        var harness = new Harness { PublisherConcurrency = 4, BatchChannelCapacity = 4 };
        harness.Publisher.FailOnBatchSeq = 3; // batch 3 always faults, creating a gap
        var bytes = Bytes(ManyRecords(20));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Build(maxRecords: 1).IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None));

        // Batches 4..19 may confirm out of order, but they sit beyond the gap at 3, so the contiguous
        // watermark can never reach or pass 3. It is null or somewhere in 0..2 — never a skipped record.
        var watermark = await harness.Checkpoints.LoadAsync("g266.dat", CancellationToken.None);
        Assert.True(watermark is null || watermark.BatchSeq < 3);
    }

    [Fact]
    public async Task Ingest_FanOut_ResumesAfterFault_AllBatchesEventuallyPublished_NoLoss()
    {
        var harness = new Harness { PublisherConcurrency = 4, BatchChannelCapacity = 8, PublisherConfirmWindow = 8 };
        var bytes = Bytes(ManyRecords(20));

        // Run 1: batch 10 faults, creating a gap; the run fails and the watermark stops before batch 10.
        harness.Publisher.FailOnBatchSeq = 10;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Build(maxRecords: 1).IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None));
        var afterFault = await harness.Checkpoints.LoadAsync("g266.dat", CancellationToken.None);
        Assert.True(afterFault is null || afterFault.BatchSeq < 10);

        // Run 2: broker recovered; resume from the watermark and finish.
        harness.Publisher.FailOnBatchSeq = null;
        var outcome = await harness.Build(maxRecords: 1)
            .IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);

        Assert.Null(await harness.Checkpoints.LoadAsync("g266.dat", CancellationToken.None)); // completed, cleared
        // Every batch 0..19 is published across the two runs (no loss). Distinct absorbs the at-least-once
        // replay of the unconfirmed window — the safety net the resume design relies on.
        Assert.Equal(
            Enumerable.Range(0, 20).Select(i => (long)i),
            harness.Publisher.Batches.Select(b => b.BatchSeq).Distinct().OrderBy(s => s));
    }

    [Fact]
    public async Task Ingest_ConfirmWindow_CapsBatchesInFlightToWindowSize()
    {
        const int window = 3;
        var harness = new Harness
        {
            PublisherConfirmWindow = window,
            BatchChannelCapacity = 100, // large, so the window (not the channel) is the binding constraint
            PublisherConcurrency = 8,   // >= window, so every windowed batch can be picked up
        };
        harness.Publisher.Gated = true; // hold confirms so the contiguous prefix never advances
        var bytes = Bytes(ManyRecords(20));

        var runTask = harness.Build(maxRecords: 1)
            .IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);

        // With confirms held, no window slot is ever released, so the producer can create at most `window`
        // batches — bounded regardless of the 20-record file. At most `window` publishes therefore start.
        await WaitUntil(() => harness.Publisher.PublishCallCount >= window);
        await Task.Delay(100);
        Assert.Equal(window, harness.Publisher.PublishCallCount);

        harness.Publisher.ReleaseGate();
        var outcome = await runTask;
        Assert.Equal(20, outcome.BatchesPublished); // once slots free, the rest flow through
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
        }
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
    public async Task Ingest_EmitsLineage_ForEveryRecordTransition()
    {
        var harness = new Harness();
        var bytes = Bytes(FileText);

        await harness.Build(maxRecords: 2).IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);
        harness.Lineage.Complete();

        var events = new List<LineageEvent>();
        await foreach (var e in harness.Lineage.Reader.ReadAllAsync())
        {
            events.Add(e);
        }

        // An accepted record moves consumed -> accepted -> batched -> confirmed.
        List<LineageState> accepted = [LineageState.Consumed, LineageState.Accepted, LineageState.Batched, LineageState.Confirmed];
        Assert.Equal(accepted, States(events, recordSeq: 1));

        // A rejected record moves consumed -> rejected, carrying the reason code (never a value).
        List<LineageState> rejected = [LineageState.Consumed, LineageState.Rejected];
        Assert.Equal(rejected, States(events, recordSeq: 3));
        Assert.Equal("CODE", events.Single(e => e.Locator.RecordSeq == 3 && e.State == LineageState.Rejected).ReasonCode);
    }

    private static List<LineageState> States(IEnumerable<LineageEvent> events, long recordSeq) =>
        events.Where(e => e.Locator.RecordSeq == recordSeq).Select(e => e.State).ToList();

    [Fact]
    public async Task Ingest_CreatesRunAndBatchSpans()
    {
        var operations = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "test-pipeline",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => operations.Add(activity.OperationName),
        };
        ActivitySource.AddActivityListener(listener);

        var bytes = Bytes(FileText);
        await new Harness().Build(maxRecords: 2)
            .IngestAsync(Request(() => new MemoryStream(bytes)), CancellationToken.None);

        Assert.Contains("ingest.file", operations);
        Assert.Contains("ingest.batch", operations);
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

    private sealed class StubPayloadProtector : IPayloadProtector
    {
        public EncryptedFieldValue Protect(FieldProtectionContext context, string payload) =>
            new(new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn"));

        public string Unprotect(FieldProtectionContext context, EncryptedFieldValue payload) => payload.Value.Ciphertext;
    }

    // Thread-safe: under fan-out, PublishBatchAsync is called from N publisher threads concurrently.
    private sealed class CapturingPublisher : IMessagePublisher
    {
        private readonly List<IngestBatchMessage> _batches = [];
        private readonly List<RejectMessage> _rejects = [];
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _publishCalls;

        public IReadOnlyList<IngestBatchMessage> Batches
        {
            get { lock (_batches) { return _batches.ToArray(); } }
        }

        public IReadOnlyList<RejectMessage> Rejects
        {
            get { lock (_rejects) { return _rejects.ToArray(); } }
        }

        public int FailOnBatchNumber { get; set; } = int.MaxValue; // 1-based publish attempt that faults
        public long? FailOnBatchSeq { get; set; }                  // fail a specific batch (deterministic under fan-out)
        public bool FailOnReject { get; set; }
        public bool Gated { get; set; }                            // hold every confirm until ReleaseGate
        public int PublishCallCount => Volatile.Read(ref _publishCalls);

        public void ReleaseGate() => _gate.TrySetResult();

        public async Task PublishBatchAsync(IngestBatchMessage batch, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _publishCalls);

            if (batch.BatchSeq == FailOnBatchSeq)
            {
                throw new InvalidOperationException("broker publish fault");
            }

            if (Gated)
            {
                await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false); // hold confirm until released
            }

            lock (_batches)
            {
                if (_batches.Count + 1 == FailOnBatchNumber)
                {
                    throw new InvalidOperationException("broker publish fault");
                }

                _batches.Add(batch);
            }
        }

        public Task PublishRejectAsync(RejectMessage reject, CancellationToken cancellationToken)
        {
            if (FailOnReject)
            {
                return Task.FromException(new InvalidOperationException("broker reject fault"));
            }

            lock (_rejects)
            {
                _rejects.Add(reject);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCheckpointStore : ICheckpointStore
    {
        private readonly Dictionary<string, Watermark> _watermarks = new(StringComparer.Ordinal);

        public bool FailOnSave { get; set; }

        public Task<Watermark?> LoadAsync(string sourceKey, CancellationToken cancellationToken) =>
            Task.FromResult(_watermarks.GetValueOrDefault(sourceKey));

        public Task SaveAsync(Watermark watermark, CancellationToken cancellationToken)
        {
            if (FailOnSave)
            {
                return Task.FromException(new IOException("checkpoint write fault"));
            }

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

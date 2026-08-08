using System.Text;
using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Checkpointing;
using Common.FileIngestion.Health;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Pipeline;
using Common.FileIngestion.Protection;
using Common.FileIngestion.Rejecting;
using Common.FileIngestion.Telemetry;
using Common.Messaging.Contracts;
using Common.Observability;
using Common.Security.DataProtection;
using Ingestion.Worker.Messages;
using Ingestion.Worker.Profiles;

namespace Ingestion.Worker.Tests;

/// <summary>
/// Drives a real delimited file through the whole host wiring using the production force-update-balance
/// layout — nothing about the format is stubbed. Only the publisher and checkpoint store are fakes, each
/// covered by its own component's tests.
/// <para>
/// No separator is assumed anywhere here: every row is built from the layout's own delimiter, and the
/// separator theory re-runs the same ingestion over a layout that differs only in that character.
/// </para>
/// </summary>
public sealed class DelimitedEndToEndTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "fub-" + Guid.NewGuid().ToString("N") + ".dat");
    private readonly CapturingPublisher _publisher = new();
    private readonly InMemoryCheckpointStore _checkpoints = new();
    private readonly DelimitedLayout _layout = (DelimitedLayout)new DelimitedFormat()
        .LoadLayout(Path.Combine(AppContext.BaseDirectory, "Layouts", "force-update-balance-v1.0.yaml"));

    public void Dispose() => File.Delete(_file);

    // The same layout with a different separator: only that character changes, so a difference in outcome
    // could only come from a separator assumption in the code under test.
    private DelimitedLayout WithDelimiter(char delimiter) =>
        new(_layout.Version, delimiter, _layout.Encoding, _layout.RowTypes);

    // A data row of exactly the declared width, joined with whatever the layout declares.
    private static string DataRow(
        DelimitedLayout layout, string accountIdentifier = "GUID-1", string programCode = "PGM") =>
        string.Join(layout.Delimiter, layout.Data.Fields.Select(f => f.Name switch
        {
            "AccountIdentifier" => accountIdentifier,
            "ProgramCode" => programCode,
            "GDAccountKey" => string.Empty,          // the one field that is neither required nor encrypted
            _ => f.Name + "-v",
        }));

    private static string HeaderRow(DelimitedLayout layout) =>
        string.Join(layout.Delimiter, layout.Data.Fields.Select(f => f.Name));

    private static string File_(DelimitedLayout layout, params string[] rows) =>
        string.Join('\n', rows.Prepend(HeaderRow(layout))) + "\n";

    // ---------- separator independence ----------

    [Theory]
    [InlineData('\t')]
    [InlineData(',')]
    [InlineData('|')]
    [InlineData(';')]
    [InlineData('~')]
    [InlineData((char)0x1F)]
    public async Task Dispatch_AnyDelimiter_IngestsIdentically(char delimiter)
    {
        // Comma, tab or anything else — the separator is layout data, so nothing downstream may assume one.
        var layout = WithDelimiter(delimiter);
        await File.WriteAllTextAsync(_file, File_(layout, DataRow(layout, "A"), DataRow(layout, "B")));

        await Dispatch(layout);

        var records = _publisher.Batches.SelectMany(b => b.Records).ToList();
        Assert.Empty(_publisher.Rejects);
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(layout.Data.Fields.Count, r.Fields.Count));
    }

    // ---------- structure ----------

    [Fact]
    public async Task Dispatch_SkipsHeader_PublishesEveryDataRow()
    {
        await File.WriteAllTextAsync(_file, File_(_layout, DataRow(_layout, "A"), DataRow(_layout, "B")));

        await Dispatch(_layout);

        var records = _publisher.Batches.SelectMany(b => b.Records).ToList();
        Assert.Equal(2, records.Count); // the header row is consumed for framing, never emitted
        Assert.All(records, r => Assert.Equal(_layout.Data.Name, r.Locator.RecordType));
    }

    [Fact]
    public async Task Dispatch_EncryptFlaggedFields_TravelEncrypted_AndTheRestClear()
    {
        await File.WriteAllTextAsync(_file, File_(_layout, DataRow(_layout)));

        await Dispatch(_layout);

        var record = Assert.Single(_publisher.Batches.SelectMany(b => b.Records));
        foreach (var declared in _layout.Data.Fields)
        {
            var value = record.Fields[declared.Name];
            if (declared.Encrypt)
            {
                Assert.IsType<EncryptedFieldValue>(value);
            }
            else
            {
                Assert.IsType<ClearFieldValue>(value);
            }
        }

        // The layout, not this test, decides which fields those are.
        Assert.Equal(3, _layout.Data.Fields.Count(f => f.Encrypt));
    }

    // ---------- rejection ----------

    [Fact]
    public async Task Dispatch_RowWithWrongFieldCount_IsRejected_NotMisMapped()
    {
        // A short row would otherwise shift every field after the gap; the count check rejects it instead.
        var shortRow = string.Join(_layout.Delimiter, "too", "short");
        await File.WriteAllTextAsync(_file, File_(_layout, DataRow(_layout), shortRow));

        await Dispatch(_layout);

        Assert.Single(_publisher.Batches.SelectMany(b => b.Records)); // the good row still publishes
        var reject = Assert.Single(_publisher.Rejects);
        Assert.Equal("WRONG_FIELD_COUNT", reject.Reasons[0].Code);
        Assert.IsType<EncryptedFieldValue>(reject.RawRecord); // a failed row can still carry PII
    }

    [Fact]
    public async Task Dispatch_BlankRequiredField_IsRejected()
    {
        await File.WriteAllTextAsync(_file, File_(_layout, DataRow(_layout, programCode: "   ")));

        await Dispatch(_layout);

        var reject = Assert.Single(_publisher.Rejects);
        Assert.Equal("REQUIRED_MISSING", reject.Reasons[0].Code);
        Assert.Equal("ProgramCode", reject.Reasons[0].Field);
    }

    // ---------- resume ----------

    [Fact]
    public async Task Dispatch_VariableLengthRows_AdvanceTheWatermarkAcrossRealRecordBoundaries()
    {
        // Rows differ in width, so any fixed-stride assumption anywhere in the chain would drift.
        var text = File_(_layout, DataRow(_layout, "SHORT"), DataRow(_layout, "A-MUCH-LONGER-IDENTIFIER"));
        await File.WriteAllTextAsync(_file, text);
        var rowEnds = RunningLineEnds(text);

        await Dispatch(_layout);

        Assert.NotEmpty(_checkpoints.Saved);
        Assert.All(_checkpoints.Saved, w => Assert.Contains(w.ByteOffset, rowEnds));
        Assert.Equal(Encoding.ASCII.GetByteCount(text), _checkpoints.Saved[^1].ByteOffset);
    }

    private static List<long> RunningLineEnds(string text)
    {
        var ends = new List<long>();
        long running = 0;
        foreach (var line in text.Split('\n')[..^1])
        {
            running += Encoding.ASCII.GetByteCount(line) + 1; // the row plus its terminator
            ends.Add(running);
        }

        return ends;
    }

    private Task Dispatch(DelimitedLayout layout) =>
        new PipelineIngestFileDispatcher(BuildPipeline(layout)).DispatchAsync(
            new IngestFile("fub.dat", "fub.dat", _file, "run-1", "force-update-balance", layout.Version),
            CancellationToken.None);

    private FileIngestionPipeline BuildPipeline(DelimitedLayout layout)
    {
        var instrumentation = new ObservabilityInstrumentation("fub-e2e");
        var keys = new InMemoryKeyProvider();
        var crypto = new AesGcmCryptoProvider();

        // Reader and parser come from the format binding, exactly as the composition root builds them.
        var (reader, parser) = new DelimitedFormat()
            .CreateFraming(layout, Encoding.GetEncoding(layout.Encoding));

        return new FileIngestionPipeline(
            reader,
            parser,
            new RecordProtector(
                new DefaultFieldProtector(crypto, keys, LayoutProtectionPolicy.From(layout)),
                new DefaultPayloadProtector(crypto, keys)),
            _publisher,
            new RejectSink(_publisher, "fub-rejects"),
            _checkpoints,
            new IngestionMetrics(instrumentation),
            new RecordLineage(new ChannelLineageEmitter(1000), TimeProvider.System, enabled: true),
            new IngestionTracing(instrumentation),
            new Heartbeat(TimeProvider.System),
            new IngestionOptions(1, 100_000, 64, 1, 64),
            "fub-batches");
    }

    private sealed class InMemoryCheckpointStore : ICheckpointStore
    {
        private readonly Dictionary<string, Watermark> _watermarks = new(StringComparer.Ordinal);

        public List<Watermark> Saved { get; } = [];

        public Task<Watermark?> LoadAsync(string sourceKey, CancellationToken cancellationToken) =>
            Task.FromResult(_watermarks.GetValueOrDefault(sourceKey));

        public Task SaveAsync(Watermark watermark, CancellationToken cancellationToken)
        {
            _watermarks[watermark.SourceKey] = watermark;
            Saved.Add(watermark);
            return Task.CompletedTask;
        }

        public Task ClearAsync(string sourceKey, CancellationToken cancellationToken)
        {
            _watermarks.Remove(sourceKey);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingPublisher : IMessagePublisher
    {
        public List<IngestBatchMessage> Batches { get; } = [];
        public List<RejectMessage> Rejects { get; } = [];

        public Task PublishBatchAsync(IngestBatchMessage batch, string destination, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }

        public Task PublishRejectAsync(RejectMessage reject, string destination, CancellationToken cancellationToken)
        {
            Rejects.Add(reject);
            return Task.CompletedTask;
        }
    }
}

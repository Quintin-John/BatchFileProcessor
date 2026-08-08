using System.Globalization;
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
/// The delimited path end to end, over a layout this test declares. Nothing here knows about any particular
/// feed: the engine's contract is with the layout model, so every fixture is synthetic and every expectation
/// is derived from the layout under test. Changing a shipped layout must not move a single assertion.
/// <para>
/// The layout deliberately carries one row type per role and one data field per flag combination — plain,
/// encrypt, required, skip — so every branch the parser and protector can take is reachable from one shape.
/// Only the publisher and checkpoint store are doubles; framing, parsing, protection and batching are the
/// real components, wired the way the composition root wires them.
/// </para>
/// </summary>
public sealed class DelimitedIngestionEndToEndTests : IDisposable
{
    private const string HeaderRowType = "head";
    private const string DataRowType = "body";
    private const string TrailerRowType = "foot";
    private const string TrailerMarker = "END";
    private const int MarkerIndex = 0;

    private const string PlainField = "plain";
    private const string SecretField = "secret";
    private const string MandatoryField = "mandatory";
    private const string IgnoredField = "ignored";

    private readonly string _file = Path.Combine(Path.GetTempPath(), "e2e-" + Guid.NewGuid().ToString("N") + ".dat");
    private readonly CapturingPublisher _publisher = new();
    private readonly RecordingCheckpointStore _checkpoints = new();

    public void Dispose() => File.Delete(_file);

    // ---------- the layout under test ----------

    private static DelimitedLayout Layout(string delimiter = ",", bool withTrailerMarker = true) =>
        new("1.0", delimiter, '\n', "ascii", new[]
        {
            new DelimitedRowDefinition(HeaderRowType, RowRole.Header, 1, [], skip: true),
            new DelimitedRowDefinition(DataRowType, RowRole.Data, 0, new[]
            {
                new DelimitedFieldDefinition(PlainField, 0),
                new DelimitedFieldDefinition(SecretField, 1, encrypt: true),
                new DelimitedFieldDefinition(MandatoryField, 2, required: true),
                new DelimitedFieldDefinition(IgnoredField, 3, skip: true),
            }),
            new DelimitedRowDefinition(
                TrailerRowType, RowRole.Trailer, 1, [], skip: true,
                withTrailerMarker ? new RowMatch(MarkerIndex, TrailerMarker) : null),
        });

    // This fixture declares one body type; a layout whose body mixes types has its own test below.
    private static DelimitedRowDefinition Body(DelimitedLayout layout) => Assert.Single(layout.DataRows);

    // A value that identifies its own field, so a mis-mapped column is visible in the assertion.
    private static string ValueFor(DelimitedFieldDefinition field) => field.Name + "-value";

    private static string DataRow(DelimitedLayout layout, string? mandatory = null) =>
        string.Join(layout.Delimiter, Body(layout).Fields.Select(
            f => f.Name == MandatoryField ? mandatory ?? ValueFor(f) : ValueFor(f)));

    private static string HeaderRow(DelimitedLayout layout) => string.Join(layout.Delimiter, "H", "ignored");

    private static string TrailerRow(DelimitedLayout layout, string marker = TrailerMarker, int count = 0)
    {
        var cells = Enumerable.Repeat(string.Empty, MarkerIndex + 2).ToArray();
        cells[MarkerIndex] = marker;
        cells[^1] = count.ToString(CultureInfo.InvariantCulture);
        return string.Join(layout.Delimiter, cells);
    }

    private static string FileWith(DelimitedLayout layout, params string[] dataRows) =>
        string.Join('\n', dataRows.Prepend(HeaderRow(layout)).Append(TrailerRow(layout, count: dataRows.Length))) + "\n";

    // ---------- field mapping ----------

    [Fact]
    public async Task EveryEmittedField_ReceivesTheValueAtItsDeclaredIndex()
    {
        var layout = Layout();
        await WriteAsync(FileWith(layout, DataRow(layout)));

        await DispatchAsync(layout);

        var record = Assert.Single(_publisher.Batches.SelectMany(b => b.Records));
        foreach (var field in Body(layout).Fields.Where(f => !f.Skip && !f.Encrypt))
        {
            Assert.Equal(new ClearFieldValue(ValueFor(field)), record.Fields[field.Name]);
        }
    }

    [Fact]
    public async Task EncryptFlaggedField_ArrivesEncrypted_AndUnflaggedFieldsArriveClear()
    {
        var layout = Layout();
        await WriteAsync(FileWith(layout, DataRow(layout)));

        await DispatchAsync(layout);

        var record = Assert.Single(_publisher.Batches.SelectMany(b => b.Records));
        foreach (var field in Body(layout).Fields.Where(f => !f.Skip))
        {
            Assert.True(field.Encrypt
                ? record.Fields[field.Name] is EncryptedFieldValue
                : record.Fields[field.Name] is ClearFieldValue);
        }
    }

    [Fact]
    public async Task SkipFlaggedField_IsConsumedForCoverage_ButNeverEmitted()
    {
        var layout = Layout();
        await WriteAsync(FileWith(layout, DataRow(layout)));

        await DispatchAsync(layout);

        var record = Assert.Single(_publisher.Batches.SelectMany(b => b.Records));
        var emitted = Body(layout).Fields.Where(f => !f.Skip).ToList();

        Assert.Equal(emitted.Count, record.Fields.Count);
        Assert.All(Body(layout).Fields.Where(f => f.Skip), f => Assert.False(record.Fields.ContainsKey(f.Name)));
    }

    // ---------- rejection ----------

    [Fact]
    public async Task BlankRequiredField_RejectsTheRow()
    {
        var layout = Layout();
        await WriteAsync(FileWith(layout, DataRow(layout, mandatory: "   ")));

        await DispatchAsync(layout);

        var reject = Assert.Single(_publisher.Rejects);
        Assert.Equal("REQUIRED_MISSING", reject.Reasons[0].Code);
        Assert.Equal(MandatoryField, reject.Reasons[0].Field);
    }

    [Theory]
    [InlineData(-1)] // one value short
    [InlineData(+1)] // one value too many
    public async Task RowWithWrongFieldCount_IsRejected_RatherThanMisMapped(int delta)
    {
        // A short or long row must not shift every field after the gap; the count check rejects it whole.
        var layout = Layout();
        var cells = Enumerable.Range(0, Body(layout).Fields.Count + delta).Select(i => "v" + i);
        await WriteAsync(FileWith(layout, DataRow(layout), string.Join(layout.Delimiter, cells)));

        await DispatchAsync(layout);

        Assert.Single(_publisher.Batches.SelectMany(b => b.Records)); // the good row still publishes
        var reject = Assert.Single(_publisher.Rejects);
        Assert.Equal("WRONG_FIELD_COUNT", reject.Reasons[0].Code);
        Assert.IsType<EncryptedFieldValue>(reject.RawRecord); // a failed row can still carry sensitive data
    }

    // ---------- row structure ----------

    [Fact]
    public async Task SkippedHeaderAndTrailerRows_AreConsumedForFraming_ButNeverEmitted()
    {
        var layout = Layout();
        const int dataRows = 2;
        await WriteAsync(FileWith(layout, DataRow(layout), DataRow(layout)));

        await DispatchAsync(layout);

        Assert.Empty(_publisher.Rejects);
        var records = _publisher.Batches.SelectMany(b => b.Records).ToList();
        Assert.Equal(dataRows, records.Count);
        Assert.All(records, r => Assert.Equal(DataRowType, r.Locator.RecordType));
    }

    [Fact]
    public async Task FileOfHeaderAndTrailerOnly_IsAnEmptyBatch_NotAnError()
    {
        var layout = Layout();
        await WriteAsync(FileWith(layout));

        await DispatchAsync(layout);

        Assert.Empty(_publisher.Batches);
        Assert.Empty(_publisher.Rejects);
    }

    [Fact]
    public async Task TrailerCarryingItsDeclaredMarker_IsAccepted()
    {
        var layout = Layout();
        await WriteAsync(FileWith(layout, DataRow(layout)));

        await DispatchAsync(layout);

        Assert.Single(_publisher.Batches.SelectMany(b => b.Records));
    }

    [Fact]
    public async Task TrailerCarryingTheWrongMarker_FailsClosed_AndPublishesNothing()
    {
        var layout = Layout();
        var text = string.Join('\n', HeaderRow(layout), DataRow(layout), TrailerRow(layout, marker: "NOT-" + TrailerMarker)) + "\n";
        await WriteAsync(text);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => DispatchAsync(layout));
        Assert.Contains(TrailerMarker, ex.Message, StringComparison.Ordinal);

        AssertNothingShipped();
    }

    [Fact]
    public async Task TruncatedFile_WhoseLastRowIsNotTheTrailer_FailsClosed_AndPublishesNothing()
    {
        // Without the declared marker the final data row would pass as the trailer and be silently dropped.
        var layout = Layout();
        await WriteAsync(string.Join('\n', HeaderRow(layout), DataRow(layout), DataRow(layout)) + "\n");

        await Assert.ThrowsAsync<InvalidDataException>(() => DispatchAsync(layout));

        AssertNothingShipped();
    }

    [Fact]
    public async Task FileHoldingFewerRowsThanItsLayoutRequires_FailsClosed_AndPublishesNothing()
    {
        // The layout declares a header row and a trailer row, so a one-row file cannot satisfy it.
        var layout = Layout();
        await WriteAsync(HeaderRow(layout) + "\n");

        await Assert.ThrowsAsync<InvalidDataException>(() => DispatchAsync(layout));

        AssertNothingShipped();
    }

    [Fact]
    public async Task AStructuralFault_AfterManyGoodRows_StillPublishesNothing()
    {
        // The fault is at the end of the file and every row before it parses cleanly, so an implementation
        // that only discovered the problem while emitting would already have shipped all of them.
        var layout = Layout();
        var rows = Enumerable.Range(0, 50).Select(_ => DataRow(layout)).ToArray();
        var text = string.Join('\n', rows.Prepend(HeaderRow(layout)).Append(TrailerRow(layout, marker: "WRONG"))) + "\n";
        await WriteAsync(text);

        await Assert.ThrowsAsync<InvalidDataException>(() => DispatchAsync(layout));

        AssertNothingShipped();
    }

    // A file the engine rejects must leave no trace downstream: no batch, no reject, and no watermark that
    // would let a resumed run believe part of it had been confirmed.
    private void AssertNothingShipped()
    {
        Assert.Empty(_publisher.Batches);
        Assert.Empty(_publisher.Rejects);
        Assert.Empty(_checkpoints.Saved);
    }

    [Fact]
    public async Task WithoutADeclaredMarker_TheLastRowIsTakenAsTheTrailerOnPositionAlone()
    {
        // The marker is opt-in per layout; a layout that declares none keeps pure positional classification.
        var layout = Layout(withTrailerMarker: false);
        await WriteAsync(string.Join('\n', HeaderRow(layout), DataRow(layout), "anything-at-all") + "\n");

        await DispatchAsync(layout);

        Assert.Single(_publisher.Batches.SelectMany(b => b.Records));
        Assert.Empty(_publisher.Rejects);
    }

    // ---------- a body of several row types ----------

    // Two body types of different widths, each naming itself in the same column. Widths differ deliberately:
    // if the engine resolved a row to the wrong type, the field count would not line up and the row would be
    // rejected rather than silently mis-mapped.
    private const string DebitMarker = "DR";
    private const string CreditMarker = "CR";

    private static DelimitedLayout MixedBodyLayout() =>
        new("1.0", ",", '\n', "ascii", new[]
        {
            new DelimitedRowDefinition(HeaderRowType, RowRole.Header, 1, [], skip: true),
            new DelimitedRowDefinition(
                "debit", RowRole.Data, 0,
                [new DelimitedFieldDefinition("kind", 0), new DelimitedFieldDefinition("amount", 1)],
                skip: false, new RowMatch(MarkerIndex, DebitMarker)),
            new DelimitedRowDefinition(
                "credit", RowRole.Data, 0,
                [
                    new DelimitedFieldDefinition("kind", 0),
                    new DelimitedFieldDefinition("amount", 1),
                    new DelimitedFieldDefinition("reference", 2, encrypt: true),
                ],
                skip: false, new RowMatch(MarkerIndex, CreditMarker)),
            new DelimitedRowDefinition(
                TrailerRowType, RowRole.Trailer, 1, [], skip: true, new RowMatch(MarkerIndex, TrailerMarker)),
        });

    // Builds a row of the named type from its own declared fields, so the row's shape follows the layout.
    private static string RowOf(DelimitedLayout layout, string typeName)
    {
        var type = layout.DataRows.Single(r => r.Name == typeName);
        return string.Join(
            layout.Delimiter,
            type.Fields.Select(f => f.Index == MarkerIndex ? type.Match!.Value : ValueFor(f)));
    }

    [Fact]
    public async Task EachBodyRow_IsMappedAgainstTheTypeItsOwnMarkerNames()
    {
        var layout = MixedBodyLayout();
        await WriteAsync(FileWith(layout, RowOf(layout, "debit"), RowOf(layout, "credit")));

        await DispatchAsync(layout);

        var records = _publisher.Batches.SelectMany(b => b.Records).ToList();
        Assert.Empty(_publisher.Rejects);

        // Each record carries exactly the fields its own type declares — proof the row resolved to that type
        // and not to the other one, whose width differs.
        Assert.Collection(
            records.OrderBy(r => r.Locator.RecordSeq),
            debit => AssertMapsToType(layout, debit, "debit"),
            credit => AssertMapsToType(layout, credit, "credit"));
    }

    private static void AssertMapsToType(DelimitedLayout layout, IngestRecord record, string typeName)
    {
        var type = layout.DataRows.Single(r => r.Name == typeName);

        Assert.Equal(typeName, record.Locator.RecordType);
        Assert.Equal(type.Fields.Select(f => f.Name).OrderBy(n => n), record.Fields.Keys.OrderBy(n => n));

        // Per-type flags still apply: the encrypt flag lives on one type's field and not the other's.
        foreach (var field in type.Fields.Where(f => f.Index != MarkerIndex))
        {
            Assert.True(field.Encrypt
                ? record.Fields[field.Name] is EncryptedFieldValue
                : record.Fields[field.Name] is ClearFieldValue);
        }
    }

    [Fact]
    public async Task ABodyRowNamingNoDeclaredType_FailsClosed_AndShipsNothing()
    {
        // The unknown row sits last so that everything before it would already have been published if the
        // whole file were not framed before anything ships.
        var layout = MixedBodyLayout();
        var rows = Enumerable.Range(0, 50).Select(_ => RowOf(layout, "debit")).ToList();
        rows.Add("XX,1");
        await WriteAsync(FileWith(layout, [.. rows]));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => DispatchAsync(layout));

        AssertNothingShipped();

        // The message names the column and the declared types, never the value the row carried — row content
        // is not safe to put in a log.
        Assert.Contains("debit", ex.Message, StringComparison.Ordinal);
        Assert.Contains("credit", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("XX", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlankBodyRow_NamesNoType_AndFailsClosed()
    {
        // A blank line in the body is not a free pass: it names no type, so the file does not match its
        // layout. (A row genuinely too short to reach a marker in a later column is covered where the layout
        // resolves it, since with the marker at column 0 no row can be too short.)
        var layout = MixedBodyLayout();
        await WriteAsync(FileWith(layout, RowOf(layout, "debit"), string.Empty));

        await Assert.ThrowsAsync<InvalidDataException>(() => DispatchAsync(layout));

        AssertNothingShipped();
    }

    // ---------- framing ----------

    [Theory]
    [InlineData(",")]
    [InlineData("|")]
    [InlineData(";")]
    [InlineData("~")]
    [InlineData("\u001F")]
    [InlineData("~|~")]
    [InlineData("||")]
    [InlineData("<SEP>")]
    public async Task AnyDeclaredDelimiter_BehavesIdentically(string delimiter)
    {
        var layout = Layout(delimiter);
        await WriteAsync(FileWith(layout, DataRow(layout), DataRow(layout)));

        await DispatchAsync(layout);

        var records = _publisher.Batches.SelectMany(b => b.Records).ToList();
        Assert.Empty(_publisher.Rejects);
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(Body(layout).Fields.Count(f => !f.Skip), r.Fields.Count));
    }

    [Fact]
    public async Task VariableLengthRows_AdvanceTheWatermarkByEachRowsRealExtent()
    {
        // Rows differ in width, so any fixed-stride assumption anywhere in the chain would drift.
        var layout = Layout();
        var text = FileWith(layout, DataRow(layout, mandatory: "s"), DataRow(layout, mandatory: new string('l', 40)));
        await WriteAsync(text);

        await DispatchAsync(layout);

        Assert.NotEmpty(_checkpoints.Saved);
        Assert.All(_checkpoints.Saved, w => Assert.Contains(w.ByteOffset, RowEndOffsets(text)));
    }

    // Byte offset of the end of each row, derived from the file itself.
    private static List<long> RowEndOffsets(string text)
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

    // ---------- harness ----------

    private Task WriteAsync(string text) => File.WriteAllTextAsync(_file, text);

    private Task DispatchAsync(DelimitedLayout layout) =>
        new PipelineIngestFileDispatcher(BuildPipeline(layout)).DispatchAsync(
            new IngestFile("source", "source", _file, "run-1", "profile", layout.Version),
            CancellationToken.None);

    private FileIngestionPipeline BuildPipeline(DelimitedLayout layout)
    {
        var instrumentation = new ObservabilityInstrumentation("e2e");
        var keys = new InMemoryKeyProvider();
        var crypto = new AesGcmCryptoProvider();

        // Reader and parser come from the format binding, exactly as the composition root builds them.
        var (reader, parser) = new DelimitedFormat().CreateFraming(layout, Encoding.GetEncoding(layout.Encoding));

        return new FileIngestionPipeline(
            reader,
            parser,
            new RecordProtector(
                new DefaultFieldProtector(crypto, keys, LayoutProtectionPolicy.From(layout)),
                new DefaultPayloadProtector(crypto, keys)),
            _publisher,
            new RejectSink(_publisher, "rejects"),
            _checkpoints,
            new IngestionMetrics(instrumentation),
            new RecordLineage(new ChannelLineageEmitter(1000), TimeProvider.System, enabled: true),
            new IngestionTracing(instrumentation),
            new Heartbeat(TimeProvider.System),
            new IngestionOptions(1, 200_000, 64, 1, 64),
            "batches");
    }

    private sealed class RecordingCheckpointStore : ICheckpointStore
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

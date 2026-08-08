using System.Globalization;
using System.Text;
using Common.FileIngestion.Abstractions;
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
/// Drives a real ACI E138 extract through the whole host wiring on the production layout: a skipped header,
/// 60-field data rows, and a skipped footer that verifies itself with the marker the layout declares. Only
/// the publisher and checkpoint store are fakes. No separator, column or count is written here — every one
/// is read from the layout.
/// </summary>
public sealed class E138EndToEndTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "e138-" + Guid.NewGuid().ToString("N") + ".dat");
    private readonly CapturingPublisher _publisher = new();
    private readonly RecordingCheckpointStore _checkpoints = new();
    private readonly DelimitedLayout _layout = (DelimitedLayout)new DelimitedFormat()
        .LoadLayout(Path.Combine(AppContext.BaseDirectory, "Layouts", "e138-v1.0.yaml"));

    public void Dispose() => File.Delete(_file);

    // A data row of exactly the declared width, joined with the layout's own delimiter.
    private string DataRow(string transactionAmount = "100.00") =>
        string.Join(_layout.Delimiter, _layout.Data.Fields.Select(f =>
            f.Name == "TransactionAmount" ? transactionAmount : f.Name + "-v"));

    private string HeaderRow() => string.Join(_layout.Delimiter, "Header", "2026-08-08T00:00:00");

    // The footer's marker value and column both come from the layout, so this row is correct by construction.
    private string FooterRow(int recordCount, string? marker = null)
    {
        var match = _layout.Trailer!.Match!;
        var cells = new List<string>();
        for (var index = 0; index <= Math.Max(match.Index, 1); index++)
        {
            cells.Add(index == match.Index ? marker ?? match.Value : recordCount.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(_layout.Delimiter, cells);
    }

    private string File_(params string[] dataRows) =>
        string.Join('\n', dataRows.Prepend(HeaderRow()).Append(FooterRow(dataRows.Length))) + "\n";

    [Fact]
    public async Task Dispatch_SkipsHeaderAndFooter_PublishesOnlyDataRows()
    {
        await File.WriteAllTextAsync(_file, File_(DataRow(), DataRow()));

        await Dispatch();

        var records = _publisher.Batches.SelectMany(b => b.Records).ToList();
        Assert.Empty(_publisher.Rejects);
        Assert.Equal(2, records.Count); // header and footer are consumed for framing, never emitted
        Assert.All(records, r => Assert.Equal(_layout.Data.Name, r.Locator.RecordType));
    }

    [Fact]
    public async Task Dispatch_EmitsEveryFieldExceptTheSkippedColumns()
    {
        await File.WriteAllTextAsync(_file, File_(DataRow()));

        await Dispatch();

        var record = Assert.Single(_publisher.Batches.SelectMany(b => b.Records));
        var emitted = _layout.Data.Fields.Where(f => !f.Skip).ToList();

        Assert.Equal(emitted.Count, record.Fields.Count);
        Assert.All(emitted, f => Assert.True(record.Fields.ContainsKey(f.Name)));

        // The UNUSED columns are counted for row coverage but never travel upstream.
        Assert.All(_layout.Data.Fields.Where(f => f.Skip), f => Assert.False(record.Fields.ContainsKey(f.Name)));
    }

    [Fact]
    public async Task Dispatch_EncryptFlaggedFields_TravelEncrypted_AndTheRestClear()
    {
        await File.WriteAllTextAsync(_file, File_(DataRow()));

        await Dispatch();

        var record = Assert.Single(_publisher.Batches.SelectMany(b => b.Records));
        foreach (var declared in _layout.Data.Fields.Where(f => !f.Skip))
        {
            Assert.True(declared.Encrypt
                ? record.Fields[declared.Name] is EncryptedFieldValue
                : record.Fields[declared.Name] is ClearFieldValue);
        }
    }

    [Fact]
    public async Task Dispatch_BlankRequiredField_IsRejected()
    {
        await File.WriteAllTextAsync(_file, File_(DataRow(transactionAmount: "   ")));

        await Dispatch();

        var reject = Assert.Single(_publisher.Rejects);
        Assert.Equal("REQUIRED_MISSING", reject.Reasons[0].Code);
        Assert.Equal("TransactionAmount", reject.Reasons[0].Field);
    }

    [Fact]
    public async Task Dispatch_TruncatedFile_WhoseLastRowIsNotTheFooter_FailsClosed()
    {
        // No footer row at all: without the declared marker the final data row would pass as the footer and
        // its 60 values would be silently discarded.
        await File.WriteAllTextAsync(_file, string.Join('\n', HeaderRow(), DataRow(), DataRow()) + "\n");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(Dispatch);
        Assert.Contains(_layout.Trailer!.Match!.Value, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_FooterCarryingTheWrongMarker_FailsClosed()
    {
        var text = string.Join('\n', HeaderRow(), DataRow(), FooterRow(1, marker: "NotTheFooter")) + "\n";
        await File.WriteAllTextAsync(_file, text);

        await Assert.ThrowsAsync<InvalidDataException>(Dispatch);
    }

    [Fact]
    public async Task Dispatch_FileWithNoDataRows_IsAcceptedAsAnEmptyBatch()
    {
        // Header plus footer and nothing between is a legitimate nil return, not a malformed file.
        await File.WriteAllTextAsync(_file, File_());

        await Dispatch();

        Assert.Empty(_publisher.Batches);
        Assert.Empty(_publisher.Rejects);
    }

    private Task Dispatch() =>
        new PipelineIngestFileDispatcher(BuildPipeline()).DispatchAsync(
            new IngestFile("e138.dat", "e138.dat", _file, "run-1", "e138", _layout.Version),
            CancellationToken.None);

    private FileIngestionPipeline BuildPipeline()
    {
        var instrumentation = new ObservabilityInstrumentation("e138-e2e");
        var keys = new InMemoryKeyProvider();
        var crypto = new AesGcmCryptoProvider();
        var (reader, parser) = new DelimitedFormat()
            .CreateFraming(_layout, Encoding.GetEncoding(_layout.Encoding));

        return new FileIngestionPipeline(
            reader,
            parser,
            new RecordProtector(
                new DefaultFieldProtector(crypto, keys, LayoutProtectionPolicy.From(_layout)),
                new DefaultPayloadProtector(crypto, keys)),
            _publisher,
            new RejectSink(_publisher, "e138-rejects"),
            _checkpoints,
            new IngestionMetrics(instrumentation),
            new RecordLineage(new ChannelLineageEmitter(1000), TimeProvider.System, enabled: true),
            new IngestionTracing(instrumentation),
            new Heartbeat(TimeProvider.System),
            new IngestionOptions(1, 200_000, 64, 1, 64),
            "e138-batches");
    }

    private sealed class RecordingCheckpointStore : ICheckpointStore
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

using System.Text;
using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Checkpointing;
using Common.FileIngestion.Health;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Parsing;
using Common.FileIngestion.Pipeline;
using Common.FileIngestion.Protection;
using Common.FileIngestion.Reading;
using Common.FileIngestion.Rejecting;
using Common.FileIngestion.Telemetry;
using Common.Messaging.Contracts;
using Common.Observability;
using Common.Security.DataProtection;
using Ingestion.Worker.Messages;
using Ingestion.Worker.Profiles;

namespace Ingestion.Worker.Tests;

/// <summary>
/// One folder receiving two versions of a format. The engine knows nothing about either: each layout
/// declares a record length, and that is the whole basis on which a file is attributed to one of them.
/// The versions here are synthetic, and the assertions derive from the layouts under test, so changing a
/// shipped layout cannot move one.
/// </summary>
public sealed class LayoutSelectionTests : IDisposable
{
    private const int ShortRecord = 12;
    private const int LongRecord = 24;
    private const string ShortVersion = "short";
    private const string LongVersion = "long";

    private readonly string _file = Path.Combine(Path.GetTempPath(), "sel-" + Guid.NewGuid().ToString("N") + ".dat");
    private readonly CapturingPublisher _publisher = new();
    private readonly InMemoryCheckpointStore _checkpoints = new();

    public void Dispose() => File.Delete(_file);

    private static Layout Layout(string version, int recordLength) =>
        new(version, recordLength, "ascii", 1, 1, 2, new[]
        {
            new RecordDefinition("r", "DT", new[]
            {
                new FieldDefinition("marker", 1, 2),
                new FieldDefinition("body", 3, recordLength - 2),
            }),
        });

    // A whole number of records of the given width, each terminated — the shape that layout describes.
    private async Task WriteRecordsAsync(int recordLength, int count)
    {
        var record = "DT" + new string('x', recordLength - 2) + "\n";
        await File.WriteAllTextAsync(_file, string.Concat(Enumerable.Repeat(record, count)));
    }

    private PipelineIngestFileDispatcher Dispatcher(params Layout[] candidates) =>
        new(new FixedLengthFormat(),
            [.. candidates.Select(l => new LayoutPipeline(l, BuildPipeline(l)))]);

    private Task DispatchAsync(PipelineIngestFileDispatcher dispatcher) =>
        dispatcher.DispatchAsync(
            new IngestFile("sel.dat", "sel.dat", _file, "run-1", "profile-a"), CancellationToken.None);

    // ---------- a file is attributed to the layout whose records it is made of ----------

    [Theory]
    [InlineData(ShortRecord, ShortVersion)]
    [InlineData(LongRecord, LongVersion)]
    public async Task AFileIsReadByTheLayoutWhoseRecordLengthItMatches(int recordLength, string expectedVersion)
    {
        await WriteRecordsAsync(recordLength, count: 3);

        await DispatchAsync(Dispatcher(Layout(ShortVersion, ShortRecord), Layout(LongVersion, LongRecord)));

        // Provenance names the layout that actually read the file, which is how the choice is observable.
        var batch = Assert.Single(_publisher.Batches);
        Assert.Equal(expectedVersion, batch.Provenance.LayoutVersion);
        Assert.Equal(3, batch.Records.Count);
    }

    [Fact]
    public async Task TheOrderLayoutsAreDeclaredInDoesNotDecideTheOutcome()
    {
        // Selection must come from the file, not from which candidate happens to be first.
        await WriteRecordsAsync(LongRecord, count: 2);

        await DispatchAsync(Dispatcher(Layout(LongVersion, LongRecord), Layout(ShortVersion, ShortRecord)));
        var first = Assert.Single(_publisher.Batches).Provenance.LayoutVersion;

        _publisher.Batches.Clear();
        await _checkpoints.ClearAsync("sel.dat", CancellationToken.None);
        await DispatchAsync(Dispatcher(Layout(ShortVersion, ShortRecord), Layout(LongVersion, LongRecord)));

        Assert.Equal(first, Assert.Single(_publisher.Batches).Provenance.LayoutVersion);
        Assert.Equal(LongVersion, first);
    }

    // ---------- a file that cannot be attributed is not read at all ----------

    [Fact]
    public async Task AFileMatchingNoLayout_FailsClosed_AndShipsNothing()
    {
        // A whole number of neither record length.
        await File.WriteAllTextAsync(_file, new string('x', ShortRecord + LongRecord + 5));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => DispatchAsync(Dispatcher(Layout(ShortVersion, ShortRecord), Layout(LongVersion, LongRecord))));

        Assert.Contains("matches 0", ex.Message, StringComparison.Ordinal);
        AssertNothingShipped();
    }

    [Fact]
    public async Task AFileMatchingSeveralLayouts_FailsClosed_RatherThanPickingOne()
    {
        // Strides that share a multiple both divide such a file. Guessing would silently mis-map every
        // field, so an unattributable file is quarantined instead.
        var sharedMultiple = (ShortRecord + 1) * (LongRecord + 1);
        await File.WriteAllTextAsync(_file, new string('x', sharedMultiple));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => DispatchAsync(Dispatcher(Layout(ShortVersion, ShortRecord), Layout(LongVersion, LongRecord))));

        Assert.Contains("matches 2", ex.Message, StringComparison.Ordinal);
        Assert.Contains(ShortVersion, ex.Message, StringComparison.Ordinal);
        Assert.Contains(LongVersion, ex.Message, StringComparison.Ordinal);
        AssertNothingShipped();
    }

    [Fact]
    public async Task WithOneLayoutThereIsNoChoice_AndFramingRemainsThePipelinesToJudge()
    {
        // A single candidate is used as declared; a file that does not fit it still fails, but with the
        // pipeline's own diagnosis rather than a vaguer one from selection.
        await File.WriteAllTextAsync(_file, new string('x', ShortRecord + 5));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => DispatchAsync(Dispatcher(Layout(ShortVersion, ShortRecord))));

        Assert.Contains("incomplete record", ex.Message, StringComparison.Ordinal);
        AssertNothingShipped();
    }

    private void AssertNothingShipped()
    {
        Assert.Empty(_publisher.Batches);
        Assert.Empty(_publisher.Rejects);
    }

    private FileIngestionPipeline BuildPipeline(Layout layout)
    {
        var instrumentation = new ObservabilityInstrumentation("selection");
        var keys = new InMemoryKeyProvider();

        return new FileIngestionPipeline(
            new StreamRecordReader(layout.RecordLength, terminatorLength: 1, Encoding.ASCII),
            new FixedLengthRecordParser(layout),
            new RecordProtector(
                new DefaultFieldProtector(new AesGcmCryptoProvider(), keys, LayoutProtectionPolicy.From(layout)),
                new DefaultPayloadProtector(new AesGcmCryptoProvider(), keys)),
            _publisher,
            new RejectSink(_publisher, "rejects"),
            _checkpoints,
            new IngestionMetrics(instrumentation),
            new RecordLineage(new ChannelLineageEmitter(1000), TimeProvider.System, enabled: true),
            new IngestionTracing(instrumentation),
            new Heartbeat(TimeProvider.System),
            new IngestionOptions(100, 1_000_000, 64, 1, 64),
            "batches");
    }
}

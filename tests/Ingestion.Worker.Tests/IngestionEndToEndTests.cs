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
using Ingestion.Worker.Consumers;
using Ingestion.Worker.Messages;
using MassTransit;
using MassTransit.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestion.Worker.Tests;

// Proves the host wiring end to end: mediator -> IngestFileConsumer -> FileIngestionPipeline -> publish.
// Fakes sit only at the parser/protector/publisher edges, each covered by its own component's tests.
public sealed class IngestionEndToEndTests : IDisposable
{
    private const string FileText = "DATA0001\nDATA0002\nREJ00003\nDATA0004\n";
    private readonly string _file = Path.Combine(Path.GetTempPath(), "e2e-" + Guid.NewGuid().ToString("N") + ".dat");
    private readonly CapturingPublisher _publisher = new();
    private readonly InMemoryCheckpointStore _checkpoints = new();

    public void Dispose() => File.Delete(_file);

    [Fact]
    public async Task Send_IngestFile_RunsPipeline_PublishesBatchesAndRejects()
    {
        await File.WriteAllTextAsync(_file, FileText);

        await using var provider = new ServiceCollection()
            .AddSingleton(BuildPipeline())
            .AddMediator(cfg => cfg.AddConsumer<IngestFileConsumer>())
            .BuildServiceProvider(true);
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(new IngestFile("e2e.dat", "e2e.dat", _file, "run-1", "g266", "4.8"));

        Assert.NotEmpty(_publisher.Batches);
        Assert.IsType<EncryptedFieldValue>(Assert.Single(_publisher.Rejects).RawRecord);
        Assert.All(_publisher.Batches, b => Assert.StartsWith(b.Provenance.FileId, b.MessageId, StringComparison.Ordinal));
        Assert.Null(await _checkpoints.LoadAsync("e2e.dat", CancellationToken.None));
    }

    private FileIngestionPipeline BuildPipeline() => new(
        new StreamRecordReader(8, terminatorLength: 1, Encoding.ASCII),
        new FakeParser(),
        new RecordProtector(new PassThroughProtector(), new StubPayloadProtector()),
        _publisher,
        new RejectSink(_publisher),
        _checkpoints,
        new IngestionMetrics(new ObservabilityInstrumentation("e2e")),
        new Heartbeat(TimeProvider.System),
        new IngestionOptions(2, 100_000));

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

            return RecordParseResult.Success(new IngestRecord(
                new RecordLocator(recordSeq, byteOffset, "DATA"),
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

    private sealed class CapturingPublisher : IMessagePublisher
    {
        public List<IngestBatchMessage> Batches { get; } = [];
        public List<RejectMessage> Rejects { get; } = [];

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

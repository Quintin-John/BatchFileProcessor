using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit.Tests;

public sealed class MessagingIntegrationTests : IAsyncLifetime
{
    // Bytes one fixture record occupies, terminator included; the offset is derived from it.
    private const int RecordExtent = 1200;
    private const long FixtureSeq = 101;

    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;
    private BatchCollector _collector = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddSingleton<BatchCollector>()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<CapturingBatchConsumer>();
                cfg.UsingInMemory((context, bus) =>
                {
                    bus.ConfigureJsonSerializerOptions(options =>
                    {
                        MessagingJson.Configure(options);
                        return options;
                    });

                    // Explicit endpoint so the addressed Send to queue:batches is received.
                    bus.ReceiveEndpoint("batches", e => e.ConfigureConsumer<CapturingBatchConsumer>(context));
                });
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        _collector = _provider.GetRequiredService<BatchCollector>();
        await _harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static IngestBatchMessage SampleBatch()
    {
        var fields = new Dictionary<string, FieldValue>
        {
            ["plain"] = new ClearFieldValue(221.73m),
            ["encrypted"] = new EncryptedFieldValue(
                new EncryptedValue("AES-256-GCM", "key-id", "v1", "bm9uY2U=", "Y2lwaGVy", "dGFn")),
        };
        var provenance = new MessageProvenance("run-xyz", "file-abc", "source.dat", "feed-a", "1.0");
        var record = new IngestRecord(new RecordLocator(FixtureSeq, FixtureSeq * RecordExtent, RecordExtent, "TRAN"), fields);
        return new IngestBatchMessage("file-abc-1", provenance, 1, new[] { record });
    }

    [Fact]
    public async Task PublishedBatch_RoundTripsThroughTransport_WithFieldsAndCorrelation()
    {
        var batch = SampleBatch();
        var publisher = new MassTransitPublisher(_harness.Bus);

        await publisher.PublishBatchAsync(batch, "batches", CancellationToken.None);

        var context = await _collector.Received.WaitAsync(TimeSpan.FromSeconds(10));
        var received = context.Message;

        Assert.Equal(batch.MessageId, received.MessageId);
        Assert.Equal(batch.Provenance, received.Provenance);
        Assert.Equal(batch.Records[0].Locator, received.Records[0].Locator);
        Assert.Equal(batch.Records[0].Fields["plain"], received.Records[0].Fields["plain"]);
        Assert.Equal(batch.Records[0].Fields["encrypted"], received.Records[0].Fields["encrypted"]);

        Assert.True(context.Headers.TryGetHeader(MassTransitPublisher.CorrelationIdHeader, out var correlation));
        Assert.Equal(batch.Provenance.CorrelationId, correlation?.ToString());
    }
}

/// <summary>Captures the first consumed batch for assertion.</summary>
public sealed class BatchCollector
{
    private readonly TaskCompletionSource<ConsumeContext<IngestBatchMessage>> _source =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ConsumeContext<IngestBatchMessage>> Received => _source.Task;

    public void Set(ConsumeContext<IngestBatchMessage> context) => _source.TrySetResult(context);
}

/// <summary>Consumer that hands the consumed batch to a <see cref="BatchCollector"/>.</summary>
public sealed class CapturingBatchConsumer : IConsumer<IngestBatchMessage>
{
    private readonly BatchCollector _collector;

    public CapturingBatchConsumer(BatchCollector collector) => _collector = collector;

    public Task Consume(ConsumeContext<IngestBatchMessage> context)
    {
        _collector.Set(context);
        return Task.CompletedTask;
    }
}

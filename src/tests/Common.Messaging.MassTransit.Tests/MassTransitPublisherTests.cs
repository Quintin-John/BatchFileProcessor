using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit.Tests;

public sealed class MassTransitPublisherTests : IAsyncLifetime
{
    private const string BatchDestination = "batches";
    private const string RejectDestination = "rejects";
    private const string GuidCorrelation = "3f2504e04f8941d39a0c0305e82c3301"; // valid GUID (N format)

    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddSingleton<ContentTypeSpy>()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<BatchConsumer>();
                cfg.AddConsumer<RejectConsumer>();
                cfg.UsingInMemory((context, bus) =>
                {
                    bus.ConfigureJsonSerializerOptions(options =>
                    {
                        MessagingJson.Configure(options);
                        return options;
                    });

                    // Mirror production: bare domain JSON, not the MassTransit envelope. AnyMessageType lets the
                    // in-memory consumer bind the raw payload back to its type for the round-trip assertions.
                    bus.UseRawJsonSerializer(RawSerializerOptions.AnyMessageType);

                    // Explicit endpoints so an addressed Send to queue:<destination> is received.
                    bus.ReceiveEndpoint(BatchDestination, e => e.ConfigureConsumer<BatchConsumer>(context));
                    bus.ReceiveEndpoint(RejectDestination, e => e.ConfigureConsumer<RejectConsumer>(context));
                });
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static IngestBatchMessage SampleBatch() => BatchWithCorrelation("run-xyz");

    private static IngestBatchMessage BatchWithCorrelation(string correlationId)
    {
        var provenance = new MessageProvenance(correlationId, "file-abc", "g266.dat", "g266", "4.8");
        var record = new IngestRecord(
            new RecordLocator(101, 121200, "TRAN"),
            new Dictionary<string, FieldValue> { ["amount"] = new ClearFieldValue(221.73m) });
        return new IngestBatchMessage("file-abc-1", provenance, 1, new[] { record });
    }

    private static RejectMessage SampleReject()
    {
        var provenance = new MessageProvenance("run-xyz", "file-abc", "g266.dat", "g266", "4.8");
        var reasons = new[] { new RejectReason("amount", "decimal", "NON_NUMERIC", "decimal", "12A4") };
        return new RejectMessage(
            "file-abc-101-reject", provenance, new RecordLocator(101, 121200, "TRAN"),
            new ClearFieldValue("cmF3"), reasons);
    }

    [Fact]
    public async Task PublishBatchAsync_SendsToDestination_AndIsConsumed()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await publisher.PublishBatchAsync(SampleBatch(), BatchDestination, CancellationToken.None);

        Assert.True(await _harness.Sent.Any<IngestBatchMessage>());
        Assert.True(await _harness.GetConsumerHarness<BatchConsumer>().Consumed.Any<IngestBatchMessage>());
    }

    [Fact]
    public async Task PublishRejectAsync_SendsToDestination_AndIsConsumed()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await publisher.PublishRejectAsync(SampleReject(), RejectDestination, CancellationToken.None);

        Assert.True(await _harness.Sent.Any<RejectMessage>());
        Assert.True(await _harness.GetConsumerHarness<RejectConsumer>().Consumed.Any<RejectMessage>());
    }

    [Fact]
    public async Task PublishBatchAsync_StampsDeterministicEnvelopeMessageId()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);
        var batch = SampleBatch();

        await publisher.PublishBatchAsync(batch, BatchDestination, CancellationToken.None);

        Assert.True(await _harness.Sent.Any<IngestBatchMessage>());
        var sent = _harness.Sent.Select<IngestBatchMessage>().First();
        Assert.Equal(DeterministicGuid.From(batch.MessageId), sent.Context.MessageId);
    }

    [Fact]
    public async Task PublishBatchAsync_GuidCorrelation_SetsNativeEnvelopeCorrelationId_AndHeader()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await publisher.PublishBatchAsync(BatchWithCorrelation(GuidCorrelation), BatchDestination, CancellationToken.None);

        Assert.True(await _harness.Sent.Any<IngestBatchMessage>());
        var sent = _harness.Sent.Select<IngestBatchMessage>().First();
        Assert.Equal(Guid.Parse(GuidCorrelation), sent.Context.CorrelationId);
        Assert.Equal(GuidCorrelation, sent.Context.Headers.Get<string>(MassTransitPublisher.CorrelationIdHeader));
    }

    [Fact]
    public async Task PublishBatchAsync_NonGuidCorrelation_LeavesNativeCorrelationIdUnset_ButHeaderCarriesIt()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await publisher.PublishBatchAsync(BatchWithCorrelation("run-xyz"), BatchDestination, CancellationToken.None);

        Assert.True(await _harness.Sent.Any<IngestBatchMessage>());
        var sent = _harness.Sent.Select<IngestBatchMessage>().First();
        Assert.Null(sent.Context.CorrelationId);
        Assert.Equal("run-xyz", sent.Context.Headers.Get<string>(MassTransitPublisher.CorrelationIdHeader));
    }

    [Fact]
    public async Task PublishRejectAsync_StampsDeterministicEnvelopeMessageId()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);
        var reject = SampleReject();

        await publisher.PublishRejectAsync(reject, RejectDestination, CancellationToken.None);

        Assert.True(await _harness.Sent.Any<RejectMessage>());
        var sent = _harness.Sent.Select<RejectMessage>().First();
        Assert.Equal(DeterministicGuid.From(reject.MessageId), sent.Context.MessageId);
    }

    [Fact]
    public async Task PublishBatchAsync_SendsBareDomainJson_NotTheMassTransitEnvelope()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await publisher.PublishBatchAsync(SampleBatch(), BatchDestination, CancellationToken.None);

        Assert.True(await _harness.GetConsumerHarness<BatchConsumer>().Consumed.Any<IngestBatchMessage>());
        // Raw JSON => content-type application/json; the MassTransit envelope would be application/vnd.masstransit+json.
        Assert.Equal("application/json", _provider.GetRequiredService<ContentTypeSpy>().BatchContentType);
    }

    [Fact]
    public async Task PublishBatchAsync_NullBatch_Throws()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => publisher.PublishBatchAsync(null!, BatchDestination, CancellationToken.None));
    }

    [Fact]
    public async Task PublishBatchAsync_BlankDestination_Throws()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await Assert.ThrowsAsync<ArgumentException>(
            () => publisher.PublishBatchAsync(SampleBatch(), "  ", CancellationToken.None));
    }

    [Fact]
    public async Task PublishRejectAsync_NullReject_Throws()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => publisher.PublishRejectAsync(null!, RejectDestination, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullBus_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MassTransitPublisher(null!));
    }
}

/// <summary>Captures the wire content-type seen by the consumer, to prove bare JSON vs the MT envelope.</summary>
public sealed class ContentTypeSpy
{
    public string? BatchContentType { get; set; }
}

/// <summary>Test consumer used to prove a sent batch round-trips and to capture its wire content-type.</summary>
public sealed class BatchConsumer : IConsumer<IngestBatchMessage>
{
    private readonly ContentTypeSpy _spy;

    public BatchConsumer(ContentTypeSpy spy) => _spy = spy;

    public Task Consume(ConsumeContext<IngestBatchMessage> context)
    {
        _spy.BatchContentType = context.ReceiveContext.ContentType?.MediaType;
        return Task.CompletedTask;
    }
}

/// <summary>Test consumer used to prove a sent reject round-trips and is consumed.</summary>
public sealed class RejectConsumer : IConsumer<RejectMessage>
{
    public Task Consume(ConsumeContext<RejectMessage> context) => Task.CompletedTask;
}

using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit.Tests;

public sealed class MassTransitPublisherTests : IAsyncLifetime
{
    private const string BatchDestination = "batches";
    private const string RejectDestination = "rejects";

    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
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

    private static IngestBatchMessage SampleBatch()
    {
        var provenance = new MessageProvenance("run-xyz", "file-abc", "g266.dat", "g266", "4.8");
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

/// <summary>Test consumer used to prove a sent batch round-trips and is consumed.</summary>
public sealed class BatchConsumer : IConsumer<IngestBatchMessage>
{
    public Task Consume(ConsumeContext<IngestBatchMessage> context) => Task.CompletedTask;
}

/// <summary>Test consumer used to prove a sent reject round-trips and is consumed.</summary>
public sealed class RejectConsumer : IConsumer<RejectMessage>
{
    public Task Consume(ConsumeContext<RejectMessage> context) => Task.CompletedTask;
}

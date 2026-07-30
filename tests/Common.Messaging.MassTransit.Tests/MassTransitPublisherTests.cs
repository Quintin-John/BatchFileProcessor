using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit.Tests;

public sealed class MassTransitPublisherTests : IAsyncLifetime
{
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
                        MessagingSerialization.Configure(options);
                        return options;
                    });
                    bus.ConfigureEndpoints(context);
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

    [Fact]
    public async Task PublishBatchAsync_PublishesAndIsConsumed()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await publisher.PublishBatchAsync(SampleBatch(), CancellationToken.None);

        Assert.True(await _harness.Published.Any<IngestBatchMessage>());
        var consumer = _harness.GetConsumerHarness<BatchConsumer>();
        Assert.True(await consumer.Consumed.Any<IngestBatchMessage>());
    }

    [Fact]
    public async Task PublishBatchAsync_NullBatch_Throws()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => publisher.PublishBatchAsync(null!, CancellationToken.None));
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
    public async Task PublishRejectAsync_PublishesAndIsConsumed()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await publisher.PublishRejectAsync(SampleReject(), CancellationToken.None);

        Assert.True(await _harness.Published.Any<RejectMessage>());
        var consumer = _harness.GetConsumerHarness<RejectConsumer>();
        Assert.True(await consumer.Consumed.Any<RejectMessage>());
    }

    [Fact]
    public async Task PublishRejectAsync_NullReject_Throws()
    {
        var publisher = new MassTransitPublisher(_harness.Bus);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => publisher.PublishRejectAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullEndpoint_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MassTransitPublisher(null!));
    }
}

/// <summary>Test consumer used to prove a published batch round-trips and is consumed.</summary>
public sealed class BatchConsumer : IConsumer<IngestBatchMessage>
{
    public Task Consume(ConsumeContext<IngestBatchMessage> context) => Task.CompletedTask;
}

/// <summary>Test consumer used to prove a published reject round-trips and is consumed.</summary>
public sealed class RejectConsumer : IConsumer<RejectMessage>
{
    public Task Consume(ConsumeContext<RejectMessage> context) => Task.CompletedTask;
}

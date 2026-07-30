using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit.Tests;

public sealed class MessagingResilienceTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;
    private InvocationCounter _counter = null!;

    private static MessagingResilienceOptions FastRetry() => new()
    {
        RetryLimit = 2,
        RetryInitialInterval = TimeSpan.FromMilliseconds(1),
        RetryIntervalIncrement = TimeSpan.FromMilliseconds(1),
        CircuitBreakerActiveThreshold = 100, // high enough not to trip on this single failure
        CircuitBreakerTripThreshold = 100,
        CircuitBreakerResetInterval = TimeSpan.FromMinutes(1),
        RateLimit = 1000, // exercises the rate-limit path without throttling one message
        RateLimitInterval = TimeSpan.FromSeconds(1),
    };

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddSingleton<InvocationCounter>()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<FailingConsumer>();
                cfg.UsingInMemory((context, bus) =>
                {
                    bus.ConfigureJsonSerializerOptions(options =>
                    {
                        MessagingSerialization.Configure(options);
                        return options;
                    });
                    bus.ReceiveEndpoint("failing", endpoint =>
                    {
                        MessagingResilience.Apply(endpoint, FastRetry());
                        endpoint.ConfigureConsumer<FailingConsumer>(context);
                    });
                });
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        _counter = _provider.GetRequiredService<InvocationCounter>();
        await _harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static IngestBatchMessage SampleBatch()
    {
        var provenance = new MessageProvenance("run-xyz", "file-abc", "g266.dat", "g266", "4.8");
        var record = new IngestRecord(
            new RecordLocator(1, 0, "TRAN"),
            new Dictionary<string, FieldValue> { ["amount"] = new ClearFieldValue(1m) });
        return new IngestBatchMessage("file-abc-1", provenance, 1, new[] { record });
    }

    [Fact]
    public async Task FailingConsumer_IsRetried_PerPolicy()
    {
        await _harness.Bus.Publish(SampleBatch());

        Assert.True(await _harness.Consumed.Any<IngestBatchMessage>(x => x.Exception is not null));
        Assert.Equal(3, _counter.Count); // 1 initial attempt + 2 retries
    }

    [Fact]
    public void Apply_NullEndpoint_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MessagingResilience.Apply(null!, FastRetry()));
    }
}

/// <summary>Counts consumer invocations across retries.</summary>
public sealed class InvocationCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);
}

/// <summary>Test consumer that always throws, to exercise the retry policy.</summary>
public sealed class FailingConsumer : IConsumer<IngestBatchMessage>
{
    private readonly InvocationCounter _counter;

    public FailingConsumer(InvocationCounter counter) => _counter = counter;

    public Task Consume(ConsumeContext<IngestBatchMessage> context)
    {
        _counter.Increment();
        throw new InvalidOperationException("boom");
    }
}

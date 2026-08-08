using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit.Tests;

public sealed class RetryingMessagePublisherTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 1200;
    private const long FixtureSeq = 101;

    private const string Destination = "batches";

    // Zero backoff so retry-count behaviour is exercised without real waiting; the clock is real but
    // Task.Delay(Zero) completes immediately. Backoff arithmetic is asserted separately in BackoffFor_*.
    private static MessagingResilienceOptions FastRetry(int retryLimit = 5) => new()
    {
        RetryLimit = retryLimit,
        RetryInitialInterval = TimeSpan.Zero,
        RetryIntervalIncrement = TimeSpan.Zero,
    };

    private static RetryingMessagePublisher Sut(IMessagePublisher inner, MessagingResilienceOptions? resilience = null) =>
        new(inner, resilience ?? FastRetry(), TimeProvider.System);

    private static IngestBatchMessage SampleBatch()
    {
        var provenance = new MessageProvenance("run-xyz", "file-abc", "source.dat", "feed-a", "1.0");
        var record = new IngestRecord(
            new RecordLocator(FixtureSeq, FixtureSeq * RecordExtent, RecordExtent, "TRAN"),
            new Dictionary<string, FieldValue> { ["amount"] = new ClearFieldValue(221.73m) });
        return new IngestBatchMessage("file-abc-1", provenance, 1, new[] { record });
    }

    private static RejectMessage SampleReject()
    {
        var provenance = new MessageProvenance("run-xyz", "file-abc", "source.dat", "feed-a", "1.0");
        var reasons = new[] { new RejectReason("amount", "decimal", "NON_NUMERIC", "decimal", "12A4") };
        return new RejectMessage(
            "file-abc-101-reject", provenance, new RecordLocator(FixtureSeq, FixtureSeq * RecordExtent, RecordExtent, "TRAN"),
            new ClearFieldValue("cmF3"), reasons);
    }

    [Fact]
    public async Task PublishBatchAsync_SucceedsFirstTry_SendsOnce()
    {
        var inner = new ScriptedPublisher(failuresBeforeSuccess: 0, fault: new InvalidOperationException());

        await Sut(inner).PublishBatchAsync(SampleBatch(), Destination, CancellationToken.None);

        Assert.Equal(1, inner.BatchCalls);
    }

    [Fact]
    public async Task PublishBatchAsync_FaultsThenSucceeds_RetriesUntilSuccess()
    {
        var inner = new ScriptedPublisher(failuresBeforeSuccess: 2, fault: new InvalidOperationException("nak"));

        await Sut(inner).PublishBatchAsync(SampleBatch(), Destination, CancellationToken.None);

        Assert.Equal(3, inner.BatchCalls); // 2 faults + 1 success
    }

    [Fact]
    public async Task PublishBatchAsync_ExhaustsRetries_ThrowsLastFaultAfterLimitPlusOne()
    {
        var inner = new ScriptedPublisher(failuresBeforeSuccess: int.MaxValue, fault: new InvalidOperationException("down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sut(inner, FastRetry(retryLimit: 5)).PublishBatchAsync(SampleBatch(), Destination, CancellationToken.None));

        Assert.Equal(6, inner.BatchCalls); // initial attempt + 5 retries
    }

    [Fact]
    public async Task PublishBatchAsync_Cancellation_IsNotRetried()
    {
        var inner = new ScriptedPublisher(failuresBeforeSuccess: int.MaxValue, fault: new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Sut(inner).PublishBatchAsync(SampleBatch(), Destination, CancellationToken.None));

        Assert.Equal(1, inner.BatchCalls); // cancellation is terminal, never retried
    }

    [Fact]
    public async Task PublishRejectAsync_FaultsThenSucceeds_RetriesUntilSuccess()
    {
        var inner = new ScriptedPublisher(failuresBeforeSuccess: 1, fault: new InvalidOperationException("nak"));

        await Sut(inner).PublishRejectAsync(SampleReject(), Destination, CancellationToken.None);

        Assert.Equal(2, inner.RejectCalls); // 1 fault + 1 success
    }

    [Fact]
    public void BackoffFor_IsIncremental()
    {
        var sut = new RetryingMessagePublisher(
            new ScriptedPublisher(0, new InvalidOperationException()),
            new MessagingResilienceOptions
            {
                RetryLimit = 5,
                RetryInitialInterval = TimeSpan.FromSeconds(1),
                RetryIntervalIncrement = TimeSpan.FromSeconds(2),
            },
            TimeProvider.System);

        Assert.Equal(TimeSpan.FromSeconds(1), sut.BackoffFor(0));
        Assert.Equal(TimeSpan.FromSeconds(3), sut.BackoffFor(1));
        Assert.Equal(TimeSpan.FromSeconds(5), sut.BackoffFor(2));
    }

    [Fact]
    public void Constructor_NullInner_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RetryingMessagePublisher(null!, FastRetry(), TimeProvider.System));

    [Fact]
    public void Constructor_NullResilience_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RetryingMessagePublisher(
            new ScriptedPublisher(0, new InvalidOperationException()), null!, TimeProvider.System));

    [Fact]
    public void Constructor_NullTimeProvider_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RetryingMessagePublisher(
            new ScriptedPublisher(0, new InvalidOperationException()), FastRetry(), null!));

    [Fact]
    public void Constructor_InvalidResilience_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryingMessagePublisher(
            new ScriptedPublisher(0, new InvalidOperationException()),
            new MessagingResilienceOptions { RetryLimit = -1 },
            TimeProvider.System));

    // Inner publisher scripted to fault a fixed number of times before succeeding, counting calls per method.
    private sealed class ScriptedPublisher : IMessagePublisher
    {
        private readonly int _failuresBeforeSuccess;
        private readonly Exception _fault;

        public ScriptedPublisher(int failuresBeforeSuccess, Exception fault)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
            _fault = fault;
        }

        public int BatchCalls { get; private set; }

        public int RejectCalls { get; private set; }

        public Task PublishBatchAsync(IngestBatchMessage batch, string destination, CancellationToken cancellationToken)
        {
            BatchCalls++;
            return BatchCalls <= _failuresBeforeSuccess ? Task.FromException(_fault) : Task.CompletedTask;
        }

        public Task PublishRejectAsync(RejectMessage reject, string destination, CancellationToken cancellationToken)
        {
            RejectCalls++;
            return RejectCalls <= _failuresBeforeSuccess ? Task.FromException(_fault) : Task.CompletedTask;
        }
    }
}

using Common.FileIngestion.Rejecting;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Rejecting;

public sealed class RejectSinkTests
{
    private static MessageProvenance Provenance() => new("run", "FILE1", "f.dat", "g266", "4.8");

    private static RejectReason[] Reasons() =>
        [new RejectReason("amount", "decimal", "NON_NUMERIC", "decimal", "12A4")];

    [Fact]
    public async Task RejectAsync_BuildsDeterministicMessage_AndPublishes()
    {
        var publisher = new CapturingPublisher();
        var sink = new RejectSink(publisher);

        await sink.RejectAsync(
            Provenance(), new RecordLocator(7, 8400, "TRAN"),
            new ClearFieldValue("cmF3"), Reasons(), CancellationToken.None);

        var published = Assert.Single(publisher.Rejects);
        Assert.Equal("FILE1-7-reject", published.MessageId);
        Assert.Equal(7, published.Locator.RecordSeq);
        Assert.Equal("NON_NUMERIC", Assert.Single(published.Reasons).Code);
    }

    [Fact]
    public async Task RejectAsync_NullReasons_Throws()
    {
        var sink = new RejectSink(new CapturingPublisher());

        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.RejectAsync(
            Provenance(), new RecordLocator(1, 0, "TRAN"), new ClearFieldValue("x"), null!, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullPublisher_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RejectSink(null!));

    private sealed class CapturingPublisher : IMessagePublisher
    {
        public List<RejectMessage> Rejects { get; } = [];

        public Task PublishBatchAsync(IngestBatchMessage batch, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PublishRejectAsync(RejectMessage reject, CancellationToken cancellationToken)
        {
            Rejects.Add(reject);
            return Task.CompletedTask;
        }
    }
}

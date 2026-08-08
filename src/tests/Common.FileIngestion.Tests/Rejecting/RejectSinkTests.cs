using Common.FileIngestion.Rejecting;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Rejecting;

public sealed class RejectSinkTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 1200;

    private const string Destination = "rejects";

    private static MessageProvenance Provenance() => new("run", "FILE1", "f.dat", "feed-a", "1.0");

    private static RejectReason[] Reasons() =>
        [new RejectReason("amount", "decimal", "NON_NUMERIC", "decimal", "12A4")];

    [Fact]
    public async Task RejectAsync_BuildsDeterministicMessage_AndPublishesToDestination()
    {
        var publisher = new CapturingPublisher();
        var sink = new RejectSink(publisher, Destination);

        await sink.RejectAsync(
            Provenance(), new RecordLocator(7, 8400, RecordExtent, "TRAN"),
            new ClearFieldValue("cmF3"), Reasons(), CancellationToken.None);

        var published = Assert.Single(publisher.Rejects);
        Assert.Equal("FILE1-7-reject", published.Message.MessageId);
        Assert.Equal(7, published.Message.Locator.RecordSeq);
        Assert.Equal("NON_NUMERIC", Assert.Single(published.Message.Reasons).Code);
        Assert.Equal(Destination, published.Destination); // routed to the configured reject destination
    }

    [Fact]
    public async Task RejectAsync_NullReasons_Throws()
    {
        var sink = new RejectSink(new CapturingPublisher(), Destination);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.RejectAsync(
            Provenance(), new RecordLocator(1, 0, RecordExtent, "TRAN"), new ClearFieldValue("x"), null!, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullPublisher_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RejectSink(null!, Destination));

    [Fact]
    public void Constructor_BlankDestination_Throws() =>
        Assert.Throws<ArgumentException>(() => new RejectSink(new CapturingPublisher(), "  "));

    private sealed class CapturingPublisher : IMessagePublisher
    {
        public List<(RejectMessage Message, string Destination)> Rejects { get; } = [];

        public Task PublishBatchAsync(IngestBatchMessage batch, string destination, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PublishRejectAsync(RejectMessage reject, string destination, CancellationToken cancellationToken)
        {
            Rejects.Add((reject, destination));
            return Task.CompletedTask;
        }
    }
}

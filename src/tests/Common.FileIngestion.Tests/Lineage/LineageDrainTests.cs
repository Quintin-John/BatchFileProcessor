using Common.FileIngestion.Lineage;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Lineage;

public sealed class LineageDrainTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 10;

    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static LineageEvent Event(long seq) =>
        new("run-1", "FILE1", new RecordLocator(seq, seq * 10, RecordExtent, "TRAN"), LineageState.Consumed, When);

    [Fact]
    public async Task RunAsync_DrainsAllEventsToSink_InOrder_ThenCompletes()
    {
        var emitter = new ChannelLineageEmitter(capacity: 8);
        var sink = new CapturingSink();
        var drain = new LineageDrain(emitter.Reader, sink);

        await emitter.EmitAsync(Event(1), CancellationToken.None);
        await emitter.EmitAsync(Event(2), CancellationToken.None);
        emitter.Complete();

        await drain.RunAsync(CancellationToken.None); // returns once the channel is drained

        Assert.Equal(2, sink.Exported.Count);
        Assert.Equal(1, sink.Exported[0].Locator.RecordSeq);
        Assert.Equal(2, sink.Exported[1].Locator.RecordSeq);
    }

    [Fact]
    public void Constructor_NullArgument_Throws()
    {
        var emitter = new ChannelLineageEmitter(capacity: 1);

        Assert.Throws<ArgumentNullException>(() => new LineageDrain(null!, new CapturingSink()));
        Assert.Throws<ArgumentNullException>(() => new LineageDrain(emitter.Reader, null!));
    }

    private sealed class CapturingSink : ILineageSink
    {
        public List<LineageEvent> Exported { get; } = [];

        public Task ExportAsync(LineageEvent lineageEvent, CancellationToken cancellationToken)
        {
            Exported.Add(lineageEvent);
            return Task.CompletedTask;
        }
    }
}

using Common.FileIngestion.Lineage;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Lineage;

public sealed class ChannelLineageEmitterTests
{
    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static LineageEvent Event(long seq) =>
        new("run-1", "FILE1", new RecordLocator(seq, seq * 10, "TRAN"), LineageState.Consumed, When);

    [Fact]
    public async Task EmitAsync_EnqueuesEventsInOrder()
    {
        var emitter = new ChannelLineageEmitter(capacity: 8);

        await emitter.EmitAsync(Event(1), CancellationToken.None);
        await emitter.EmitAsync(Event(2), CancellationToken.None);
        emitter.Complete();

        var read = new List<long>();
        await foreach (var e in emitter.Reader.ReadAllAsync())
        {
            read.Add(e.Locator.RecordSeq);
        }

        Assert.Equal(1, read[0]);
        Assert.Equal(2, read[1]);
    }

    [Fact]
    public async Task EmitAsync_WhenFull_BlocksUntilDrained_NeverDrops()
    {
        var emitter = new ChannelLineageEmitter(capacity: 1);
        await emitter.EmitAsync(Event(1), CancellationToken.None); // fills the buffer

        var pending = emitter.EmitAsync(Event(2), CancellationToken.None).AsTask();
        Assert.False(pending.IsCompleted); // backpressure — not dropped, not yet accepted

        var first = await emitter.Reader.ReadAsync(); // free one slot
        await pending;                                // now the second is accepted
        var second = await emitter.Reader.ReadAsync();

        Assert.Equal(1, first.Locator.RecordSeq);
        Assert.Equal(2, second.Locator.RecordSeq);
    }

    [Fact]
    public async Task Complete_CompletesTheReader()
    {
        var emitter = new ChannelLineageEmitter(capacity: 1);

        emitter.Complete();

        Assert.False(await emitter.Reader.WaitToReadAsync());
    }

    [Fact]
    public void Constructor_InvalidCapacity_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChannelLineageEmitter(0));

    [Fact]
    public async Task EmitAsync_NullEvent_Throws()
    {
        var emitter = new ChannelLineageEmitter(capacity: 1);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await emitter.EmitAsync(null!, CancellationToken.None));
    }
}

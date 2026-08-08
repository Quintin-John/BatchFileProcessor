using Common.FileIngestion.Lineage;
using Common.Messaging.Contracts;

namespace Ingestion.Worker.Tests;

public sealed class LineageDrainServiceTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 10;

    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static LineageEvent Event(long seq) =>
        new("run-1", "FILE1", new RecordLocator(seq, seq * 10, RecordExtent, "TRAN"), LineageState.Consumed, When);

    [Fact]
    public async Task DrainsEmittedEventsToSink()
    {
        var emitter = new ChannelLineageEmitter(capacity: 16);
        var sink = new CapturingSink();
        var service = new LineageDrainService(emitter, sink);

        await emitter.EmitAsync(Event(1), CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => sink.Count >= 1);
        await service.StopAsync(CancellationToken.None);

        Assert.True(sink.Count >= 1);
    }

    [Fact]
    public async Task StopAsync_FlushesBufferedEvents_NoneDropped()
    {
        var emitter = new ChannelLineageEmitter(capacity: 100);
        var sink = new CapturingSink();
        var service = new LineageDrainService(emitter, sink);

        for (var i = 1; i <= 50; i++)
        {
            await emitter.EmitAsync(Event(i), CancellationToken.None);
        }

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None); // completes the emitter and drains the buffer

        Assert.Equal(50, sink.Count); // every buffered event exported before stop returned — never dropped (§6.1/§8)
    }

    [Fact]
    public void Constructor_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LineageDrainService(null!, new CapturingSink()));
        Assert.Throws<ArgumentNullException>(() => new LineageDrainService(new ChannelLineageEmitter(1), null!));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private sealed class CapturingSink : ILineageSink
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task ExportAsync(LineageEvent lineageEvent, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }
    }
}

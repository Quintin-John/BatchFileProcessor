using Common.FileIngestion.Lineage;
using Common.Messaging.Contracts;

namespace Ingestion.Worker.Tests;

public sealed class LineageDrainServiceTests
{
    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static LineageEvent Event(long seq) =>
        new("run-1", "FILE1", new RecordLocator(seq, seq * 10, "TRAN"), LineageState.Consumed, When);

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

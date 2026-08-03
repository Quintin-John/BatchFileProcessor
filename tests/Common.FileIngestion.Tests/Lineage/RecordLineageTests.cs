using Common.FileIngestion.Lineage;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Lineage;

public sealed class RecordLineageTests
{
    private static MessageProvenance Provenance() => new("run", "FILE1", "f.dat", "g266", "4.8");

    private static RecordLocator Locator() => new(1, 0, "TRAN");

    [Fact]
    public async Task EmitAsync_Enabled_EmitsEventWithIdentityAndState()
    {
        var emitter = new CapturingEmitter();
        var lineage = new RecordLineage(emitter, TimeProvider.System, enabled: true);

        await lineage.EmitAsync(Provenance(), Locator(), LineageState.Accepted);

        var e = Assert.Single(emitter.Events);
        Assert.Equal("run", e.CorrelationId);
        Assert.Equal("FILE1", e.FileId);
        Assert.Equal(LineageState.Accepted, e.State);
    }

    [Fact]
    public async Task EmitAsync_Disabled_EmitsNothing()
    {
        var emitter = new CapturingEmitter();
        var lineage = new RecordLineage(emitter, TimeProvider.System, enabled: false);

        // Every transition a record can take must be a no-op when disabled (uniform, all-or-nothing).
        foreach (var state in Enum.GetValues<LineageState>())
        {
            await lineage.EmitAsync(Provenance(), Locator(), state);
        }

        Assert.Empty(emitter.Events); // no emission, and no LineageEvent was allocated
    }

    [Fact]
    public async Task EmitAsync_NullProvenance_Throws_EvenWhenDisabled()
    {
        var lineage = new RecordLineage(new CapturingEmitter(), TimeProvider.System, enabled: false);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await lineage.EmitAsync(null!, Locator(), LineageState.Consumed));
    }

    [Fact]
    public void Constructor_NullEmitter_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RecordLineage(null!, TimeProvider.System, enabled: true));

    [Fact]
    public void Constructor_NullClock_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RecordLineage(new CapturingEmitter(), null!, enabled: true));

    private sealed class CapturingEmitter : ILineageEmitter
    {
        public List<LineageEvent> Events { get; } = [];

        public ValueTask EmitAsync(LineageEvent lineageEvent, CancellationToken cancellationToken)
        {
            Events.Add(lineageEvent);
            return ValueTask.CompletedTask;
        }
    }
}

using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Health;
using Common.FileIngestion.Sources;
using Common.Observability;
using Ingestion.Worker;
using Ingestion.Worker.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Ingestion.Worker.Tests;

public sealed class FolderIngestionWorkerTests
{
    private static WorkerOptions Options() => new("g266", "4.8", TimeSpan.FromMilliseconds(10));

    private static FolderIngestionWorker Worker(
        IFileSource source, FakeDispatcher dispatcher, ReadinessGate gate, TimeProvider? clock = null) =>
        new(source, dispatcher, gate, Options(), NullLogger<FolderIngestionWorker>.Instance, clock ?? TimeProvider.System);

    private static FakeDispatcher FailingOn(string marker) => new()
    {
        Behavior = command => command.SourceKey.Contains(marker, StringComparison.Ordinal)
            ? throw new InvalidOperationException(marker)
            : Task.CompletedTask,
    };

    [Fact]
    public async Task ProcessAsync_CompletesOnSuccess_QuarantinesOnFailure()
    {
        var dispatcher = FailingOn("boom");
        var source = new FakeFileSource();
        var worker = Worker(source, dispatcher, new ReadinessGate());

        await worker.ProcessAsync([new("ok.dat", "p/ok.dat"), new("boom.dat", "p/boom.dat")], CancellationToken.None);

        Assert.Equal(2, dispatcher.Received.Count);
        Assert.Equal("ok.dat", dispatcher.Received[0]);
        Assert.Equal("boom.dat", dispatcher.Received[1]);
        Assert.Equal("ok.dat", Assert.Single(source.Completed));
        Assert.Equal("boom.dat", Assert.Single(source.Failed));
    }

    [Fact]
    public async Task ExecuteAsync_RecoversOrphans_ThenPolls_UntilStopped()
    {
        var dispatcher = new FakeDispatcher();
        var source = new FakeFileSource { Orphans = { new ClaimedFile("orphan.dat", "p/orphan.dat") } };
        var worker = Worker(source, dispatcher, new ReadinessGate());

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => source.Completed.Contains("orphan.dat"));
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains("orphan.dat", dispatcher.Received);
        Assert.Contains("orphan.dat", source.Completed);
    }

    [Fact]
    public async Task Dispatch_MarksReadiness_HealthyOnSuccess_DegradedOnFailure()
    {
        var gate = new ReadinessGate();
        var worker = Worker(new FakeFileSource(), FailingOn("boom"), gate);

        await worker.ProcessAsync([new("ok.dat", "p/ok.dat")], CancellationToken.None);
        Assert.Equal(HealthStatus.Healthy, gate.Status);

        await worker.ProcessAsync([new("boom.dat", "p/boom.dat")], CancellationToken.None);
        Assert.Equal(HealthStatus.Degraded, gate.Status);
    }

    [Fact]
    public async Task Dispatch_OpensCorrelationScope_ThatFlowsToDispatcher_WithMatchingCorrelationId()
    {
        var dispatcher = new FakeDispatcher();
        var worker = Worker(new FakeFileSource(), dispatcher, new ReadinessGate());

        await worker.ProcessAsync([new("ok.dat", "p/ok.dat")], CancellationToken.None);

        Assert.NotNull(dispatcher.ObservedRun); // a scope was active while the command was dispatched
        Assert.Equal(dispatcher.ObservedRun!.CorrelationId, dispatcher.ObservedCommandCorrelationId);
        Assert.Equal(dispatcher.ObservedRun.RunId, dispatcher.ObservedRun.CorrelationId); // fresh run
    }

    [Fact]
    public async Task Dispatch_RestoresCorrelationScope_AfterEachFile_NoAmbientLeak()
    {
        var worker = Worker(new FakeFileSource(), new FakeDispatcher(), new ReadinessGate());

        await worker.ProcessAsync([new("ok.dat", "p/ok.dat")], CancellationToken.None);

        Assert.Null(CorrelationScope.Current); // scope disposed after dispatch; nothing leaks to the caller
    }

    [Fact]
    public async Task Dispatch_NonShutdownCancellation_Quarantines_DoesNotPropagate()
    {
        // A cancellation that is NOT the stopping token (e.g. a downstream timeout surfaced as an OCE) must
        // be quarantined, not propagated — otherwise it would unwind ExecuteAsync and stop the whole host.
        var dispatcher = new FakeDispatcher { Behavior = _ => throw new OperationCanceledException() };
        var source = new FakeFileSource();
        var worker = Worker(source, dispatcher, new ReadinessGate());

        await worker.ProcessAsync([new("x.dat", "p/x.dat")], CancellationToken.None); // must not throw

        Assert.Equal("x.dat", Assert.Single(source.Failed));
        Assert.Empty(source.Completed);
    }

    [Fact]
    public async Task Dispatch_ShutdownCancellation_Rethrows_LeavesClaimForRecovery()
    {
        // Genuine shutdown: the stopping token is cancelled during dispatch. The claim is left in place
        // (neither completed nor failed) so the next run re-offers it, and cancellation unwinds the loop.
        using var cts = new CancellationTokenSource();
        var dispatcher = new FakeDispatcher
        {
            Behavior = _ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
        };
        var source = new FakeFileSource();
        var worker = Worker(source, dispatcher, new ReadinessGate());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => worker.ProcessAsync([new("x.dat", "p/x.dat")], cts.Token));

        Assert.Empty(source.Failed);    // not quarantined
        Assert.Empty(source.Completed); // not completed — left for the next run to recover
    }

    [Fact]
    public async Task ExecuteAsync_PollsAgain_OnlyAfterTheInjectedClockAdvances()
    {
        var clock = new FakeTimeProvider();
        var source = new CountingFileSource();
        var worker = Worker(source, new FakeDispatcher(), new ReadinessGate(), clock);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => source.ClaimCount >= 1); // first poll ran; the loop now waits on the injected clock
        var afterFirstPoll = source.ClaimCount;

        await Task.Delay(50); // real time passes, but the fake clock does not
        Assert.Equal(afterFirstPoll, source.ClaimCount); // no further poll while the clock is frozen

        clock.Advance(TimeSpan.FromMilliseconds(10)); // == the configured poll interval
        await WaitUntil(() => source.ClaimCount > afterFirstPoll);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(source.ClaimCount > afterFirstPoll); // the next poll fired only once the injected clock moved
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private sealed class FakeDispatcher : IIngestFileDispatcher
    {
        public List<string> Received { get; } = [];

        /// <summary>The ambient correlation scope observed at dispatch time (what the pipeline would see).</summary>
        public RunContext? ObservedRun { get; private set; }

        /// <summary>The correlation id carried on the dispatched command.</summary>
        public string? ObservedCommandCorrelationId { get; private set; }

        /// <summary>Optional per-command behaviour; returns/throws to simulate dispatch outcomes.</summary>
        public Func<IngestFile, Task>? Behavior { get; set; }

        public Task DispatchAsync(IngestFile command, CancellationToken cancellationToken)
        {
            ObservedRun = CorrelationScope.Current;
            ObservedCommandCorrelationId = command.CorrelationId;
            Received.Add(command.SourceKey);
            return Behavior?.Invoke(command) ?? Task.CompletedTask;
        }
    }

    private sealed class FakeFileSource : IFileSource
    {
        private readonly List<string> _completed = [];
        private readonly List<string> _failed = [];
        private bool _claimed;

        public List<ClaimedFile> Orphans { get; } = [];
        public IReadOnlyList<string> Completed => _completed;
        public IReadOnlyList<string> Failed => _failed;

        public IReadOnlyList<ClaimedFile> RecoverOrphans() => Orphans;

        public IReadOnlyList<ClaimedFile> Claim()
        {
            if (_claimed)
            {
                return Array.Empty<ClaimedFile>();
            }

            _claimed = true;
            return Array.Empty<ClaimedFile>();
        }

        public void Complete(ClaimedFile file) => _completed.Add(file.Name);

        public void Fail(ClaimedFile file) => _failed.Add(file.Name);
    }

    private sealed class CountingFileSource : IFileSource
    {
        private int _claimCount;

        public int ClaimCount => Volatile.Read(ref _claimCount);

        public IReadOnlyList<ClaimedFile> RecoverOrphans() => Array.Empty<ClaimedFile>();

        public IReadOnlyList<ClaimedFile> Claim()
        {
            Interlocked.Increment(ref _claimCount);
            return Array.Empty<ClaimedFile>();
        }

        public void Complete(ClaimedFile file)
        {
            // no-op: this source never yields files to complete
        }

        public void Fail(ClaimedFile file)
        {
            // no-op: this source never yields files to fail
        }
    }
}

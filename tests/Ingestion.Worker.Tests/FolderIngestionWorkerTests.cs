using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Health;
using Common.FileIngestion.Sources;
using Common.Observability;
using Ingestion.Worker;
using Ingestion.Worker.Messages;
using MassTransit;
using MassTransit.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Ingestion.Worker.Tests;

public sealed class FolderIngestionWorkerTests
{
    private static WorkerOptions Options() => new("g266", "4.8", TimeSpan.FromMilliseconds(10));

    private static (IMediator Mediator, SpyState Spy, ServiceProvider Provider) Mediator()
    {
        var provider = new ServiceCollection()
            .AddSingleton<SpyState>()
            .AddMediator(cfg => cfg.AddConsumer<SpyIngestConsumer>())
            .BuildServiceProvider(true);
        return (provider.GetRequiredService<IMediator>(), provider.GetRequiredService<SpyState>(), provider);
    }

    [Fact]
    public async Task ProcessAsync_CompletesOnSuccess_QuarantinesOnFailure()
    {
        var (mediator, spy, provider) = Mediator();
        await using var _ = provider;
        var source = new FakeFileSource();
        var worker = new FolderIngestionWorker(source, mediator, new ReadinessGate(), Options(), NullLogger<FolderIngestionWorker>.Instance, TimeProvider.System);

        List<ClaimedFile> files = [new("ok.dat", "p/ok.dat"), new("boom.dat", "p/boom.dat")];

        await worker.ProcessAsync(files, CancellationToken.None);

        Assert.Equal(2, spy.Received.Count);
        Assert.Equal("ok.dat", spy.Received[0]);
        Assert.Equal("boom.dat", spy.Received[1]);
        Assert.Equal("ok.dat", Assert.Single(source.Completed));
        Assert.Equal("boom.dat", Assert.Single(source.Failed));
    }

    [Fact]
    public async Task ExecuteAsync_RecoversOrphans_ThenPolls_UntilStopped()
    {
        var (mediator, spy, provider) = Mediator();
        await using var _ = provider;
        var source = new FakeFileSource { Orphans = { new ClaimedFile("orphan.dat", "p/orphan.dat") } };
        var worker = new FolderIngestionWorker(source, mediator, new ReadinessGate(), Options(), NullLogger<FolderIngestionWorker>.Instance, TimeProvider.System);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => source.Completed.Contains("orphan.dat"));
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains("orphan.dat", spy.Received);
        Assert.Contains("orphan.dat", source.Completed);
    }

    [Fact]
    public async Task Dispatch_MarksReadiness_HealthyOnSuccess_DegradedOnFailure()
    {
        var (mediator, _, provider) = Mediator();
        await using var __ = provider;
        var gate = new ReadinessGate();
        var worker = new FolderIngestionWorker(
            new FakeFileSource(), mediator, gate, Options(), NullLogger<FolderIngestionWorker>.Instance, TimeProvider.System);

        await worker.ProcessAsync([new ClaimedFile("ok.dat", "p/ok.dat")], CancellationToken.None);
        Assert.Equal(HealthStatus.Healthy, gate.Status);

        await worker.ProcessAsync([new ClaimedFile("boom.dat", "p/boom.dat")], CancellationToken.None);
        Assert.Equal(HealthStatus.Degraded, gate.Status);
    }

    [Fact]
    public async Task Dispatch_OpensCorrelationScope_ThatFlowsToConsumer_WithMatchingCorrelationId()
    {
        var (mediator, spy, provider) = Mediator();
        await using var _ = provider;
        var worker = new FolderIngestionWorker(
            new FakeFileSource(), mediator, new ReadinessGate(), Options(), NullLogger<FolderIngestionWorker>.Instance, TimeProvider.System);

        await worker.ProcessAsync([new ClaimedFile("ok.dat", "p/ok.dat")], CancellationToken.None);

        Assert.NotNull(spy.ObservedRun); // a scope was active while the pipeline ran
        Assert.Equal(spy.ObservedRun!.CorrelationId, spy.ObservedCommandCorrelationId); // command carries the scope's id
        Assert.Equal(spy.ObservedRun.RunId, spy.ObservedRun.CorrelationId); // fresh run: run id == correlation id
    }

    [Fact]
    public async Task Dispatch_RestoresCorrelationScope_AfterEachFile_NoAmbientLeak()
    {
        var (mediator, _, provider) = Mediator();
        await using var __ = provider;
        var worker = new FolderIngestionWorker(
            new FakeFileSource(), mediator, new ReadinessGate(), Options(), NullLogger<FolderIngestionWorker>.Instance, TimeProvider.System);

        await worker.ProcessAsync([new ClaimedFile("ok.dat", "p/ok.dat")], CancellationToken.None);

        Assert.Null(CorrelationScope.Current); // scope disposed after dispatch; nothing leaks to the caller
    }

    [Fact]
    public async Task ExecuteAsync_PollsAgain_OnlyAfterTheInjectedClockAdvances()
    {
        var (mediator, _, provider) = Mediator();
        await using var __ = provider;
        var clock = new FakeTimeProvider();
        var source = new CountingFileSource();
        var worker = new FolderIngestionWorker(
            source, mediator, new ReadinessGate(), Options(), NullLogger<FolderIngestionWorker>.Instance, clock);

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

    private sealed class SpyState
    {
        private readonly List<string> _received = [];
        public List<string> Received => _received;

        /// <summary>The ambient correlation scope observed inside the consumer (i.e. what the pipeline sees).</summary>
        public RunContext? ObservedRun { get; private set; }

        /// <summary>The correlation id carried on the dispatched command.</summary>
        public string? ObservedCommandCorrelationId { get; private set; }

        public void Record(string name) => _received.Add(name);

        public void Observe(RunContext? run, string commandCorrelationId)
        {
            ObservedRun = run;
            ObservedCommandCorrelationId = commandCorrelationId;
        }
    }

    private sealed class SpyIngestConsumer : IConsumer<IngestFile>
    {
        private readonly SpyState _state;

        public SpyIngestConsumer(SpyState state) => _state = state;

        public Task Consume(ConsumeContext<IngestFile> context)
        {
            _state.Observe(CorrelationScope.Current, context.Message.CorrelationId);
            _state.Record(context.Message.SourceKey);
            return context.Message.SourceKey.Contains("boom", StringComparison.Ordinal)
                ? throw new InvalidOperationException("boom")
                : Task.CompletedTask;
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

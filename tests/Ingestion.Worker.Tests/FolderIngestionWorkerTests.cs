using Common.FileIngestion.Health;
using Common.FileIngestion.Sources;
using Ingestion.Worker;
using Ingestion.Worker.Messages;
using MassTransit;
using MassTransit.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
        var worker = new FolderIngestionWorker(source, mediator, new ReadinessGate(), Options(), NullLogger<FolderIngestionWorker>.Instance);

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
        var worker = new FolderIngestionWorker(source, mediator, new ReadinessGate(), Options(), NullLogger<FolderIngestionWorker>.Instance);

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
            new FakeFileSource(), mediator, gate, Options(), NullLogger<FolderIngestionWorker>.Instance);

        await worker.ProcessAsync([new ClaimedFile("ok.dat", "p/ok.dat")], CancellationToken.None);
        Assert.Equal(HealthStatus.Healthy, gate.Status);

        await worker.ProcessAsync([new ClaimedFile("boom.dat", "p/boom.dat")], CancellationToken.None);
        Assert.Equal(HealthStatus.Degraded, gate.Status);
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
        private readonly List<string> _received = new();
        public IReadOnlyList<string> Received => _received;
        public void Record(string name) => _received.Add(name);
    }

    private sealed class SpyIngestConsumer : IConsumer<IngestFile>
    {
        private readonly SpyState _state;

        public SpyIngestConsumer(SpyState state) => _state = state;

        public Task Consume(ConsumeContext<IngestFile> context)
        {
            _state.Record(context.Message.SourceKey);
            return context.Message.SourceKey.Contains("boom", StringComparison.Ordinal)
                ? throw new InvalidOperationException("boom")
                : Task.CompletedTask;
        }
    }

    private sealed class FakeFileSource : IFileSource
    {
        private readonly List<string> _completed = new();
        private readonly List<string> _failed = new();
        private bool _claimed;

        public List<ClaimedFile> Orphans { get; } = new();
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
}

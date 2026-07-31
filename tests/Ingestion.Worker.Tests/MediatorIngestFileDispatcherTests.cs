using Ingestion.Worker;
using Ingestion.Worker.Messages;
using MassTransit;
using MassTransit.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestion.Worker.Tests;

public sealed class MediatorIngestFileDispatcherTests
{
    private static ServiceProvider Provider() => new ServiceCollection()
        .AddSingleton<Spy>()
        .AddMediator(cfg => cfg.AddConsumer<SpyConsumer>())
        .BuildServiceProvider(true);

    [Fact]
    public async Task DispatchAsync_ForwardsCommand_ToTheMediatorConsumer()
    {
        await using var provider = Provider();
        var dispatcher = new MediatorIngestFileDispatcher(provider.GetRequiredService<IMediator>());

        await dispatcher.DispatchAsync(
            new IngestFile("key", "file", "path", "corr", "prof", "4.8"), CancellationToken.None);

        Assert.Equal("key", Assert.Single(provider.GetRequiredService<Spy>().Received));
    }

    [Fact]
    public void Constructor_NullMediator_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new MediatorIngestFileDispatcher(null!));

    [Fact]
    public async Task DispatchAsync_NullCommand_Throws()
    {
        await using var provider = Provider();
        var dispatcher = new MediatorIngestFileDispatcher(provider.GetRequiredService<IMediator>());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => dispatcher.DispatchAsync(null!, CancellationToken.None));
    }

    private sealed class Spy
    {
        public List<string> Received { get; } = [];
    }

    private sealed class SpyConsumer : IConsumer<IngestFile>
    {
        private readonly Spy _spy;

        public SpyConsumer(Spy spy) => _spy = spy;

        public Task Consume(ConsumeContext<IngestFile> context)
        {
            _spy.Received.Add(context.Message.SourceKey);
            return Task.CompletedTask;
        }
    }
}

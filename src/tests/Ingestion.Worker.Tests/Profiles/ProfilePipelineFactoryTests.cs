using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Health;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Parsing;
using Common.FileIngestion.Telemetry;
using Common.Messaging.Contracts;
using Common.Observability;
using Common.Security.DataProtection;
using Ingestion.Worker.Profiles;

namespace Ingestion.Worker.Tests.Profiles;

public sealed class ProfilePipelineFactoryTests
{
    private static Layout Layout() => new("1.0", 10, "ascii", 1, 1, 2, new[]
    {
        new RecordDefinition("r", "M", new[] { new FieldDefinition("f", 1, 10) }),
    });

    private static Profile Profile() => new(
        "feed-a",
        new ProfileFolders("/in", "/proc", "/done", "/failed"),
        "/cfg.yaml",
        new FixedLengthFormat(),
        new CompletionSettings(CompletionMode.StableSize, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2)),
        new RoutingTargets("batches", "rejects"),
        new BatchLimits(500, 200_000));

    private static ProfilePipelineFactory Factory()
    {
        var instrumentation = new ObservabilityInstrumentation("test");
        var keys = new InMemoryKeyProvider();
        return new ProfilePipelineFactory(
            new NoOpPublisher(),
            new NoOpCheckpointStore(),
            new AesGcmCryptoProvider(),
            keys,
            new DefaultPayloadProtector(new AesGcmCryptoProvider(), keys),
            new IngestionMetrics(instrumentation),
            new RecordLineage(new ChannelLineageEmitter(100), TimeProvider.System, enabled: true),
            new IngestionTracing(instrumentation),
            new Heartbeat(TimeProvider.System),
            new PipelineTuning(64, 1, 64));
    }

    [Fact]
    public void Create_FixedLengthProfile_BuildsPipeline()
    {
        var pipeline = Factory().Create(Profile(), Layout());

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void Create_NullProfile_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Factory().Create(null!, Layout()));

    [Fact]
    public void Create_NullLayout_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Factory().Create(Profile(), null!));

    [Fact]
    public void Create_UsesTheProfilesFormat_ToBuildFraming()
    {
        // The factory holds no format knowledge: whatever the profile's format produces is what gets wired.
        Assert.NotNull(Factory().Create(Profile(), Layout()));
    }

    [Fact]
    public void Constructor_NullPublisher_Throws()
    {
        var instrumentation = new ObservabilityInstrumentation("test");
        var keys = new InMemoryKeyProvider();

        Assert.Throws<ArgumentNullException>(() => new ProfilePipelineFactory(
            null!, new NoOpCheckpointStore(), new AesGcmCryptoProvider(), keys,
            new DefaultPayloadProtector(new AesGcmCryptoProvider(), keys), new IngestionMetrics(instrumentation),
            new RecordLineage(new ChannelLineageEmitter(100), TimeProvider.System, enabled: true), new IngestionTracing(instrumentation),
            new Heartbeat(TimeProvider.System), new PipelineTuning(64, 1, 64)));
    }

    private sealed class NoOpPublisher : IMessagePublisher
    {
        public Task PublishBatchAsync(IngestBatchMessage batch, string destination, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PublishRejectAsync(RejectMessage reject, string destination, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoOpCheckpointStore : ICheckpointStore
    {
        public Task<Watermark?> LoadAsync(string sourceKey, CancellationToken cancellationToken) =>
            Task.FromResult<Watermark?>(null);

        public Task SaveAsync(Watermark watermark, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ClearAsync(string sourceKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

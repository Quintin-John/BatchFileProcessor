using Common.FileIngestion.Abstractions;
using System.Text;
using Common.FileIngestion.Checkpointing;
using Common.FileIngestion.Checkpointing.Redis;
using Common.FileIngestion.Health;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Parsing;
using Common.FileIngestion.Pipeline;
using Common.FileIngestion.Protection;
using Common.FileIngestion.Reading;
using Common.FileIngestion.Rejecting;
using Common.FileIngestion.Sources;
using Common.FileIngestion.Telemetry;
using Common.Messaging.MassTransit;
using Common.Observability;
using Common.Security.DataProtection;
using Ingestion.Worker;
using Ingestion.Worker.Consumers;
using Ingestion.Worker.Health;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

// Composition root — wiring only (excluded from coverage). Fails fast on missing configuration.
var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var ingestion = builder.Configuration.GetSection("Ingestion");
var messaging = builder.Configuration.GetSection("Messaging");

var layout = LayoutLoader.LoadFromFile(RequiredConfig.Text(ingestion, "LayoutPath"));
var policy = LayoutProtectionPolicy.From(layout); // classification comes from the layout's encrypt flags
var encoding = Encoding.GetEncoding(layout.Encoding);

// Field-level data protection. InMemory key provider is dev/POC only — production wires a Key Vault provider.
services.AddDataProtection(policy);
services.AddInMemoryKeyProvider();

// Ingestion collaborators (record length, terminator, encoding, and layout version are derived from the layout).
services.AddSingleton(TimeProvider.System);
services.AddSingleton<IRecordParser>(new FixedLengthRecordParser(layout));
services.AddSingleton(new StreamRecordReader(layout.RecordLength, layout.TerminatorLength, encoding));
services.AddObservability(builder.Configuration.GetSection("Observability")); // binds name/version, registers OTel export
services.AddSingleton<IngestionMetrics>();
services.AddSingleton<IngestionTracing>();

// Per-record lineage (§8): bounded channel emitter, drained off the hot path to a structured-log sink.
services.AddSingleton(new ChannelLineageEmitter(RequiredConfig.Integer(ingestion, "LineageChannelCapacity")));
services.AddSingleton<ILineageEmitter>(sp => sp.GetRequiredService<ChannelLineageEmitter>());
services.AddSingleton<RecordLineage>();
services.AddSingleton<ILineageSink, StructuredLogLineageSink>();
services.AddHostedService<LineageDrainService>();

services.AddSingleton<Heartbeat>();
services.AddSingleton(new IngestionOptions(
    RequiredConfig.Integer(ingestion, "MaxRecordsPerBatch"), RequiredConfig.Integer(ingestion, "MaxContentBytesPerBatch"),
    RequiredConfig.Integer(ingestion, "BatchChannelCapacity"), RequiredConfig.Integer(ingestion, "PublisherConcurrency"),
    RequiredConfig.Integer(ingestion, "PublisherConfirmWindow")));
services.AddSingleton<RecordProtector>();
services.AddSingleton(sp => ActivatorUtilities.CreateInstance<RejectSink>(sp, RequiredConfig.Text(messaging, "RejectDestination")));
// Checkpoint store selected by config, fail-closed. File = same-volume resume; Redis = cross-instance.
ICheckpointStore checkpointStore = RequiredConfig.Enum<CheckpointProvider>(ingestion, "CheckpointProvider") switch
{
    CheckpointProvider.File => new FileCheckpointStore(RequiredConfig.Text(ingestion, "CheckpointDirectory")),
    CheckpointProvider.Redis => new RedisCheckpointStore(
        await ConnectionMultiplexer.ConnectAsync(RequiredConfig.Text(ingestion, "RedisConnectionString")),
        RequiredConfig.Text(ingestion, "CheckpointKeyPrefix")),
    _ => throw new InvalidOperationException("Unsupported checkpoint provider."),
};
services.AddSingleton(checkpointStore);
services.AddSingleton(sp => ActivatorUtilities.CreateInstance<FileIngestionPipeline>(sp, RequiredConfig.Text(messaging, "Destination")));
var root = RequiredConfig.Text(ingestion, "RootDirectory");
var completionGuard = new StableSizeCompletionGuard(
    TimeSpan.FromSeconds(RequiredConfig.Integer(ingestion, "CompletionQuietSeconds")), TimeProvider.System);
services.AddSingleton<IFileSource>(sp => new FolderFileSource(
    Path.Combine(root, "incoming"),
    Path.Combine(root, "processing"),
    Path.Combine(root, "done"),
    Path.Combine(root, "failed"),
    completionGuard,
    sp.GetRequiredService<ILogger<FolderFileSource>>()));
services.AddSingleton(new WorkerOptions(
    RequiredConfig.Text(ingestion, "ProfileId"), layout.Version, TimeSpan.FromSeconds(RequiredConfig.Integer(ingestion, "PollIntervalSeconds"))));

// Health: liveness from heartbeat staleness, readiness from the publish-outcome gate.
services.AddSingleton(sp => new LivenessProbe(
    sp.GetRequiredService<Heartbeat>(), TimeSpan.FromSeconds(RequiredConfig.Integer(ingestion, "LivenessStalenessSeconds"))));
services.AddSingleton<ReadinessGate>();
string[] liveTags = [HealthTags.Live];
string[] readyTags = [HealthTags.Ready];
services.AddHealthChecks()
    .AddCheck<LivenessHealthCheck>("liveness", tags: liveTags)
    .AddCheck<ReadinessHealthCheck>("readiness", tags: readyTags);

// Bus publishes batches/rejects to the broker; mediator dispatches IngestFile in-process.
// retry/circuit-breaker policy is config, not hardcoded; Get<T> binds into the immutable options.
var resilience = messaging.GetSection("Resilience").Get<MessagingResilienceOptions>() ?? new MessagingResilienceOptions();
services.AddMessaging(
    new MessagingTransportOptions
    {
        Transport = RequiredConfig.Enum<MessagingTransport>(messaging, "Transport"),
        ConnectionString = RequiredConfig.Text(messaging, "ConnectionString"),
        EndpointPrefix = messaging["EndpointPrefix"],
    },
    resilience);
services.AddMediator(cfg => cfg.AddConsumer<IngestFileConsumer>());
services.AddSingleton<IIngestFileDispatcher, MediatorIngestFileDispatcher>();

services.AddHostedService<FolderIngestionWorker>();

var app = builder.Build();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Live) });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Ready) });
await app.RunAsync().ConfigureAwait(false);

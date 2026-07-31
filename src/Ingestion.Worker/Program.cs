using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Checkpointing;
using Common.FileIngestion.Checkpointing.Redis;
using Common.FileIngestion.Health;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Sources;
using Common.FileIngestion.Telemetry;
using Common.Messaging.MassTransit;
using Common.Observability;
using Common.Security.DataProtection;
using Ingestion.Worker;
using Ingestion.Worker.Health;
using Ingestion.Worker.Profiles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

// Composition root — wiring only (excluded from coverage). Fails fast on missing configuration.
// One isolated worker + pipeline is built per operational profile and run concurrently, so files in
// different folders never contend. profiles.yaml owns routing (folders/layout/format/completion/
// destinations); appsettings owns shared infra (checkpoint, tuning, broker, observability); layout YAML
// owns parsing/mapping. Secrets/connection strings live in appsettings/Key Vault, never in profiles.yaml.
var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var ingestion = builder.Configuration.GetSection("Ingestion");
var messaging = builder.Configuration.GetSection("Messaging");

var profiles = ProfileLoader.LoadFromFile(RequiredConfig.Text(ingestion, "ProfilesPath"));

// Load each profile's layout once, up front (fail-fast), and reuse it for pipeline wiring and log redaction.
var layoutsByProfile = profiles.Profiles.ToDictionary(
    profile => profile.Name, profile => LayoutLoader.LoadFromFile(profile.LayoutPath), StringComparer.Ordinal);

services.AddSingleton(TimeProvider.System);

// Redact encrypt-flagged field values from every log (layout-driven; no hardcoded field names). Must run
// after the host's default logging is registered, which WebApplication.CreateBuilder has already done.
services.AddSensitiveKeyRedaction(SensitiveFieldNames.From(layoutsByProfile.Values));

// Field-level data protection, shared across profiles. Each profile's field protector is built per-layout
// by the pipeline factory; the crypto primitives below are the shared building blocks. InMemory key
// provider is dev/POC only — production wires a Key Vault provider.
services.AddSingleton<ICryptoProvider, AesGcmCryptoProvider>();
services.AddInMemoryKeyProvider();
services.AddSingleton<IPayloadProtector, DefaultPayloadProtector>();

// Shared telemetry.
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

// Shared pipeline tuning (infra), applied to every profile; per-batch limits are per-profile (profiles.yaml).
services.AddSingleton(new PipelineTuning(
    RequiredConfig.Integer(ingestion, "BatchChannelCapacity"),
    RequiredConfig.Integer(ingestion, "PublisherConcurrency"),
    RequiredConfig.Integer(ingestion, "PublisherConfirmWindow")));

// Checkpoint store selected by config, fail-closed and shared across profiles (keys are profile-namespaced).
// File = same-volume resume; Redis = cross-instance.
ICheckpointStore checkpointStore = RequiredConfig.Enum<CheckpointProvider>(ingestion, "CheckpointProvider") switch
{
    CheckpointProvider.File => new FileCheckpointStore(RequiredConfig.Text(ingestion, "CheckpointDirectory")),
    CheckpointProvider.Redis => new RedisCheckpointStore(
        await ConnectionMultiplexer.ConnectAsync(RequiredConfig.Text(ingestion, "RedisConnectionString")),
        RequiredConfig.Text(ingestion, "CheckpointKeyPrefix")),
    _ => throw new InvalidOperationException("Unsupported checkpoint provider."),
};
services.AddSingleton(checkpointStore);

// Bus publishes batches and rejects to the broker (this service is a producer only). The send-retry policy
// is bound from configuration rather than hardcoded, falling back to the option defaults when absent.
var resilience = messaging.GetSection("Resilience").Get<MessagingResilienceOptions>() ?? new MessagingResilienceOptions();
services.AddMessaging(
    new MessagingTransportOptions
    {
        Transport = RequiredConfig.Enum<MessagingTransport>(messaging, "Transport"),
        ConnectionString = RequiredConfig.Text(messaging, "ConnectionString"),
        EndpointPrefix = messaging["EndpointPrefix"],
    },
    resilience);

// Builds a fully-wired pipeline per profile from the shared collaborators above.
services.AddSingleton<ProfilePipelineFactory>();

// Health: liveness from heartbeat staleness, readiness from the publish-outcome gate. Shared across profiles.
services.AddSingleton(sp => new LivenessProbe(
    sp.GetRequiredService<Heartbeat>(), TimeSpan.FromSeconds(RequiredConfig.Integer(ingestion, "LivenessStalenessSeconds"))));
services.AddSingleton<ReadinessGate>();
string[] liveTags = [HealthTags.Live];
string[] readyTags = [HealthTags.Ready];
services.AddHealthChecks()
    .AddCheck<LivenessHealthCheck>("liveness", tags: liveTags)
    .AddCheck<ReadinessHealthCheck>("readiness", tags: readyTags);

// One worker + pipeline per profile, each hosted independently so the host runs them concurrently. The
// pipeline (parser/protection/destinations) is built from the profile and its own layout; the folder source
// gets the profile's four directories and its completion guard; IngestFile is dispatched straight to that
// profile's pipeline. Checkpoint keys are namespaced by profile name inside the worker.
foreach (var profile in profiles.Profiles)
{
    var layout = layoutsByProfile[profile.Name];
    services.AddSingleton<IHostedService>(sp =>
    {
        var pipeline = sp.GetRequiredService<ProfilePipelineFactory>().Create(profile, layout);
        var completionGuard = new StableSizeCompletionGuard(profile.Completion.QuietPeriod, sp.GetRequiredService<TimeProvider>());
        var source = new FolderFileSource(
            profile.Folders.Incoming,
            profile.Folders.Processing,
            profile.Folders.Done,
            profile.Folders.Failed,
            completionGuard,
            sp.GetRequiredService<ILogger<FolderFileSource>>());
        var options = new WorkerOptions(profile.Name, layout.Version, profile.Completion.PollInterval);

        return new FolderIngestionWorker(
            source,
            new PipelineIngestFileDispatcher(pipeline),
            sp.GetRequiredService<ReadinessGate>(),
            options,
            sp.GetRequiredService<ILogger<FolderIngestionWorker>>(),
            sp.GetRequiredService<TimeProvider>());
    });
}

var app = builder.Build();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Live) });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Ready) });
await app.RunAsync().ConfigureAwait(false);

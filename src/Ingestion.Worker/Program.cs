using System.Text;
using Common.FileIngestion.Checkpointing;
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

// Composition root — wiring only (excluded from coverage). Fails fast on missing configuration.
var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var ingestion = builder.Configuration.GetSection("Ingestion");
var messaging = builder.Configuration.GetSection("Messaging");

var layout = LayoutLoader.LoadFromFile(Required(ingestion, "LayoutPath"));
var policy = DataProtectionPolicyLoader.LoadFromFile(Required(ingestion, "DataProtectionPolicyPath"));
var encoding = Encoding.GetEncoding(layout.Encoding);

// Field-level data protection. InMemory key provider is dev/POC only — production wires a Key Vault provider.
services.AddDataProtection(policy);
services.AddInMemoryKeyProvider();

// Ingestion collaborators (record length, encoding, and layout version are derived from the layout).
services.AddSingleton(TimeProvider.System);
services.AddSingleton<IRecordParser>(new FixedLengthRecordParser(layout));
services.AddSingleton(new StreamRecordReader(layout.RecordLength, ingestion.GetValue<int>("TerminatorLength"), encoding));
services.AddObservability(builder.Configuration.GetSection("Observability")); // binds name/version, registers OTel export
services.AddSingleton<IngestionMetrics>();
services.AddSingleton<IngestionTracing>();

// Per-record lineage (§8): bounded channel emitter, drained off the hot path to a structured-log sink.
services.AddSingleton(new ChannelLineageEmitter(ingestion.GetValue<int>("LineageChannelCapacity")));
services.AddSingleton<ILineageEmitter>(sp => sp.GetRequiredService<ChannelLineageEmitter>());
services.AddSingleton<RecordLineage>();
services.AddSingleton<ILineageSink, StructuredLogLineageSink>();
services.AddHostedService<LineageDrainService>();

services.AddSingleton<Heartbeat>();
services.AddSingleton(new IngestionOptions(
    ingestion.GetValue<int>("MaxRecordsPerBatch"), ingestion.GetValue<int>("MaxContentBytesPerBatch")));
services.AddSingleton<RecordProtector>();
services.AddSingleton<RejectSink>();
services.AddSingleton<ICheckpointStore>(new FileCheckpointStore(Required(ingestion, "CheckpointDirectory")));
services.AddSingleton<FileIngestionPipeline>();
services.AddSingleton<IFileSource>(_ => new FolderFileSource(Required(ingestion, "RootDirectory")));
services.AddSingleton(new WorkerOptions(
    Required(ingestion, "ProfileId"), layout.Version, TimeSpan.FromSeconds(ingestion.GetValue<int>("PollIntervalSeconds"))));

// Health: liveness from heartbeat staleness, readiness from the publish-outcome gate.
services.AddSingleton(sp => new LivenessProbe(
    sp.GetRequiredService<Heartbeat>(), TimeSpan.FromSeconds(ingestion.GetValue<int>("LivenessStalenessSeconds"))));
services.AddSingleton<ReadinessGate>();
string[] liveTags = ["live"];
string[] readyTags = ["ready"];
services.AddHealthChecks()
    .AddCheck<LivenessHealthCheck>("liveness", tags: liveTags)
    .AddCheck<ReadinessHealthCheck>("readiness", tags: readyTags);

// Bus publishes batches/rejects to the broker; mediator dispatches IngestFile in-process.
var resilience = new MessagingResilienceOptions();
messaging.GetSection("Resilience").Bind(resilience); // retry/circuit-breaker policy is config, not hardcoded
services.AddMessaging(
    new MessagingTransportOptions
    {
        Transport = messaging.GetValue<MessagingTransport>("Transport"),
        ConnectionString = Required(messaging, "ConnectionString"),
        EndpointPrefix = messaging["EndpointPrefix"],
    },
    resilience);
services.AddMediator(cfg => cfg.AddConsumer<IngestFileConsumer>());

services.AddHostedService<FolderIngestionWorker>();

var app = builder.Build();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
await app.RunAsync().ConfigureAwait(false);

static string Required(IConfigurationSection section, string key) =>
    section[key] ?? throw new InvalidOperationException($"Missing required configuration '{section.Key}:{key}'.");

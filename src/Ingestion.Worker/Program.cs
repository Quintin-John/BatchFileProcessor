using System.Text;
using Common.FileIngestion.Checkpointing;
using Common.FileIngestion.Health;
using Common.FileIngestion.Layouts;
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
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Composition root — wiring only (excluded from coverage). Fails fast on missing configuration.
var builder = Host.CreateApplicationBuilder(args);
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

await builder.Build().RunAsync().ConfigureAwait(false);

static string Required(IConfigurationSection section, string key) =>
    section[key] ?? throw new InvalidOperationException($"Missing required configuration '{section.Key}:{key}'.");

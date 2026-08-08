using System.Diagnostics.CodeAnalysis;
using System.Text;
using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Health;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Parsing;
using Common.FileIngestion.Pipeline;
using Common.FileIngestion.Protection;
using Common.FileIngestion.Reading;
using Common.FileIngestion.Rejecting;
using Common.FileIngestion.Telemetry;
using Common.Messaging.Contracts;
using Common.Security.DataProtection;

namespace Ingestion.Worker.Profiles;

/// <summary>
/// Builds a fully-wired <see cref="FileIngestionPipeline"/> for one profile: the parser is selected from
/// the profile's format (fail-closed on an unsupported format), the field-protection policy is derived from
/// the profile's own layout, and batch/reject destinations come from the profile's routing. Cross-cutting
/// collaborators (publisher, checkpoint store, crypto, telemetry, heartbeat, tuning) are shared across
/// profiles and injected once. Composition only — no ingestion/business logic.
/// </summary>
internal sealed class ProfilePipelineFactory
{
    private readonly IMessagePublisher _publisher;
    private readonly ICheckpointStore _checkpointStore;
    private readonly ICryptoProvider _crypto;
    private readonly IKeyProvider _keys;
    private readonly IPayloadProtector _payloadProtector;
    private readonly IngestionMetrics _metrics;
    private readonly RecordLineage _lineage;
    private readonly IngestionTracing _tracing;
    private readonly Heartbeat _heartbeat;
    private readonly PipelineTuning _tuning;

    /// <summary>Creates the factory from the shared, cross-profile collaborators.</summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
        Justification = "Composition-root factory coordinating distinct single-responsibility shared services; " +
                        "bundling them into a parameterless data bag would add no invariant (a wrapper smell). " +
                        "Wired once at the composition root; not a public call surface.")]
    public ProfilePipelineFactory(
        IMessagePublisher publisher,
        ICheckpointStore checkpointStore,
        ICryptoProvider crypto,
        IKeyProvider keys,
        IPayloadProtector payloadProtector,
        IngestionMetrics metrics,
        RecordLineage lineage,
        IngestionTracing tracing,
        Heartbeat heartbeat,
        PipelineTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(crypto);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(payloadProtector);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(heartbeat);
        ArgumentNullException.ThrowIfNull(tuning);

        _publisher = publisher;
        _checkpointStore = checkpointStore;
        _crypto = crypto;
        _keys = keys;
        _payloadProtector = payloadProtector;
        _metrics = metrics;
        _lineage = lineage;
        _tracing = tracing;
        _heartbeat = heartbeat;
        _tuning = tuning;
    }

    /// <summary>Builds the pipeline for a profile using its loaded layout.</summary>
    /// <param name="profile">The profile to build for; required.</param>
    /// <param name="layout">The profile's loaded layout; required.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public FileIngestionPipeline Create(Profile profile, ILayout layout)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(layout);

        // Reader and parser come from the profile's format as a pair, so framing and mapping cannot disagree
        // about the file. Nothing here knows which format that is.
        var (reader, parser) = profile.Format.CreateFraming(layout, LayoutEncoding.Resolve(layout.Encoding));

        // Field protection is derived from this profile's own layout, so each profile encrypts its own set.
        var fieldProtector = new DefaultFieldProtector(_crypto, _keys, LayoutProtectionPolicy.From(layout));
        var protector = new RecordProtector(fieldProtector, _payloadProtector);
        var rejectSink = new RejectSink(_publisher, profile.Routing.Rejects);

        var options = new IngestionOptions(
            profile.Batch.MaxRecords, profile.Batch.MaxContentBytes,
            _tuning.BatchChannelCapacity, _tuning.PublisherConcurrency, _tuning.PublisherConfirmWindow);

        return new FileIngestionPipeline(
            reader, parser, protector, _publisher, rejectSink, _checkpointStore,
            _metrics, _lineage, _tracing, _heartbeat, options, profile.Routing.Batches);
    }
}

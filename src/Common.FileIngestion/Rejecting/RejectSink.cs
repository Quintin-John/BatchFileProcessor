using Common.Messaging.Contracts;

namespace Common.FileIngestion.Rejecting;

/// <summary>
/// Routes a quarantined record to the reject transport. Assembles a <see cref="RejectMessage"/> with
/// a deterministic id (<c>{FileId}-{RecordSeq}-reject</c>) from the record's provenance, location,
/// raw content, and field-level failures, then publishes it via confirmed delivery — a broker
/// failure faults the returned task (fail-closed).
/// </summary>
public sealed class RejectSink
{
    private const char IdSeparator = '-';
    private const string IdSuffix = "reject";

    private readonly IMessagePublisher _publisher;
    private readonly string _destination;

    /// <summary>Creates a reject sink.</summary>
    /// <param name="publisher">The confirmed-delivery publisher; required.</param>
    /// <param name="destination">Reject destination queue/topic name; required, non-blank.</param>
    /// <exception cref="ArgumentNullException"><paramref name="publisher"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is blank.</exception>
    public RejectSink(IMessagePublisher publisher, string destination)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        _publisher = publisher;
        _destination = destination;
    }

    /// <summary>Builds and publishes the reject message for a quarantined record.</summary>
    /// <param name="provenance">Source/run provenance; required.</param>
    /// <param name="locator">Where the rejected record sits in its source file; required.</param>
    /// <param name="rawRecord">Original record content (clear or encrypted); required.</param>
    /// <param name="reasons">Field-level failures; required, non-empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public Task RejectAsync(
        MessageProvenance provenance,
        RecordLocator locator,
        FieldValue rawRecord,
        IReadOnlyList<RejectReason> reasons,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(rawRecord);
        ArgumentNullException.ThrowIfNull(reasons);

        var messageId = $"{provenance.FileId}{IdSeparator}{locator.RecordSeq}{IdSeparator}{IdSuffix}";
        var message = new RejectMessage(messageId, provenance, locator, rawRecord, reasons);
        return _publisher.PublishRejectAsync(message, _destination, cancellationToken);
    }
}

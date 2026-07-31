using System.Diagnostics.Metrics;
using Common.Observability;

namespace Common.FileIngestion.Telemetry;

/// <summary>
/// The ingestion pipeline's metric instruments: monotonic counters for records parsed/rejected
/// (dimensioned by record type), batches confirmed, and bytes read. One reason to change: the set
/// of ingestion counters. Built over a component's <see cref="ObservabilityInstrumentation"/>.
/// </summary>
public sealed class IngestionMetrics
{
    /// <summary>Counter name for records successfully parsed and accepted.</summary>
    public const string RecordsParsedName = "ingestion.records.parsed";

    /// <summary>Counter name for records quarantined to the reject queue.</summary>
    public const string RecordsRejectedName = "ingestion.records.rejected";

    /// <summary>Counter name for batches the broker confirmed accepting.</summary>
    public const string BatchesPublishedName = "ingestion.batches.published";

    /// <summary>Counter name for bytes read from source files.</summary>
    public const string BytesReadName = "ingestion.bytes.read";

    private const string RecordUnit = "{record}";
    private const string BatchUnit = "{batch}";
    private const string ByteUnit = "By";

    private readonly Counter<long> _recordsParsed;
    private readonly Counter<long> _recordsRejected;
    private readonly Counter<long> _batchesPublished;
    private readonly Counter<long> _bytesRead;

    /// <summary>Creates the ingestion counters on the given instrumentation.</summary>
    /// <param name="instrumentation">The component's telemetry sources; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="instrumentation"/> is null.</exception>
    public IngestionMetrics(ObservabilityInstrumentation instrumentation)
    {
        ArgumentNullException.ThrowIfNull(instrumentation);

        _recordsParsed = instrumentation.CreateCounter(RecordsParsedName, RecordUnit, "Records successfully parsed and accepted.");
        _recordsRejected = instrumentation.CreateCounter(RecordsRejectedName, RecordUnit, "Records quarantined to the reject queue.");
        _batchesPublished = instrumentation.CreateCounter(BatchesPublishedName, BatchUnit, "Batches confirmed accepted by the broker.");
        _bytesRead = instrumentation.CreateCounter(BytesReadName, ByteUnit, "Bytes read from source files.");
    }

    /// <summary>Records one accepted record of the given type.</summary>
    /// <param name="recordType">The record type; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="recordType"/> is blank.</exception>
    public void RecordParsed(string recordType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        _recordsParsed.Add(1, new KeyValuePair<string, object?>(IngestionTelemetryTags.RecordType, recordType));
    }

    /// <summary>Records one rejected record of the given type.</summary>
    /// <param name="recordType">The record type; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="recordType"/> is blank.</exception>
    public void RecordRejected(string recordType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        _recordsRejected.Add(1, new KeyValuePair<string, object?>(IngestionTelemetryTags.RecordType, recordType));
    }

    /// <summary>Records one broker-confirmed batch.</summary>
    public void BatchPublished() => _batchesPublished.Add(1);

    /// <summary>Records bytes read from a source file.</summary>
    /// <param name="bytes">Byte count; must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bytes"/> is negative.</exception>
    public void BytesRead(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        _bytesRead.Add(bytes);
    }
}

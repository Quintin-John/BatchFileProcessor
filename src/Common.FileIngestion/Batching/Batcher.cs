using Common.Messaging.Contracts;

namespace Common.FileIngestion.Batching;

/// <summary>
/// Accumulates records into <see cref="IngestBatchMessage"/> batches, sealing one when it reaches
/// the record count or the estimated content-byte cap. Deterministic ids: <c>{FileId}-{BatchSeq}</c>,
/// batch sequence 0-based. Stateful and not thread-safe — one instance drives one file's read loop.
/// </summary>
public sealed class Batcher
{
    private const char IdSeparator = '-';

    private readonly int _maxRecords;
    private readonly int _maxContentBytes;
    private readonly MessageProvenance _provenance;
    private readonly List<IngestRecord> _pending;
    private long _estimatedBytes;
    private long _batchSeq;

    /// <summary>Creates a batcher.</summary>
    /// <param name="maxRecords">Max records per batch; must be at least 1.</param>
    /// <param name="maxContentBytes">Max estimated content bytes per batch (set below the transport limit); at least 1.</param>
    /// <param name="provenance">Provenance stamped on every batch; required.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRecords"/> or <paramref name="maxContentBytes"/> is less than 1.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="provenance"/> is null.</exception>
    public Batcher(int maxRecords, int maxContentBytes, MessageProvenance provenance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRecords, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxContentBytes, 1);
        ArgumentNullException.ThrowIfNull(provenance);

        _maxRecords = maxRecords;
        _maxContentBytes = maxContentBytes;
        _provenance = provenance;
        _pending = new List<IngestRecord>(maxRecords);
    }

    /// <summary>Adds a record, returning a sealed batch if this add completed one; otherwise null.</summary>
    /// <param name="record">The record to add; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public IngestBatchMessage? Add(IngestRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        _pending.Add(record);
        _estimatedBytes += EstimateContentBytes(record);

        return _pending.Count >= _maxRecords || _estimatedBytes >= _maxContentBytes ? Seal() : null;
    }

    /// <summary>Seals any pending records into a final batch, or returns null if none are pending.</summary>
    public IngestBatchMessage? Flush() => _pending.Count > 0 ? Seal() : null;

    private IngestBatchMessage Seal()
    {
        var seq = _batchSeq;
        var messageId = $"{_provenance.FileId}{IdSeparator}{seq}";
        var batch = new IngestBatchMessage(messageId, _provenance, seq, _pending.ToArray());

        _batchSeq++;
        _pending.Clear();
        _estimatedBytes = 0;
        return batch;
    }

    private static long EstimateContentBytes(IngestRecord record)
    {
        long bytes = 0;
        foreach (var pair in record.Fields)
        {
            bytes += pair.Key.Length + ValueLength(pair.Value);
        }

        return bytes;
    }

    private static long ValueLength(FieldValue value) => value switch
    {
        ClearFieldValue clear => clear.Value?.ToString()?.Length ?? 0,
        EncryptedFieldValue encrypted =>
            encrypted.Value.Ciphertext.Length + encrypted.Value.Nonce.Length + encrypted.Value.Tag.Length,
        _ => 0,
    };
}

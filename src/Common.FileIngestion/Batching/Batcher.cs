using System.Text.Json;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Batching;

/// <summary>
/// Accumulates records into <see cref="IngestBatchMessage"/> batches, sealing one when it reaches the
/// record count or would exceed the content-byte cap. Each record's contribution is the exact size of
/// its serialized wire form (via <see cref="MessagingJson.Options"/>, the same options the transport
/// uses), not a proxy — so a batch never overruns the transport limit through an undercount. The cap
/// is applied <em>before</em> a record is admitted (seal-before-exceed); only a single record larger
/// than the whole cap forms an over-cap batch on its own. Deterministic ids: <c>{FileId}-{BatchSeq}</c>,
/// batch sequence 0-based. Stateful and not thread-safe — one instance drives one file's read loop.
/// </summary>
public sealed class Batcher
{
    private const char IdSeparator = '-';

    private readonly int _maxRecords;
    private readonly int _maxContentBytes;
    private readonly MessageProvenance _provenance;
    private readonly List<IngestRecord> _pending;
    private long _accumulatedBytes;
    private long _batchSeq;

    /// <summary>Creates a batcher.</summary>
    /// <param name="maxRecords">Max records per batch; must be at least 1.</param>
    /// <param name="maxContentBytes">Max serialized record bytes per batch (set below the transport limit, leaving margin for the fixed batch envelope); at least 1.</param>
    /// <param name="provenance">Provenance stamped on every batch; required.</param>
    /// <param name="firstBatchSeq">Sequence for the first sealed batch; on resume this continues past the
    /// last confirmed batch so message ids never collide with already-published batches. Non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRecords"/> or <paramref name="maxContentBytes"/> is less than 1, or <paramref name="firstBatchSeq"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="provenance"/> is null.</exception>
    public Batcher(int maxRecords, int maxContentBytes, MessageProvenance provenance, long firstBatchSeq = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRecords, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxContentBytes, 1);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentOutOfRangeException.ThrowIfNegative(firstBatchSeq);

        _maxRecords = maxRecords;
        _maxContentBytes = maxContentBytes;
        _provenance = provenance;
        _pending = new List<IngestRecord>(maxRecords);
        _batchSeq = firstBatchSeq;
    }

    /// <summary>Adds a record, returning a sealed batch if this add completed one; otherwise null.</summary>
    /// <param name="record">The record to add; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public IngestBatchMessage? Add(IngestRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var recordBytes = MeasureAndCache(record);

        // Seal the in-progress batch before this record would push it past the byte cap, so a batch
        // never exceeds the transport limit; this record then opens the next batch.
        if (_pending.Count > 0 && _accumulatedBytes + recordBytes > _maxContentBytes)
        {
            var sealedBatch = Seal();
            _pending.Add(record);
            _accumulatedBytes = recordBytes;
            return sealedBatch;
        }

        _pending.Add(record);
        _accumulatedBytes += recordBytes;

        // Record-count cap, or a single record that alone meets/exceeds the byte cap.
        return _pending.Count >= _maxRecords || _accumulatedBytes >= _maxContentBytes ? Seal() : null;
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
        _accumulatedBytes = 0;
        return batch;
    }

    // Serializes the record once, caches those bytes on the record (reused verbatim at publish so it is not
    // serialized again), and returns their length for the byte-cap decision — the exact wire size, since the
    // same MessagingJson options drive both this and the transport.
    private static long MeasureAndCache(IngestRecord record)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, MessagingJson.Options);
        record.SerializedForm = bytes;
        return bytes.Length;
    }
}

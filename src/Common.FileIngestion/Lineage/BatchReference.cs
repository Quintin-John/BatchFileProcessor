namespace Common.FileIngestion.Lineage;

/// <summary>
/// Identifies the batch a record was placed into: its 0-based sequence within the file and its
/// deterministic message id. The two travel together (both unknown until a record is batched), so
/// they are one value rather than two loose parameters.
/// </summary>
public sealed record BatchReference
{
    /// <summary>0-based batch sequence within the file.</summary>
    public long BatchSeq { get; }

    /// <summary>Deterministic batch message id.</summary>
    public string MessageId { get; }

    /// <summary>Creates a validated batch reference.</summary>
    /// <param name="batchSeq">Batch sequence; non-negative.</param>
    /// <param name="messageId">Batch message id; required, non-blank.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchSeq"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="messageId"/> is blank.</exception>
    public BatchReference(long batchSeq, string messageId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batchSeq);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        BatchSeq = batchSeq;
        MessageId = messageId;
    }
}

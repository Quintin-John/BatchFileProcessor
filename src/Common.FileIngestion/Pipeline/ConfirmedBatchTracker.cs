namespace Common.FileIngestion.Pipeline;

/// <summary>
/// Tracks which batches the broker has confirmed and computes how far the watermark may safely advance
/// when publishers confirm out of order (design §3: no cross-publisher ordering). The watermark may
/// only move across the <em>contiguous</em> confirmed prefix: a confirmed batch beyond an unconfirmed
/// gap is held until the gap fills, so a crash never resumes past an unconfirmed record. Thread-safe —
/// concurrent publishers confirm through one instance.
/// </summary>
public sealed class ConfirmedBatchTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<long, BatchPosition> _confirmedAhead = new();
    private long _nextExpectedSeq;

    /// <summary>Creates a tracker expecting the first batch at <paramref name="firstBatchSeq"/>.</summary>
    /// <param name="firstBatchSeq">Sequence of the first batch this run will publish; non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="firstBatchSeq"/> is negative.</exception>
    public ConfirmedBatchTracker(long firstBatchSeq)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstBatchSeq);
        _nextExpectedSeq = firstBatchSeq;
    }

    /// <summary>
    /// Records a confirmed batch and returns the new watermark position if the contiguous confirmed
    /// prefix advanced; otherwise null (this batch is confirmed but sits beyond an unconfirmed gap).
    /// </summary>
    /// <param name="position">The confirmed batch's position; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="position"/> is null.</exception>
    public BatchPosition? Confirm(BatchPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        lock (_gate)
        {
            if (position.BatchSeq < _nextExpectedSeq)
            {
                return null; // already advanced past this batch (each batch confirms once)
            }

            _confirmedAhead[position.BatchSeq] = position;

            BatchPosition? advanced = null;
            while (_confirmedAhead.Remove(_nextExpectedSeq, out var next))
            {
                advanced = next;
                _nextExpectedSeq++;
            }

            return advanced;
        }
    }
}

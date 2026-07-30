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
    private readonly Dictionary<long, BatchPosition> _confirmedAhead = [];
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
    /// Records a confirmed batch and reports how far the contiguous confirmed prefix advanced: the new
    /// watermark position and how many batches became contiguous (0 when this batch sits beyond an
    /// unconfirmed gap). The count lets the caller release exactly that many confirm-window slots.
    /// </summary>
    /// <param name="position">The confirmed batch's position; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="position"/> is null.</exception>
    public ConfirmResult Confirm(BatchPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        lock (_gate)
        {
            if (position.BatchSeq < _nextExpectedSeq)
            {
                return default; // already advanced past this batch (each batch confirms once)
            }

            _confirmedAhead[position.BatchSeq] = position;

            BatchPosition? advanced = null;
            var count = 0;
            while (_confirmedAhead.Remove(_nextExpectedSeq, out var next))
            {
                advanced = next;
                _nextExpectedSeq++;
                count++;
            }

            return new ConfirmResult(advanced, count);
        }
    }
}

/// <summary>Result of a <see cref="ConfirmedBatchTracker.Confirm"/>: how far the prefix advanced.</summary>
/// <param name="AdvancedTo">New watermark position, or null if the prefix did not advance.</param>
/// <param name="AdvancedCount">Number of batches that became contiguous (0 if held).</param>
public readonly record struct ConfirmResult(BatchPosition? AdvancedTo, int AdvancedCount);

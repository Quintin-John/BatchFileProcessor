using Common.FileIngestion.Batching;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Pipeline;

/// <summary>
/// Everything one file's ingestion accumulates while it runs: where it resumes from, the provenance every
/// message carries, the batcher filling the next batch, which batches have been confirmed, and the two
/// gates that keep concurrent publishers from corrupting the watermark.
/// <para>
/// Held apart from the pipeline because more than one collaborator acts on the same run — the stage that
/// turns records into batches and the stage that publishes and confirms them both need it — and passing it
/// between them is what lets each own its own dependencies instead of the pipeline owning all of them.
/// </para>
/// <para>
/// Threading: the reader is a single producer and mutates <see cref="Batcher"/>, <see cref="Accepted"/> and
/// <see cref="Rejected"/>; concurrent publishers mutate <see cref="Batches"/> through Interlocked and
/// advance the watermark through <see cref="Tracker"/> under <see cref="WatermarkGate"/>.
/// <see cref="Window"/> bounds how many batches can be in flight at once.
/// </para>
/// </summary>
internal sealed class FileRun : IDisposable
{
    /// <summary>Creates the state for one file's run.</summary>
    /// <param name="sourceKey">Resume key for the checkpoint store.</param>
    /// <param name="resumeOffset">Byte offset a prior run confirmed up to; 0 when starting fresh.</param>
    /// <param name="provenance">Provenance stamped on every message this run emits.</param>
    /// <param name="batcher">Accumulates records into batches.</param>
    /// <param name="tracker">Tracks which batches are confirmed, and how far the contiguous prefix reaches.</param>
    /// <param name="confirmWindow">Maximum batches in flight at once.</param>
    public FileRun(
        string sourceKey, long resumeOffset, MessageProvenance provenance, Batcher batcher,
        ConfirmedBatchTracker tracker, int confirmWindow)
    {
        SourceKey = sourceKey;
        ResumeOffset = resumeOffset;
        Provenance = provenance;
        Batcher = batcher;
        Tracker = tracker;
        Window = new SemaphoreSlim(confirmWindow, confirmWindow);
    }

    /// <summary>Resume key for the checkpoint store.</summary>
    public string SourceKey { get; }

    /// <summary>Byte offset a prior run confirmed up to; 0 when starting fresh.</summary>
    public long ResumeOffset { get; }

    /// <summary>Provenance stamped on every message this run emits.</summary>
    public MessageProvenance Provenance { get; }

    /// <summary>Accumulates records into batches.</summary>
    public Batcher Batcher { get; }

    /// <summary>Tracks which batches are confirmed, and how far the contiguous prefix reaches.</summary>
    public ConfirmedBatchTracker Tracker { get; }

    /// <summary>Serialises watermark writes across publishers and enforces monotonic advance.</summary>
    public SemaphoreSlim WatermarkGate { get; } = new(1, 1);

    /// <summary>Highest batch sequence written to the checkpoint store; -1 before the first save.</summary>
    public long LastSavedBatchSeq { get; set; } = -1;

    /// <summary>
    /// Outstanding-confirms window: a slot per created batch, released when it joins the contiguous
    /// confirmed prefix — bounding batches-in-flight (and the tracker's held set) to the window size.
    /// </summary>
    public SemaphoreSlim Window { get; }

    /// <summary>Records published so far.</summary>
    public long Accepted;

    /// <summary>Records quarantined so far.</summary>
    public long Rejected;

    /// <summary>Batches sealed so far.</summary>
    public long Batches;

    /// <inheritdoc />
    public void Dispose()
    {
        WatermarkGate.Dispose();
        Window.Dispose();
    }
}

using Common.FileIngestion.Pipeline;

namespace Common.FileIngestion.Tests.Pipeline;

public sealed class ConfirmedBatchTrackerTests
{
    // A position whose fields encode the seq, so assertions can identify which batch the watermark reached.
    private static BatchPosition Pos(long seq) => new(seq, (seq + 1) * 100, (seq + 1) * 10);

    [Fact]
    public void Confirm_InOrder_AdvancesEachTime_ByOne()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 0);

        var r0 = tracker.Confirm(Pos(0));
        Assert.Equal(0, r0.AdvancedTo!.BatchSeq);
        Assert.Equal(1, r0.AdvancedCount);
        Assert.Equal(1, tracker.Confirm(Pos(1)).AdvancedTo!.BatchSeq);
        Assert.Equal(2, tracker.Confirm(Pos(2)).AdvancedTo!.BatchSeq);
    }

    [Fact]
    public void Confirm_AheadOfGap_HoldsUntilGapFills_ThenAdvancesByAll()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 0);

        Assert.Equal(0, tracker.Confirm(Pos(0)).AdvancedTo!.BatchSeq); // prefix at 0

        var held2 = tracker.Confirm(Pos(2)); // gap at 1 -> held
        Assert.Null(held2.AdvancedTo);
        Assert.Equal(0, held2.AdvancedCount);
        Assert.Null(tracker.Confirm(Pos(3)).AdvancedTo); // still held

        // Filling the gap advances across the whole now-contiguous run (1,2,3) to the highest, 3.
        var filled = tracker.Confirm(Pos(1));
        Assert.Equal(3, filled.AdvancedTo!.BatchSeq);
        Assert.Equal(3, filled.AdvancedCount); // released 1, 2 and 3
    }

    [Fact]
    public void Confirm_OutOfOrderPair_AdvancesToHighestContiguous()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 0);

        Assert.Null(tracker.Confirm(Pos(1)).AdvancedTo);                // gap at 0 -> held
        var r = tracker.Confirm(Pos(0));
        Assert.Equal(1, r.AdvancedTo!.BatchSeq);                        // 0 then 1 both contiguous
        Assert.Equal(2, r.AdvancedCount);
    }

    [Fact]
    public void Confirm_ResumesFromFirstBatchSeq()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 5);

        Assert.Null(tracker.Confirm(Pos(6)).AdvancedTo);               // 5 not yet confirmed -> held
        Assert.Equal(6, tracker.Confirm(Pos(5)).AdvancedTo!.BatchSeq); // 5 then 6 -> advance to 6
    }

    [Fact]
    public void Confirm_AlreadyAdvancedSeq_ReturnsNoAdvance()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 0);
        tracker.Confirm(Pos(0));

        var r = tracker.Confirm(Pos(0)); // seq below the prefix
        Assert.Null(r.AdvancedTo);
        Assert.Equal(0, r.AdvancedCount);
    }

    [Fact]
    public void Confirm_ReturnsAdvancedWatermarkPosition()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 0);

        var advanced = tracker.Confirm(Pos(0)).AdvancedTo!;

        Assert.Equal(100, advanced.ByteOffset);
        Assert.Equal(10, advanced.LastRecordSeq);
    }

    [Fact]
    public void Constructor_NegativeFirstBatchSeq_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfirmedBatchTracker(-1));

    [Fact]
    public void Confirm_NullPosition_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ConfirmedBatchTracker(0).Confirm(null!));
}

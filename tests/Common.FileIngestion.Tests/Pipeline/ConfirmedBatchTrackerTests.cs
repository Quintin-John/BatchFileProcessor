using Common.FileIngestion.Pipeline;

namespace Common.FileIngestion.Tests.Pipeline;

public sealed class ConfirmedBatchTrackerTests
{
    // A position whose fields encode the seq, so assertions can identify which batch the watermark reached.
    private static BatchPosition Pos(long seq) => new(seq, (seq + 1) * 100, (seq + 1) * 10);

    [Fact]
    public void Confirm_InOrder_AdvancesEachTime()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 0);

        Assert.Equal(0, tracker.Confirm(Pos(0))!.BatchSeq);
        Assert.Equal(1, tracker.Confirm(Pos(1))!.BatchSeq);
        Assert.Equal(2, tracker.Confirm(Pos(2))!.BatchSeq);
    }

    [Fact]
    public void Confirm_AheadOfGap_HoldsUntilGapFills()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 0);

        Assert.Equal(0, tracker.Confirm(Pos(0))!.BatchSeq); // prefix at 0
        Assert.Null(tracker.Confirm(Pos(2)));               // gap at 1 -> held
        Assert.Null(tracker.Confirm(Pos(3)));               // still held

        // Filling the gap advances across the whole now-contiguous run (1,2,3) to the highest, 3.
        Assert.Equal(3, tracker.Confirm(Pos(1))!.BatchSeq);
    }

    [Fact]
    public void Confirm_OutOfOrderPair_AdvancesToHighestContiguous()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 0);

        Assert.Null(tracker.Confirm(Pos(1)));               // gap at 0 -> held
        Assert.Equal(1, tracker.Confirm(Pos(0))!.BatchSeq); // 0 then 1 both contiguous -> advance to 1
    }

    [Fact]
    public void Confirm_ResumesFromFirstBatchSeq()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 5);

        Assert.Null(tracker.Confirm(Pos(6)));               // 5 not yet confirmed -> held
        Assert.Equal(6, tracker.Confirm(Pos(5))!.BatchSeq); // 5 then 6 -> advance to 6
    }

    [Fact]
    public void Confirm_AlreadyAdvancedSeq_ReturnsNull()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 0);
        tracker.Confirm(Pos(0));

        Assert.Null(tracker.Confirm(Pos(0))); // seq below the prefix -> no advance
    }

    [Fact]
    public void Confirm_ReturnsAdvancedWatermarkPosition()
    {
        var tracker = new ConfirmedBatchTracker(firstBatchSeq: 0);

        var advanced = tracker.Confirm(Pos(0))!;

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

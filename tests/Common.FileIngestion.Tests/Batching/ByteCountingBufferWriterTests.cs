using System.Text;
using Common.FileIngestion.Batching;

namespace Common.FileIngestion.Tests.Batching;

public sealed class ByteCountingBufferWriterTests
{
    [Fact]
    public void Advance_AccumulatesTotalAcrossWrites()
    {
        var writer = new ByteCountingBufferWriter();

        Encoding.UTF8.GetBytes("hello", writer.GetSpan(5));
        writer.Advance(5);
        Encoding.UTF8.GetBytes("!!", writer.GetSpan(2));
        writer.Advance(2);

        Assert.Equal(7, writer.BytesWritten);
    }

    [Fact]
    public void Reset_ZeroesTheCount_ForReuse()
    {
        var writer = new ByteCountingBufferWriter();
        writer.Advance(10);

        writer.Reset();

        Assert.Equal(0, writer.BytesWritten);
        writer.Advance(3);
        Assert.Equal(3, writer.BytesWritten);
    }

    [Fact]
    public void GetSpan_HonoursSizeHint_GrowingBeyondInitialBuffer()
    {
        var writer = new ByteCountingBufferWriter();

        Assert.True(writer.GetSpan(10_000).Length >= 10_000);
    }

    [Fact]
    public void GetMemory_HonoursSizeHint()
    {
        var writer = new ByteCountingBufferWriter();

        Assert.True(writer.GetMemory(4_096).Length >= 4_096);
    }

    [Fact]
    public void GetSpan_ZeroHint_ReturnsNonEmptyBuffer()
    {
        Assert.False(new ByteCountingBufferWriter().GetSpan().IsEmpty);
    }

    [Fact]
    public void Advance_Negative_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteCountingBufferWriter().Advance(-1));

    [Fact]
    public void GetSpan_NegativeHint_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteCountingBufferWriter().GetSpan(-1));

    [Fact]
    public void GetMemory_NegativeHint_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteCountingBufferWriter().GetMemory(-1));
}

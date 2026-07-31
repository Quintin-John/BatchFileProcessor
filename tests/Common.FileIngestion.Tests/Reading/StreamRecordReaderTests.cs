using Common.FileIngestion.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Common.FileIngestion.Reading;

namespace Common.FileIngestion.Tests.Reading;

public sealed class StreamRecordReaderTests
{
    private static async Task<(List<FramedRecord> Records, string FileId)> ReadAsync(
        StreamRecordReader reader, byte[] data)
    {
        var records = new List<FramedRecord>();
        var fileId = await reader.ReadAsync(
            new MemoryStream(data),
            (record, _) =>
            {
                records.Add(record);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);
        return (records, fileId);
    }

    [Fact]
    public async Task Reads_TerminatedRecords_WithSeqAndOffset()
    {
        var data = Encoding.ASCII.GetBytes("AAAA\nBBBB\nCCCC\n");

        var (records, fileId) = await ReadAsync(new StreamRecordReader(4, 1, Encoding.ASCII), data);

        Assert.Equal(3, records.Count);
        Assert.Equal((1L, 0L, "AAAA"), (records[0].RecordSeq, records[0].ByteOffset, records[0].Content));
        Assert.Equal((2L, 5L, "BBBB"), (records[1].RecordSeq, records[1].ByteOffset, records[1].Content));
        Assert.Equal((3L, 10L, "CCCC"), (records[2].RecordSeq, records[2].ByteOffset, records[2].Content));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), fileId);
    }

    [Fact]
    public async Task Reads_FinalRecord_WithoutTerminator()
    {
        var data = Encoding.ASCII.GetBytes("AAAA\nBBBB");

        var (records, _) = await ReadAsync(new StreamRecordReader(4, 1, Encoding.ASCII), data);

        Assert.Equal(2, records.Count);
        Assert.Equal("BBBB", records[1].Content);
    }

    [Fact]
    public async Task Reads_Records_WithNoTerminatorConfigured()
    {
        var data = Encoding.ASCII.GetBytes("AAAABBBB");

        var (records, _) = await ReadAsync(new StreamRecordReader(4, 0, Encoding.ASCII), data);

        Assert.Equal(2, records.Count);
        Assert.Equal("AAAA", records[0].Content);
        Assert.Equal("BBBB", records[1].Content);
    }

    [Fact]
    public async Task IncompleteFinalRecord_Throws()
    {
        var data = Encoding.ASCII.GetBytes("AAAA\nBB"); // 2 trailing bytes, record length 4

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(new StreamRecordReader(4, 1, Encoding.ASCII), data));
    }

    [Fact]
    public async Task FramesCorrectly_WhenRecordsSpanReadSegments()
    {
        var data = Encoding.ASCII.GetBytes("AAAA\nBBBB\nCCCC\n");
        var records = new List<FramedRecord>();

        // Drip one byte per read so every record spans many pipe segments.
        var fileId = await new StreamRecordReader(4, 1, Encoding.ASCII).ReadAsync(
            new DripStream(data, 1),
            (record, _) =>
            {
                records.Add(record);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(3, records.Count);
        Assert.Equal("AAAA", records[0].Content);
        Assert.Equal("BBBB", records[1].Content);
        Assert.Equal("CCCC", records[2].Content);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), fileId);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, -1)]
    public void Constructor_InvalidLengths_Throw(int recordLength, int terminatorLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StreamRecordReader(recordLength, terminatorLength, Encoding.ASCII));
    }

    [Fact]
    public void Constructor_NullEncoding_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new StreamRecordReader(4, 1, null!));
    }

    [Fact]
    public void Constructor_SingleByteEncoding_Accepted()
    {
        var reader = new StreamRecordReader(4, 1, Encoding.Latin1); // single-byte: accepted

        Assert.Equal(5, reader.Stride);
    }

    [Fact]
    public void Constructor_MultiByteEncoding_Throws()
    {
        // UTF-8 would decode N bytes into a differently-sized string, misaligning fixed-width fields.
        Assert.Throws<ArgumentException>(() => new StreamRecordReader(4, 1, Encoding.UTF8));
    }

    [Fact]
    public async Task ReadAsync_NullArguments_Throw()
    {
        var reader = new StreamRecordReader(4, 1, Encoding.ASCII);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.ReadAsync(null!, (_, _) => ValueTask.CompletedTask, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.ReadAsync(new MemoryStream(), null!, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new StreamRecordReader(4, 1, Encoding.ASCII)
                .ReadAsync(new MemoryStream(Encoding.ASCII.GetBytes("AAAA\n")), (_, _) => ValueTask.CompletedTask, cts.Token));
    }

    private sealed class DripStream(byte[] data, int drip) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= data.Length)
            {
                return 0;
            }

            var n = Math.Min(Math.Min(count, drip), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

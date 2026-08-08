using System.Security.Cryptography;
using System.Text;
using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Reading;

namespace Common.FileIngestion.Tests.Reading;

public sealed class DelimitedLineReaderTests
{
    private const string Version = "1.0";
    private const string EncodingName = "ascii";
    private const char Delimiter = ',';

    private const string HeaderName = "head";
    private const string DataName = "body";
    private const string TrailerName = "foot";

    private static DelimitedFieldDefinition[] Fields(int count) =>
        Enumerable.Range(0, count).Select(i => new DelimitedFieldDefinition($"f{i}", i)).ToArray();

    private static DelimitedLayout Layout(int headerRows = 0, int trailerRows = 0)
    {
        var rows = new List<DelimitedRowDefinition>();
        if (headerRows > 0)
        {
            rows.Add(new DelimitedRowDefinition(HeaderName, RowRole.Header, headerRows, [], skip: true));
        }

        rows.Add(new DelimitedRowDefinition(DataName, RowRole.Data, 0, Fields(2)));

        if (trailerRows > 0)
        {
            rows.Add(new DelimitedRowDefinition(TrailerName, RowRole.Trailer, trailerRows, [], skip: true));
        }

        return new DelimitedLayout(Version, Delimiter, EncodingName, rows);
    }

    private static DelimitedLineReader Reader(int headerRows = 0, int trailerRows = 0) =>
        new(Layout(headerRows, trailerRows), Encoding.ASCII);

    private static async Task<(List<FramedRecord> Records, string FileId)> ReadAsync(
        DelimitedLineReader reader, byte[] data, Stream? stream = null)
    {
        var records = new List<FramedRecord>();
        var fileId = await reader.ReadAsync(
            stream ?? new MemoryStream(data),
            (record, _) =>
            {
                records.Add(record);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);
        return (records, fileId);
    }

    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

    // Asserts the property the resume point depends on: extents tile the file with no gap or overlap.
    private static void AssertExtentsTile(IReadOnlyList<FramedRecord> records, byte[] data)
    {
        long expected = 0;
        foreach (var record in records)
        {
            Assert.Equal(expected, record.ByteOffset);
            expected += record.ByteLength;
        }

        Assert.Equal(data.Length, expected);
    }

    // ---------- framing ----------

    [Fact]
    public async Task Reads_VariableLengthRows_WithSeqOffsetAndExtent()
    {
        var data = Bytes("a,1\nbb,22\nccc,333\n");

        var (records, fileId) = await ReadAsync(Reader(), data);

        Assert.Equal(["a,1", "bb,22", "ccc,333"], records.Select(r => r.Content));
        Assert.Equal([1L, 2L, 3L], records.Select(r => r.RecordSeq));

        // Rows differ in length, so no fixed stride exists — each extent is the row plus its terminator.
        Assert.Equal([4, 6, 8], records.Select(r => r.ByteLength));
        AssertExtentsTile(records, data);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), fileId);
    }

    [Fact]
    public async Task Reads_CrLfRows_ExcludingCrFromContentButCountingItInTheExtent()
    {
        var data = Bytes("a,1\r\nbb,22\r\n");

        var (records, _) = await ReadAsync(Reader(), data);

        Assert.Equal(["a,1", "bb,22"], records.Select(r => r.Content));
        Assert.Equal([5, 7], records.Select(r => r.ByteLength)); // content + CR + LF
        AssertExtentsTile(records, data);
    }

    [Fact]
    public async Task Reads_FinalRow_WithoutTerminator()
    {
        var data = Bytes("a,1\nbb,22");

        var (records, _) = await ReadAsync(Reader(), data);

        Assert.Equal(["a,1", "bb,22"], records.Select(r => r.Content));
        Assert.Equal([4, 5], records.Select(r => r.ByteLength)); // last row consumes no terminator
        AssertExtentsTile(records, data);
    }

    [Fact]
    public async Task Reads_EmptyRow_AsAnEmptyRowNotEndOfFile()
    {
        // A blank line is a row the parser must reject on field count, not something framing may swallow.
        var data = Bytes("a,1\n\nb,2\n");

        var (records, _) = await ReadAsync(Reader(), data);

        Assert.Equal(["a,1", string.Empty, "b,2"], records.Select(r => r.Content));
        Assert.Equal(1, records[1].ByteLength); // the terminator alone
        AssertExtentsTile(records, data);
    }

    [Fact]
    public async Task Frames_And_Hashes_Correctly_WhenRowsSpanReadSegments()
    {
        var data = Bytes("a,1\nbb,22\nccc,333\n");

        var (records, fileId) = await ReadAsync(Reader(), data, new DripStream(data, 1));

        Assert.Equal(["a,1", "bb,22", "ccc,333"], records.Select(r => r.Content));
        AssertExtentsTile(records, data);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), fileId);
    }

    [Fact]
    public async Task EmptyFile_WithNoHeaderOrTrailer_YieldsNoRows()
    {
        var (records, fileId) = await ReadAsync(Reader(), []);

        Assert.Empty(records);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([])), fileId);
    }

    // ---------- positional classification ----------

    [Fact]
    public async Task Classifies_HeaderRowsFromTheStart()
    {
        var data = Bytes("col1,col2\na,1\nb,2\n");

        var (records, _) = await ReadAsync(Reader(headerRows: 1), data);

        Assert.Equal([HeaderName, DataName, DataName], records.Select(r => r.RowType));
    }

    [Fact]
    public async Task Classifies_TrailerRowsFromTheEnd()
    {
        // The trailer is only identifiable at end of file, so this exercises the lookahead.
        var data = Bytes("a,1\nb,2\nCOUNT,2\n");

        var (records, _) = await ReadAsync(Reader(trailerRows: 1), data);

        Assert.Equal([DataName, DataName, TrailerName], records.Select(r => r.RowType));
        Assert.Equal("COUNT,2", records[^1].Content);
    }

    [Fact]
    public async Task Classifies_HeaderAndTrailerTogether()
    {
        var data = Bytes("col1,col2\na,1\nb,2\nc,3\nCOUNT,3\n");

        var (records, _) = await ReadAsync(Reader(headerRows: 1, trailerRows: 1), data);

        Assert.Equal(
            [HeaderName, DataName, DataName, DataName, TrailerName],
            records.Select(r => r.RowType));
        AssertExtentsTile(records, data);
    }

    [Fact]
    public async Task Classifies_MultiRowHeaderAndTrailer()
    {
        var data = Bytes("h1\nh2\na,1\nt1\nt2\n");

        var (records, _) = await ReadAsync(Reader(headerRows: 2, trailerRows: 2), data);

        Assert.Equal(
            [HeaderName, HeaderName, DataName, TrailerName, TrailerName],
            records.Select(r => r.RowType));
    }

    [Fact]
    public async Task FileOfExactlyHeaderPlusTrailer_HasNoDataRows()
    {
        // A legitimately empty batch: the control rows are present and nothing sits between them.
        var data = Bytes("col1,col2\nCOUNT,0\n");

        var (records, _) = await ReadAsync(Reader(headerRows: 1, trailerRows: 1), data);

        Assert.Equal([HeaderName, TrailerName], records.Select(r => r.RowType));
        Assert.DoesNotContain(DataName, records.Select(r => r.RowType));
    }

    [Fact]
    public async Task TrailerRowsAreHashedAndAdvanceOffsets_EvenThoughTheyAreControlRows()
    {
        var data = Bytes("a,1\nCOUNT,1\n");

        var (records, fileId) = await ReadAsync(Reader(trailerRows: 1), data);

        AssertExtentsTile(records, data);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), fileId);
    }

    [Fact]
    public async Task Emits_SkippedRowTypes_LeavingTheSkipDecisionToTheParser()
    {
        // Framing classifies; acting on skip is layout semantics and belongs to the parser, so a skipped
        // header still arrives here tagged with its type.
        var data = Bytes("col1,col2\na,1\n");

        var (records, _) = await ReadAsync(Reader(headerRows: 1), data);

        Assert.Equal(2, records.Count);
        Assert.Equal(HeaderName, records[0].RowType);
    }

    // ---------- fail-closed ----------

    [Fact]
    public async Task FileShorterThanItsDeclaredHeaderAndTrailer_Throws()
    {
        var data = Bytes("only-one-row\n");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => ReadAsync(Reader(headerRows: 1, trailerRows: 1), data));
        Assert.Contains("needing at least 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyFile_WithDeclaredHeader_Throws()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(Reader(headerRows: 1), []));
    }

    [Fact]
    public void Constructor_NullLayout_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DelimitedLineReader(null!, Encoding.ASCII));
    }

    [Fact]
    public void Constructor_NullEncoding_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DelimitedLineReader(Layout(), null!));
    }

    [Fact]
    public void Constructor_Utf8Encoding_IsAccepted()
    {
        // UTF-8 continuation bytes all have the high bit set, so the terminator byte cannot occur inside a
        // character — rows can be framed on bytes safely.
        Assert.NotNull(new DelimitedLineReader(Layout(), Encoding.UTF8));
    }

    [Fact]
    public void Constructor_WideEncoding_Throws()
    {
        // UTF-16 encodes 'a' as 0x61 0x00 and could place the terminator byte inside a character.
        var ex = Assert.Throws<ArgumentException>(() => new DelimitedLineReader(Layout(), Encoding.Unicode));
        Assert.Contains("mid-character", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_NullArguments_Throw()
    {
        var reader = Reader();

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
            () => Reader().ReadAsync(
                new MemoryStream(Bytes("a,1\n")), (_, _) => ValueTask.CompletedTask, cts.Token));
    }
}

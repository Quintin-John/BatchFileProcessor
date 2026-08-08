using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text;
using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Reading;

/// <summary>
/// Frames a stream into terminator-delimited rows and computes the file's SHA-256 in a single streaming
/// pass, classifying each row against the layout's row types. Rows vary in length, so every framed record
/// reports its own extent; consecutive extents tile the file exactly, which is what a resume point relies on.
/// <para>
/// Row classification is positional, so it belongs here rather than in the parser: only the reader knows
/// where a row sits. Header rows are identified from the start of the file and can be classified as they
/// arrive. Trailer rows can only be identified once the end is in sight, so rows are held in a queue of the
/// declared trailer length and released once they are known not to be trailer rows — memory stays O(trailer
/// rows), never O(file). Every row is emitted carrying the type it resolved to; acting on that type,
/// including honouring <c>skip</c>, is the parser's job, so this class only frames and classifies.
/// </para>
/// </summary>
public sealed class DelimitedLineReader : IRecordReader
{
    private const byte LineFeed = (byte)'\n';
    private const byte CarriageReturn = (byte)'\r';

    private readonly byte _rowTerminator;

    private readonly DelimitedLayout _layout;
    private readonly Encoding _encoding;

    /// <summary>Creates a reader for the given delimited layout.</summary>
    /// <param name="layout">The layout whose row types classify each row; required.</param>
    /// <param name="encoding">Encoding used to decode row content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> or <paramref name="encoding"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="encoding"/> is one whose code units can contain the terminator byte.</exception>
    public DelimitedLineReader(DelimitedLayout layout, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(encoding);

        // Rows are framed by scanning bytes for the terminator, so the encoding must never produce that byte
        // as part of another character. Single-byte encodings cannot; UTF-8 cannot either, because its
        // continuation bytes all have the high bit set. UTF-16/32 can, and would split rows mid-character.
        if (!encoding.IsSingleByte && encoding.CodePage != Encoding.UTF8.CodePage)
        {
            throw new ArgumentException(
                "A single-byte or UTF-8 encoding is required so the row terminator cannot occur inside a " +
                "character; a wide encoding would frame rows mid-character.",
                nameof(encoding));
        }

        _layout = layout;
        _encoding = encoding;
        _rowTerminator = (byte)layout.RowTerminator;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">The file holds fewer rows than its header and trailer require.</exception>
    public async Task<string> ReadAsync(
        Stream stream,
        Func<FramedRecord, CancellationToken, ValueTask> onRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(onRecord);

        using var hash = FileContentHash.CreateIncremental();
        var pipe = PipeReader.Create(stream);

        // Rows released only once they are known not to be trailer rows.
        var pending = new Queue<FramedRecord>(_layout.TrailerRows + 1);
        long recordSeq = 1;
        long byteOffset = 0;

        try
        {
            while (true)
            {
                var result = await pipe.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (TryReadRow(ref buffer, hash, out var content, out var byteLength))
                {
                    pending.Enqueue(new FramedRecord(recordSeq, byteOffset, byteLength, content));
                    recordSeq++;
                    byteOffset += byteLength;

                    await ReleaseSettledRowsAsync(pending, onRecord, cancellationToken).ConfigureAwait(false);
                }

                if (result.IsCompleted)
                {
                    // Read the tail before advancing: advancing releases the buffer this reads from. A final
                    // row with no trailing terminator still consumes its content bytes.
                    if (!buffer.IsEmpty)
                    {
                        // The tail is the last row in the file, so byteOffset is not advanced past it —
                        // there is no next row to position.
                        var tail = ReadTail(buffer, hash);
                        pending.Enqueue(new FramedRecord(recordSeq, byteOffset, tail.Length, tail.Content));
                        recordSeq++;
                    }

                    pipe.AdvanceTo(buffer.End);

                    // Drain again first: the tail row was queued after the last release, and it is only a
                    // trailer row if the layout declares one that reaches it.
                    await ReleaseSettledRowsAsync(pending, onRecord, cancellationToken).ConfigureAwait(false);
                    await FlushTrailerAsync(pending, recordSeq - 1, onRecord, cancellationToken).ConfigureAwait(false);
                    break;
                }

                // Consumed up to the last complete row; everything past it has been examined for a terminator
                // and did not contain one, so the pipe must wait for more data rather than replay it.
                pipe.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        finally
        {
            await pipe.CompleteAsync().ConfigureAwait(false);
        }

        return FileContentHash.Format(hash.GetHashAndReset());
    }

    // Releases every row that can no longer turn out to be a trailer row: once more than TrailerRows rows are
    // queued, the oldest is settled. Its position alone then decides header versus data.
    private async ValueTask ReleaseSettledRowsAsync(
        Queue<FramedRecord> pending,
        Func<FramedRecord, CancellationToken, ValueTask> onRecord,
        CancellationToken cancellationToken)
    {
        while (pending.Count > _layout.TrailerRows)
        {
            var row = pending.Dequeue();
            var rowIndex = row.RecordSeq - 1;
            var rowType = rowIndex < _layout.HeaderRows ? _layout.Header : _layout.Data;
            await EmitAsync(row, rowType, onRecord, cancellationToken).ConfigureAwait(false);
        }
    }

    // At end of stream whatever remains queued is, by construction, the last TrailerRows rows.
    private async ValueTask FlushTrailerAsync(
        Queue<FramedRecord> pending,
        long totalRows,
        Func<FramedRecord, CancellationToken, ValueTask> onRecord,
        CancellationToken cancellationToken)
    {
        var required = _layout.HeaderRows + (long)_layout.TrailerRows;
        if (totalRows < required)
        {
            throw new InvalidDataException(
                $"File holds {totalRows} row(s) but its layout declares {_layout.HeaderRows} header and " +
                $"{_layout.TrailerRows} trailer row(s), needing at least {required}.");
        }

        while (pending.Count > 0)
        {
            await EmitAsync(pending.Dequeue(), _layout.Trailer, onRecord, cancellationToken).ConfigureAwait(false);
        }
    }

    private ValueTask EmitAsync(
        FramedRecord row,
        DelimitedRowDefinition? rowType,
        Func<FramedRecord, CancellationToken, ValueTask> onRecord,
        CancellationToken cancellationToken)
    {
        // Every framed row is emitted, tagged with the type its position resolves to. Whether that type is
        // skipped is layout semantics, not framing, so the parser applies it — exactly as it does for a
        // skipped fixed-width record type. A null type means the layout declares no row type for this
        // position (a header or trailer type that is absent), which the row-count invariant already rules out.
        if (rowType is null)
        {
            return ValueTask.CompletedTask;
        }

        VerifyMatch(row, rowType);
        return onRecord(row with { RowType = rowType.Name }, cancellationToken);
    }

    // Positional classification is a claim; a declared marker is what makes it checkable. Without this, the
    // last row of a truncated file passes as the trailer and its data is silently discarded.
    private void VerifyMatch(FramedRecord row, DelimitedRowDefinition rowType)
    {
        if (rowType.Match is not { } expected)
        {
            return;
        }

        var present = DelimitedFields.TryReadAt(row.Content, expected.Index, _layout.Delimiter, out var actual);
        if (!present || !actual.SequenceEqual(expected.Value))
        {
            throw new InvalidDataException(
                $"Row {row.RecordSeq} is positioned as '{rowType.Name}' but field {expected.Index} does not " +
                $"carry '{expected.Value}'; the file does not match its layout.");
        }
    }

    // Frames one terminated row out of the buffer, hashing every byte it consumes including the terminator.
    private bool TryReadRow(ref ReadOnlySequence<byte> buffer, IncrementalHash hash, out string content, out int byteLength)
    {
        var reader = new SequenceReader<byte>(buffer);
        if (!reader.TryReadTo(out ReadOnlySequence<byte> line, _rowTerminator, advancePastDelimiter: true))
        {
            content = string.Empty;
            byteLength = 0;
            return false;
        }

        // Consumed = the line plus its terminator; content excludes the terminator, and the CR of a CRLF pair.
        byteLength = (int)(line.Length + 1);
        Hash(hash, line);
        hash.AppendData([_rowTerminator]);
        content = Decode(TrimPairedCarriageReturn(line));

        buffer = buffer.Slice(reader.Position);
        return true;
    }

    private (string Content, int Length) ReadTail(ReadOnlySequence<byte> buffer, IncrementalHash hash)
    {
        Hash(hash, buffer);
        return (Decode(TrimPairedCarriageReturn(buffer)), (int)buffer.Length);
    }

    // CRLF is a two-byte line ending whose second byte is the terminator, so when the declared terminator is
    // LF a trailing CR is part of the ending rather than data. A layout framing on anything else says so, and
    // nothing is stripped.
    private ReadOnlySequence<byte> TrimPairedCarriageReturn(ReadOnlySequence<byte> line)
    {
        if (_rowTerminator != LineFeed || line.IsEmpty)
        {
            return line;
        }

        var last = line.Slice(line.Length - 1);
        Span<byte> one = stackalloc byte[1];
        last.CopyTo(one);
        return one[0] == CarriageReturn ? line.Slice(0, line.Length - 1) : line;
    }

    private static void Hash(IncrementalHash hash, ReadOnlySequence<byte> data)
    {
        foreach (var segment in data)
        {
            hash.AppendData(segment.Span);
        }
    }

    private string Decode(ReadOnlySequence<byte> data)
    {
        if (data.IsSingleSegment)
        {
            return _encoding.GetString(data.FirstSpan);
        }

        var length = (int)data.Length;
        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            data.CopyTo(rented);
            return _encoding.GetString(rented.AsSpan(0, length));
        }
        finally
        {
            // Zero on return: the buffer held cleartext row bytes (PAN/PII) and goes back to a shared pool.
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }
}

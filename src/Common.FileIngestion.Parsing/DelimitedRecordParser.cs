using System.Globalization;
using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Layouts;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Parsing;

/// <summary>
/// Splits a delimited row into named raw fields against a layout: it resolves the row type the reader
/// assigned, checks the row splits into exactly the declared number of fields, and emits each field's raw
/// text verbatim (spaces preserved), except fields the layout marks <c>skip</c> — counted for coverage but
/// omitted from output. A row type the layout marks <c>skip</c> (a header or trailer control row) is
/// consumed for framing but produces no output — neither emitted nor rejected.
/// It interprets no value — types and meaning are downstream's concern. A row is rejected only structurally
/// (unknown row type, wrong field count) or when a field the layout marks <c>required</c> is blank.
/// Everything it knows about the format comes from the layout.
/// <para>
/// The field-count check is what makes this fail closed against quoted CSV. A quoted field containing the
/// delimiter splits into too many values and a quoted field containing a newline leaves too few, so either
/// rejects the row rather than silently mis-mapping every field after it.
/// </para>
/// </summary>
public sealed class DelimitedRecordParser : IRecordParser
{
    private const string RecordField = "record";
    private const string FieldCountRule = "field-count";
    private const string WrongFieldCountCode = "WRONG_FIELD_COUNT";
    private const string RowTypeRule = "row-type";
    private const string UnknownRowTypeCode = "UNKNOWN_ROW_TYPE";
    private const string RequiredRule = "required";
    private const string RequiredMissingCode = "REQUIRED_MISSING";

    private readonly DelimitedLayout _layout;

    /// <summary>Creates a parser for the given layout.</summary>
    /// <param name="layout">The layout to split against; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
    public DelimitedRecordParser(DelimitedLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// The framed record carries no row type. Delimited rows are classified by position, which only the
    /// reader knows, so an untagged record means this parser was paired with a reader that does not classify
    /// — a wiring fault, not bad data, and it must not be quarantined as if one row were at fault.
    /// </exception>
    public RecordParseResult Parse(FramedRecord framed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(framed.RecordSeq, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(framed.ByteOffset);
        ArgumentOutOfRangeException.ThrowIfLessThan(framed.ByteLength, 1);
        ArgumentNullException.ThrowIfNull(framed.Content);

        if (framed.RowType is null)
        {
            throw new ArgumentException(
                "A delimited record must carry the row type its position resolved to; the reader assigns it.",
                nameof(framed));
        }

        var rowDefinition = _layout.ResolveByName(framed.RowType);
        if (rowDefinition is null)
        {
            var reason = new RejectReason(RecordField, RowTypeRule, UnknownRowTypeCode, actual: framed.RowType);
            return RecordParseResult.Rejected(framed.RowType, framed.Content, [reason]);
        }

        // A skipped row type (header/trailer) is consumed for framing but never split or emitted.
        if (rowDefinition.Skip)
        {
            return RecordParseResult.Skipped(rowDefinition.Name);
        }

        ReadOnlySpan<char> row = framed.Content;
        var expected = rowDefinition.Fields.Count;
        var actual = CountFields(row, _layout.Delimiter);
        if (actual != expected)
        {
            var reason = new RejectReason(
                RecordField, FieldCountRule, WrongFieldCountCode,
                expected: expected.ToString(CultureInfo.InvariantCulture),
                actual: actual.ToString(CultureInfo.InvariantCulture));
            return RecordParseResult.Rejected(rowDefinition.Name, framed.Content, [reason]);
        }

        var fields = new Dictionary<string, FieldValue>(expected, StringComparer.Ordinal);
        List<RejectReason>? reasons = null;
        var remaining = row;

        foreach (var field in rowDefinition.Fields)
        {
            var value = TakeNext(ref remaining, _layout.Delimiter);

            // A skipped field is counted by the layout for row coverage but never emitted upstream. It
            // cannot be required (enforced by the layout), so there is nothing to validate here.
            if (field.Skip)
            {
                continue;
            }

            if (field.Required && value.IsWhiteSpace())
            {
                (reasons ??= []).Add(new RejectReason(
                    field.Name, RequiredRule, RequiredMissingCode, actual: value.ToString()));
                continue;
            }

            // Emit the field's raw text verbatim — spaces included. The pump does not interpret the value.
            fields[field.Name] = new ClearFieldValue(value.ToString());
        }

        if (reasons is not null)
        {
            return RecordParseResult.Rejected(rowDefinition.Name, framed.Content, reasons);
        }

        var locator = new RecordLocator(framed.RecordSeq, framed.ByteOffset, framed.ByteLength, rowDefinition.Name);
        return RecordParseResult.Success(new IngestRecord(locator, fields));
    }

    // Counted before any substring is materialised, so a malformed row costs nothing to reject.
    private static int CountFields(ReadOnlySpan<char> row, char delimiter)
    {
        var count = 1;
        foreach (var character in row)
        {
            if (character == delimiter)
            {
                count++;
            }
        }

        return count;
    }

    // Slices the next value off the row without allocating; only emitted fields become strings.
    private static ReadOnlySpan<char> TakeNext(ref ReadOnlySpan<char> remaining, char delimiter)
    {
        var separator = remaining.IndexOf(delimiter);
        if (separator < 0)
        {
            var last = remaining;
            remaining = default;
            return last;
        }

        var value = remaining[..separator];
        remaining = remaining[(separator + 1)..];
        return value;
    }
}

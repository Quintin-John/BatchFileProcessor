using System.Globalization;
using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Layouts;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Parsing;

/// <summary>
/// Slices a fixed-width record into named raw fields against a layout: it checks the record length,
/// resolves the record type from the discriminator, and emits each field's raw text verbatim (spaces
/// preserved), except fields the layout marks <c>skip</c> — tiled for coverage but omitted from output. A
/// record type the layout marks <c>skip</c> (a header/trailer control record) is recognised and consumed for
/// framing but produces no output — neither emitted nor rejected.
/// It interprets no value — types and meaning are downstream's concern. A record is rejected
/// only structurally (wrong length, unknown record type) or when a field the layout marks
/// <c>required</c> is blank. Everything it knows about the format comes from the layout.
/// </summary>
public sealed class FixedLengthRecordParser : IRecordParser
{
    private const string RecordField = "record";
    private const string RecordLengthRule = "record-length";
    private const string WrongLengthCode = "WRONG_LENGTH";
    private const string RecordTypeRule = "record-type";
    private const string UnknownRecordTypeCode = "UNKNOWN_RECORD_TYPE";
    private const string RequiredRule = "required";
    private const string RequiredMissingCode = "REQUIRED_MISSING";
    private const string UnknownRecordType = "?";

    private readonly Layout _layout;

    /// <summary>Creates a parser for the given layout.</summary>
    /// <param name="layout">The layout to slice against; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
    public FixedLengthRecordParser(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    /// <inheritdoc />
    public RecordParseResult Parse(long recordSeq, long byteOffset, ReadOnlySpan<char> record)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordSeq, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);

        if (record.Length != _layout.RecordLength)
        {
            var reason = new RejectReason(
                RecordField, RecordLengthRule, WrongLengthCode,
                expected: _layout.RecordLength.ToString(CultureInfo.InvariantCulture),
                actual: record.Length.ToString(CultureInfo.InvariantCulture));
            return RecordParseResult.Rejected(UnknownRecordType, record.ToString(), [reason]);
        }

        var discriminator = record.Slice(_layout.DiscriminatorOffset, _layout.DiscriminatorLength).ToString();
        var recordDefinition = _layout.ResolveByDiscriminator(discriminator);
        if (recordDefinition is null)
        {
            // A blank discriminator has no usable type label; fall back to a non-blank placeholder so the
            // reject still carries a record type (RecordLocator requires one).
            var recordType = string.IsNullOrWhiteSpace(discriminator) ? UnknownRecordType : discriminator;
            var reason = new RejectReason(RecordField, RecordTypeRule, UnknownRecordTypeCode, actual: discriminator);
            return RecordParseResult.Rejected(recordType, record.ToString(), [reason]);
        }

        // A skipped record (header/trailer) is consumed for framing but never sliced or emitted.
        if (recordDefinition.Skip)
        {
            return RecordParseResult.Skipped(recordDefinition.Match);
        }

        var fields = new Dictionary<string, FieldValue>(recordDefinition.Fields.Count, StringComparer.Ordinal);
        List<RejectReason>? reasons = null;
        foreach (var field in recordDefinition.Fields)
        {
            // A skipped field is tiled by the layout for record coverage (FILLER/padding) but never emitted
            // upstream. It cannot be required (enforced by the layout), so there is nothing to validate here.
            if (field.Skip)
            {
                continue;
            }

            // Slice the field's raw text verbatim — spaces included. The pump does not interpret the value.
            var raw = record.Slice(field.Offset, field.Length).ToString();

            if (field.Required && string.IsNullOrWhiteSpace(raw))
            {
                (reasons ??= []).Add(new RejectReason(
                    field.Name, RequiredRule, RequiredMissingCode, actual: raw, offset: field.Offset, length: field.Length));
                continue;
            }

            fields[field.Name] = new ClearFieldValue(raw);
        }

        if (reasons is not null)
        {
            return RecordParseResult.Rejected(recordDefinition.Match, record.ToString(), reasons);
        }

        var locator = new RecordLocator(recordSeq, byteOffset, recordDefinition.Match);
        return RecordParseResult.Success(new IngestRecord(locator, fields));
    }
}

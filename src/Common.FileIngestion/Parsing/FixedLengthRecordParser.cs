using System.Globalization;
using Common.FileIngestion.Layouts;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Parsing;

/// <summary>
/// Parses fixed-width records against a layout: reads the discriminator, resolves the record type,
/// and extracts each field (skipping fillers) via <see cref="FieldValueConverter"/>. Any field
/// failure quarantines the whole record with its reasons.
/// </summary>
public sealed class FixedLengthRecordParser : IRecordParser
{
    private const string RecordField = "record";
    private const string RecordLengthRule = "record-length";
    private const string WrongLengthCode = "WRONG_LENGTH";
    private const string RecordTypeRule = "record-type";
    private const string UnknownRecordTypeCode = "UNKNOWN_RECORD_TYPE";
    private const string UnknownRecordType = "?";

    private readonly Layout _layout;

    /// <summary>Creates a parser for the given layout.</summary>
    /// <param name="layout">The layout to parse against; required.</param>
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
            return RecordParseResult.Rejected(UnknownRecordType, record.ToString(), new[] { reason });
        }

        var discriminator = record.Slice(_layout.DiscriminatorStart - 1, _layout.DiscriminatorLength).ToString();
        var recordDefinition = _layout.ResolveByDiscriminator(discriminator);
        if (recordDefinition is null)
        {
            var reason = new RejectReason(RecordField, RecordTypeRule, UnknownRecordTypeCode, actual: discriminator);
            return RecordParseResult.Rejected(discriminator, record.ToString(), new[] { reason });
        }

        var fields = new Dictionary<string, FieldValue>(recordDefinition.Fields.Count, StringComparer.Ordinal);
        List<RejectReason>? reasons = null;

        foreach (var field in recordDefinition.Fields)
        {
            if (field.Type == FieldType.Filler)
            {
                continue;
            }

            var conversion = FieldValueConverter.Convert(field, record.Slice(field.Offset, field.Length).ToString());
            if (conversion.IsSuccess)
            {
                fields[field.Name] = conversion.Value!;
            }
            else
            {
                (reasons ??= []).Add(conversion.Reason!);
            }
        }

        if (reasons is not null)
        {
            return RecordParseResult.Rejected(recordDefinition.Match, record.ToString(), reasons);
        }

        var locator = new RecordLocator(recordSeq, byteOffset, recordDefinition.Match);
        return RecordParseResult.Success(new IngestRecord(locator, fields));
    }
}

using System.Globalization;
using Common.FileIngestion.Layouts;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Parsing;

/// <summary>
/// Converts a field's raw text to a typed <see cref="FieldValue"/> per its definition, or produces
/// a <see cref="RejectReason"/> on invalid data. The single coercion authority — used by every
/// record parser regardless of framing (DRY). Date/time values are validated against the format
/// and emitted as their canonical string; the consumer re-types from the layout.
/// </summary>
public static class FieldValueConverter
{
    private const string DefaultDateFormat = "yyyy-MM-dd";
    private const string DefaultTimeFormat = "HH:mm:ss";
    private const string DecimalRule = "decimal";
    private const string DateRule = "date";
    private const string TimeRule = "time";
    private const string NonNumericCode = "NON_NUMERIC";
    private const string BadDateCode = "BAD_DATE";
    private const string BadTimeCode = "BAD_TIME";

    /// <summary>Converts a field's raw text to a value or a rejection.</summary>
    /// <param name="field">The field definition; must not be <see cref="FieldType.Filler"/>.</param>
    /// <param name="raw">The raw field text extracted from the record.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="field"/> is a filler (fillers are skipped, not converted).</exception>
    public static FieldConversion Convert(FieldDefinition field, string raw)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(raw);

        return field.Type switch
        {
            FieldType.Text => FieldConversion.Success(new ClearFieldValue(raw.TrimEnd())),
            FieldType.Number => ConvertNumber(field, raw),
            FieldType.Date => ConvertDate(field, raw),
            FieldType.Time => ConvertTime(field, raw),
            _ => throw new InvalidOperationException($"Field '{field.Name}' has non-convertible type '{field.Type}'."),
        };
    }

    private static FieldConversion ConvertNumber(FieldDefinition field, string raw)
    {
        var text = raw.Trim();
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return Reject(field, DecimalRule, NonNumericCode, raw);
        }

        if (field.Scale > 0)
        {
            value /= Pow10(field.Scale);
        }

        return FieldConversion.Success(new ClearFieldValue(value));
    }

    private static FieldConversion ConvertDate(FieldDefinition field, string raw)
    {
        var text = raw.Trim();
        var format = field.Format ?? DefaultDateFormat;

        return DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? FieldConversion.Success(new ClearFieldValue(text))
            : Reject(field, DateRule, BadDateCode, raw, format);
    }

    private static FieldConversion ConvertTime(FieldDefinition field, string raw)
    {
        var text = raw.Trim();
        var format = field.Format ?? DefaultTimeFormat;

        return TimeOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? FieldConversion.Success(new ClearFieldValue(text))
            : Reject(field, TimeRule, BadTimeCode, raw, format);
    }

    private static FieldConversion Reject(FieldDefinition field, string rule, string code, string actual, string? expected = null) =>
        FieldConversion.Rejected(new RejectReason(field.Name, rule, code, expected, actual, field.Offset, field.Length));

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}

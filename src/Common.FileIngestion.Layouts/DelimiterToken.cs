using System.Globalization;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// Resolves a layout's declared delimiter token to the single character that separates fields.
/// <para>
/// Deliberately open-ended: any character is expressible without a code change. A printable separator is
/// written literally (<c>,</c>, <c>|</c>, <c>;</c>, <c>~</c>, …); anything else is written as a hex escape
/// (<c>\t</c>, or <c>\x</c> / <c>\u</c> followed by hex digits, e.g. <c>\x1F</c> for the ASCII unit
/// separator). Two aliases exist only for the separators that are invisible in a text editor and so cannot
/// be reviewed literally — <c>tab</c> and <c>space</c>. Adding a delimiter is therefore a layout edit,
/// never a change here.
/// </para>
/// Fail-closed: an unresolvable token, or one resolving to a line terminator (which would collide with row
/// framing), is rejected rather than guessed at.
/// </summary>
public static class DelimiterToken
{
    private const string UnicodeEscapePrefix = @"\u";
    private const string HexEscapePrefix = @"\x";
    private const int MaxHexDigits = 4;

    // Interchangeable spellings of the same hex escape; a new spelling is an entry here, not new logic.
    private static readonly string[] HexPrefixes = [UnicodeEscapePrefix, HexEscapePrefix];

    // Only the separators that cannot be seen when reviewing a layout by eye. Every other character is
    // written literally or as a hex escape, so this table never needs to grow to support a new delimiter.
    private static readonly Dictionary<string, char> InvisibleAliases =
        new(StringComparer.OrdinalIgnoreCase) { ["tab"] = '\t', ["space"] = ' ' };

    private static readonly Dictionary<string, char> SimpleEscapes =
        new(StringComparer.Ordinal)
        {
            [@"\t"] = '\t',
            [@"\\"] = '\\',
            [@"\0"] = '\0',
        };

    /// <summary>Resolves a declared delimiter token to its character.</summary>
    /// <param name="token">The token as written in the layout; required, non-empty.</param>
    /// <exception cref="ArgumentException"><paramref name="token"/> is null or empty, does not resolve to exactly one character, or resolves to CR or LF.</exception>
    public static char Resolve(string token)
    {
        // Not ThrowIfNullOrWhiteSpace: a literal space is a legitimate delimiter.
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("A delimiter must be declared.", nameof(token));
        }

        var resolved = ResolveCore(token)
            ?? throw new ArgumentException(
                $"Unrecognised delimiter '{token}'. Write the character literally, as a hex escape " +
                $"({HexEscapePrefix}1F), or as one of: {string.Join(", ", InvisibleAliases.Keys)}.",
                nameof(token));

        // A delimiter that is also a row terminator would make field splitting and row framing disagree.
        if (resolved is '\r' or '\n')
        {
            throw new ArgumentException(
                "A line terminator cannot be a field delimiter; it would collide with row framing.", nameof(token));
        }

        return resolved;
    }

    private static char? ResolveCore(string token)
    {
        if (token.Length == 1)
        {
            return token[0];
        }

        if (SimpleEscapes.TryGetValue(token, out var simple))
        {
            return simple;
        }

        return InvisibleAliases.TryGetValue(token, out var alias) ? alias : ParseHexEscape(token);
    }

    // The general escape hatch that keeps any character expressible from the layout alone.
    private static char? ParseHexEscape(string token)
    {
        foreach (var prefix in HexPrefixes)
        {
            if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var digits = token[prefix.Length..];
            return digits.Length is > 0 and <= MaxHexDigits
                   && ushort.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)
                ? (char)code
                : null;
        }

        return null;
    }
}

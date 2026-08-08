using System.Globalization;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// Resolves a layout's declared separator tokens to the characters they stand for.
/// <para>
/// Deliberately open-ended: any separator is expressible without a code change. A printable one is written
/// literally (<c>,</c>, <c>|</c>, <c>~|~</c>, …) — including sequences of more than one character; anything
/// unprintable is written as a hex escape (<c>\t</c>, or <c>\x</c> / <c>\u</c> followed by hex digits, e.g.
/// <c>\x1F</c> for the ASCII unit separator). A few aliases exist only for separators that are invisible in
/// a text editor and so cannot be reviewed literally — <c>tab</c>, <c>space</c>, <c>lf</c>, <c>cr</c>. An
/// alias is matched before the literal reading, so <c>tab</c> means the TAB character rather than those
/// three letters.
/// </para>
/// Fail-closed where a mistake is still detectable: a malformed escape is rejected rather than silently
/// read as literal text, and a separator that would collide with row framing is rejected outright.
/// </summary>
public static class DelimiterToken
{
    private const string UnicodeEscapePrefix = @"\u";
    private const string HexEscapePrefix = @"\x";
    private const int MaxHexDigits = 4;
    private const char MaxSingleByteCharacter = (char)0xFF;

    // Interchangeable spellings of the same hex escape; a new spelling is an entry here, not new logic.
    private static readonly string[] HexPrefixes = [UnicodeEscapePrefix, HexEscapePrefix];

    // Only the separators that cannot be seen when reviewing a layout by eye. Every other character is
    // written literally or as a hex escape, so this table never needs to grow to support a new delimiter.
    private static readonly Dictionary<string, char> InvisibleAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["tab"] = '\t',
            ["space"] = ' ',
            ["lf"] = '\n',
            ["cr"] = '\r',
        };

    private static readonly Dictionary<string, char> SimpleEscapes =
        new(StringComparer.Ordinal)
        {
            [@"\t"] = '\t',
            [@"\\"] = '\\',
            [@"\0"] = '\0',
        };

    /// <summary>
    /// Resolves a declared field-delimiter token. The result may be more than one character: a feed
    /// separated by <c>~|~</c> is as valid as one separated by a comma.
    /// </summary>
    /// <param name="token">The token as written in the layout; required, non-empty.</param>
    /// <exception cref="ArgumentException"><paramref name="token"/> is null or empty, carries a malformed escape, or contains a line terminator.</exception>
    public static string Resolve(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("A delimiter must be declared.", nameof(token));
        }

        // An alias or escape names one character; anything else is the separator's own text, which is what
        // makes a multi-character delimiter expressible at all.
        var resolved = ResolveCore(token) is { } single ? single.ToString() : LiteralOrThrow(token, "delimiter");

        // A delimiter containing a line terminator would make field splitting and row framing disagree.
        if (resolved.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException(
                "A line terminator cannot appear in a field delimiter; it would collide with row framing.",
                nameof(token));
        }

        return resolved;
    }

    // A token that begins like an escape but does not parse is a mistake, not a separator spelled oddly.
    // Reading it literally would turn a typo into a four-character delimiter and every row would then fail
    // its field count with nothing pointing at the cause.
    private static string LiteralOrThrow(string token, string role)
    {
        if (token.StartsWith('\\'))
        {
            throw new ArgumentException(
                $"Unrecognised {role} escape '{token}'. Use {HexEscapePrefix} or {UnicodeEscapePrefix} " +
                "followed by up to four hex digits.",
                nameof(token));
        }

        return token;
    }

    /// <summary>
    /// Resolves a declared row-terminator token to its character. Unlike a delimiter this may be a line
    /// terminator — that is its usual value — but it must be a single byte in the layout's encoding, because
    /// rows are framed by scanning bytes before anything is decoded.
    /// </summary>
    /// <param name="token">The token as written in the layout; required, non-empty.</param>
    /// <exception cref="ArgumentException"><paramref name="token"/> is null or empty, does not resolve to exactly one character, or resolves outside the single-byte range.</exception>
    public static char ResolveRowTerminator(string token)
    {
        // Not ThrowIfNullOrWhiteSpace: a literal space is a legitimate separator.
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("A row terminator must be declared.", nameof(token));
        }

        var resolved = ResolveCore(token)
            ?? throw new ArgumentException(
                $"Unrecognised row terminator '{token}'. Write the character literally, as a hex escape " +
                $"({HexEscapePrefix}1F), or as one of: {string.Join(", ", InvisibleAliases.Keys)}.",
                nameof(token));

        if (resolved > MaxSingleByteCharacter)
        {
            throw new ArgumentException(
                $"Row terminator '{token}' is not a single byte; rows are framed on bytes before decoding.",
                nameof(token));
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

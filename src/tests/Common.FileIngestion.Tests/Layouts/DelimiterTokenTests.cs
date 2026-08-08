using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class DelimiterTokenTests
{
    [Theory]
    [InlineData(",")]
    [InlineData("|")]
    [InlineData(";")]
    [InlineData("~")]
    [InlineData("^")]
    [InlineData("\t")]
    public void Resolve_SingleLiteralCharacter_IsThatCharacter(string token)
    {
        // Any printable separator is written literally, so a new one needs no code change.
        Assert.Equal(token, DelimiterToken.Resolve(token));
    }

    [Theory]
    [InlineData("~|~")]
    [InlineData("||")]
    [InlineData("::")]
    [InlineData("<SEP>")]
    public void Resolve_SeveralLiteralCharacters_IsThatText(string token)
    {
        // A delimiter is not restricted to one character. Anything the layout writes literally is the
        // separator, however long, so a feed that separates on '~|~' needs no code change either.
        Assert.Equal(token, DelimiterToken.Resolve(token));
    }

    [Theory]
    [InlineData("tab")]
    [InlineData("space")]
    public void Resolve_AliasIsMatchedBeforeTheLiteralReading(string token)
    {
        // The one ambiguity multi-character delimiters introduce: a token that is also an alias resolves to
        // the alias, never to its own letters. Pinned so the precedence cannot be reversed silently.
        Assert.NotEqual(token, DelimiterToken.Resolve(token));
        Assert.Single(DelimiterToken.Resolve(token));
    }

    [Theory]
    [InlineData("tab", "\t")]
    [InlineData("TAB", "\t")]
    [InlineData("space", " ")]
    public void Resolve_InvisibleAlias_IsResolved(string token, string expected)
    {
        // Aliases exist only for separators that cannot be reviewed by eye in a layout file.
        Assert.Equal(expected, DelimiterToken.Resolve(token));
    }

    [Theory]
    [InlineData(@"\t", "\t")]
    [InlineData(@"\\", "\\")]
    [InlineData(@"\0", "\0")]
    public void Resolve_SimpleEscape_IsResolved(string token, string expected)
    {
        Assert.Equal(expected, DelimiterToken.Resolve(token));
    }

    [Theory]
    [InlineData(@"\x1F", "\u001F")]   // ASCII unit separator
    [InlineData(@"\x1f", "\u001F")]   // case-insensitive digits
    [InlineData(@"\X1F", "\u001F")]   // case-insensitive prefix
    [InlineData(@"\u" + "001F", "\u001F")]   // the \u form resolves identically
    [InlineData(@"\x7C", "|")]
    [InlineData(@"\x01", "\u0001")]
    public void Resolve_HexEscape_IsResolved(string token, string expected)
    {
        // The general escape hatch: every character is expressible from the layout alone, so no delimiter
        // ever requires editing the resolver.
        Assert.Equal(expected, DelimiterToken.Resolve(token));
    }

    [Fact]
    public void Resolve_HexEscape_CoversEveryByteValue()
    {
        // Genericity stated as a property rather than a sample: nothing in the 8-bit range is unreachable,
        // except the two line terminators, which are rejected deliberately.
        for (var code = 0; code <= byte.MaxValue; code++)
        {
            var token = $@"\x{code:X2}";
            if (code is '\r' or '\n')
            {
                Assert.ThrowsAny<ArgumentException>(() => DelimiterToken.Resolve(token));
                continue;
            }

            Assert.Equal(((char)code).ToString(), DelimiterToken.Resolve(token));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_NullOrEmpty_Throws(string? token)
    {
        Assert.Throws<ArgumentException>(() => DelimiterToken.Resolve(token!));
    }

    [Theory]
    [InlineData(@"\q")]          // unknown escape
    [InlineData(@"\x")]          // hex prefix with no digits
    [InlineData(@"\xZZ")]        // non-hex digits
    [InlineData(@"\x12345")]     // too many digits to be one character
    public void Resolve_MalformedEscape_Throws(string token)
    {
        // Once any text is a valid delimiter, a botched escape is the only delimiter mistake still
        // detectable — and it must not be read as literal backslash-and-letters, which would turn a typo
        // into a working layout that rejects every row for the wrong reason.
        var ex = Assert.Throws<ArgumentException>(() => DelimiterToken.Resolve(token));
        Assert.Contains("escape", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData(@"\x0D")]
    [InlineData(@"\x0A")]
    [InlineData("~\n~")]     // buried inside a multi-character delimiter, not just standing alone
    [InlineData("|\r")]
    public void Resolve_LineTerminator_Throws(string token)
    {
        // A delimiter carrying a row terminator would make field splitting and row framing disagree.
        var ex = Assert.Throws<ArgumentException>(() => DelimiterToken.Resolve(token));
        Assert.Contains("row framing", ex.Message, StringComparison.Ordinal);
    }
}

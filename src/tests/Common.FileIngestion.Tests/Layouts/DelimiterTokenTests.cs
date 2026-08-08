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
        Assert.Equal(token[0], DelimiterToken.Resolve(token));
    }

    [Theory]
    [InlineData("tab", '\t')]
    [InlineData("TAB", '\t')]
    [InlineData("space", ' ')]
    public void Resolve_InvisibleAlias_IsResolved(string token, char expected)
    {
        // Aliases exist only for separators that cannot be reviewed by eye in a layout file.
        Assert.Equal(expected, DelimiterToken.Resolve(token));
    }

    [Theory]
    [InlineData(@"\t", '\t')]
    [InlineData(@"\\", '\\')]
    [InlineData(@"\0", '\0')]
    public void Resolve_SimpleEscape_IsResolved(string token, char expected)
    {
        Assert.Equal(expected, DelimiterToken.Resolve(token));
    }

    [Theory]
    [InlineData(@"\x1F", (char)0x1F)]   // ASCII unit separator
    [InlineData(@"\x1f", (char)0x1F)]   // case-insensitive digits
    [InlineData(@"\X1F", (char)0x1F)]   // case-insensitive prefix
    [InlineData(@"\u" + "001F", (char)0x1F)]   // the \u form resolves identically
    [InlineData(@"\x7C", '|')]
    [InlineData(@"\x01", (char)0x01)]
    public void Resolve_HexEscape_IsResolved(string token, char expected)
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

            Assert.Equal((char)code, DelimiterToken.Resolve(token));
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
    [InlineData("comma")]        // not an alias: ',' is writable literally
    [InlineData("||")]           // more than one character
    [InlineData(@"\q")]          // unknown escape
    [InlineData(@"\x")]          // hex prefix with no digits
    [InlineData(@"\xZZ")]        // non-hex digits
    [InlineData(@"\x12345")]     // too many digits to be one character
    public void Resolve_Unrecognised_Throws(string token)
    {
        Assert.Throws<ArgumentException>(() => DelimiterToken.Resolve(token));
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData(@"\x0D")]
    [InlineData(@"\x0A")]
    public void Resolve_LineTerminator_Throws(string token)
    {
        // A delimiter that is also a row terminator would make field splitting and row framing disagree.
        var ex = Assert.Throws<ArgumentException>(() => DelimiterToken.Resolve(token));
        Assert.Contains("row framing", ex.Message, StringComparison.Ordinal);
    }
}

using FluentAssertions;
using STIGForge.Core;

namespace STIGForge.Tests.CrossPlatform.Core;

public class CsvEscapeTests
{
    [Fact]
    public void PlainString_NoSpecialChars_ReturnsUnchanged()
    {
        CsvEscape.Escape("hello world").Should().Be("hello world");
    }

    [Fact]
    public void Null_ReturnsEmptyString()
    {
        CsvEscape.Escape(null).Should().Be(string.Empty);
    }

    [Fact]
    public void StringWithComma_GetsDoubleQuoted()
    {
        CsvEscape.Escape("a,b").Should().Be("\"a,b\"");
    }

    [Fact]
    public void StringWithDoubleQuote_GetsEscapedInsideQuotes()
    {
        CsvEscape.Escape("say \"hi\"").Should().Be("\"say \"\"hi\"\"\"");
    }

    [Fact]
    public void StringWithNewline_GetsDoubleQuoted()
    {
        CsvEscape.Escape("line1\nline2").Should().Be("\"line1\nline2\"");
    }

    [Fact]
    public void StringWithCarriageReturn_GetsDoubleQuoted()
    {
        CsvEscape.Escape("line1\rline2").Should().Be("\"line1\rline2\"");
    }

    [Fact]
    public void StringWithCommaAndQuote_HandledCorrectly()
    {
        CsvEscape.Escape("a,\"b\"").Should().Be("\"a,\"\"b\"\"\"");
    }

    [Fact]
    public void EmptyString_ReturnsEmptyString()
    {
        CsvEscape.Escape(string.Empty).Should().Be(string.Empty);
    }
}

using FluentAssertions;
using STIGForge.Core.Utilities;

namespace STIGForge.UnitTests.Core;

public sealed class CsvUtilityTests
{
  [Fact]
  public void ParseLine_WithSimpleCsv_ReturnsFields()
  {
    var result = CsvUtility.ParseLine("a,b,c");

    result.Should().Equal("a", "b", "c");
  }

  [Fact]
  public void ParseLine_WithQuotedCommas_ParsesSingleField()
  {
    var result = CsvUtility.ParseLine("a,\"b,c\",d");

    result.Should().Equal("a", "b,c", "d");
  }

  [Fact]
  public void ParseLine_WithEscapedQuotes_UnescapesCorrectly()
  {
    var result = CsvUtility.ParseLine("\"a\"\"b\"");

    result.Should().Equal("a\"b");
  }

  [Fact]
  public void ParseLine_WithTrailingEmptyField_KeepsEmptyColumn()
  {
    var result = CsvUtility.ParseLine("a,b,");

    result.Should().Equal("a", "b", string.Empty);
  }

  [Fact]
  public void ParseLine_WithNullInput_ReturnsEmptyArray()
  {
    var result = CsvUtility.ParseLine(null!);

    result.Should().BeEmpty();
  }
}

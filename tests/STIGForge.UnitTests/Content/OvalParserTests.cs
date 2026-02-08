using STIGForge.Content.Import;
using STIGForge.Content.Models;
using Xunit;

namespace STIGForge.UnitTests.Content;

public class OvalParserTests
{
    private static string GetFixturePath(string filename)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "..", "..", "..", "fixtures", filename);
    }

    [Fact]
    public void ParseOval_ReturnsDefinitions()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-oval.xml");

        // Act
        var results = OvalParser.Parse(xmlPath);

        // Assert
        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void ParseOval_ExtractsDefinitionId()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-oval.xml");

        // Act
        var results = OvalParser.Parse(xmlPath);

        // Assert
        Assert.Contains(results, d => d.DefinitionId == "oval:gov.disa.stig:def:1001");
        Assert.Contains(results, d => d.DefinitionId == "oval:gov.disa.stig:def:1002");
    }

    [Fact]
    public void ParseOval_ExtractsTitle()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-oval.xml");

        // Act
        var results = OvalParser.Parse(xmlPath);

        // Assert
        var def1 = results.First(d => d.DefinitionId == "oval:gov.disa.stig:def:1001");
        Assert.Equal("Test OVAL Definition 1", def1.Title);
    }

    [Fact]
    public void ParseOval_ExtractsClass()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-oval.xml");

        // Act
        var results = OvalParser.Parse(xmlPath);

        // Assert
        var def1 = results.First(d => d.DefinitionId == "oval:gov.disa.stig:def:1001");
        Assert.Equal("compliance", def1.Class);
        
        var def2 = results.First(d => d.DefinitionId == "oval:gov.disa.stig:def:1002");
        Assert.Equal("inventory", def2.Class);
    }

    [Fact]
    public void ParseOval_HandlesEmptyFile()
    {
        // Arrange - create minimal OVAL file
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, @"<?xml version=""1.0""?>
<oval_definitions xmlns=""http://oval.mitre.org/XMLSchema/oval-definitions-5"">
  <definitions></definitions>
</oval_definitions>");

        try
        {
            // Act
            var results = OvalParser.Parse(tempFile);

            // Assert
            Assert.NotNull(results);
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseOval_RejectsDtdPayload()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "oval-dtd-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(tempFile, """
<!DOCTYPE oval_definitions [
  <!ENTITY xxe SYSTEM "file:///etc/passwd">
]>
<oval_definitions xmlns="http://oval.mitre.org/XMLSchema/oval-definitions-5">
  <definitions />
</oval_definitions>
""");

        try
        {
            var ex = Assert.Throws<ParsingException>(() => OvalParser.Parse(tempFile));
            Assert.Contains("OVAL-XML-001", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

using STIGForge.Content.Import;
using STIGForge.Core.Models;
using Xunit;

namespace STIGForge.UnitTests.Content;

public class XccdfParserTests
{
    private static string GetFixturePath(string filename)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "..", "..", "..", "fixtures", filename);
    }

    [Fact]
    public void ParseSmallXccdf_ReturnsCorrectControlCount()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-multiple-rules.xml");
        var packName = "test-pack";

        // Act
        var results = XccdfParser.Parse(xmlPath, packName);

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void ParseXccdf_ExtractsControlId()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-manual-check.xml");
        var packName = "test-pack";

        // Act
        var results = XccdfParser.Parse(xmlPath, packName);

        // Assert
        Assert.Single(results);
        var control = results[0];
        Assert.Equal("SV-123456r1_rule", control.ExternalIds.RuleId);
        Assert.Equal("test-benchmark-manual", control.ExternalIds.BenchmarkId);
    }

    [Fact]
    public void ParseXccdf_ExtractsSeverity()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-automated-check.xml");
        var packName = "test-pack";

        // Act
        var results = XccdfParser.Parse(xmlPath, packName);

        // Assert
        Assert.Single(results);
        var control = results[0];
        Assert.Equal("high", control.Severity);
    }

    [Fact]
    public void ParseXccdf_DetectsManualCheck()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-manual-check.xml");
        var packName = "test-pack";

        // Act
        var results = XccdfParser.Parse(xmlPath, packName);

        // Assert
        Assert.Single(results);
        var control = results[0];
        Assert.True(control.IsManual, "Control with system='manual' should be marked as manual");
        Assert.NotNull(control.WizardPrompt);
    }

    [Fact]
    public void ParseXccdf_DetectsAutomatedCheck()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-automated-check.xml");
        var packName = "test-pack";

        // Act
        var results = XccdfParser.Parse(xmlPath, packName);

        // Assert
        Assert.Single(results);
        var control = results[0];
        Assert.False(control.IsManual, "Control with SCC system should be marked as automated");
        Assert.Null(control.WizardPrompt);
    }

    [Fact]
    public void ParseXccdf_HandlesCorruptXml()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-corrupt.xml");
        var packName = "test-pack";

        // Act & Assert
        // Should throw XmlException or return empty list, not crash
        var exception = Record.Exception(() => XccdfParser.Parse(xmlPath, packName));
        
        // Either throws a proper exception or returns empty
        if (exception == null)
        {
            var results = XccdfParser.Parse(xmlPath, packName);
            Assert.Empty(results);
        }
        else
        {
            Assert.IsType<System.Xml.XmlException>(exception);
        }
    }

    [Fact]
    public void ParseXccdf_ExtractsVulnId()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-manual-check.xml");
        var packName = "test-pack";

        // Act
        var results = XccdfParser.Parse(xmlPath, packName);

        // Assert
        Assert.Single(results);
        var control = results[0];
        Assert.Equal("V-123456", control.ExternalIds.VulnId);
    }

    [Fact]
    public void ParseXccdf_ExtractsAllFields()
    {
        // Arrange
        var xmlPath = GetFixturePath("test-manual-check.xml");
        var packName = "test-pack";

        // Act
        var results = XccdfParser.Parse(xmlPath, packName);

        // Assert
        Assert.Single(results);
        var control = results[0];
        
        Assert.NotEmpty(control.ControlId);
        Assert.Equal("Test Manual Check V-12345", control.Title);
        Assert.Equal("medium", control.Severity);
        Assert.Contains("test description", control.Discussion);
        Assert.Contains("Manually review", control.CheckText);
        Assert.Contains("Apply the security fix", control.FixText);
        Assert.Equal(packName, control.Revision.PackName);
    }
}

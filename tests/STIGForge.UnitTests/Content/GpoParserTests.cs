using STIGForge.Content.Import;
using Xunit;

namespace STIGForge.UnitTests.Content;

public class GpoParserTests
{
    private static string GetFixturePath(string filename)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "..", "..", "..", "fixtures", filename);
    }

    [Fact]
    public void ParseAdmx_ReturnsControlRecords()
    {
        // Arrange
        var admxPath = GetFixturePath("test-admx.xml");
        var packName = "test-gpo-pack";

        // Act
        var results = GpoParser.Parse(admxPath, packName);

        // Assert
        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.Equal(2, results.Count); // Two policies in test fixture
    }

    [Fact]
    public void ParseAdmx_ExtractsRegistryKey()
    {
        // Arrange
        var admxPath = GetFixturePath("test-admx.xml");
        var packName = "test-gpo-pack";

        // Act
        var results = GpoParser.Parse(admxPath, packName);

        // Assert
        var policy1 = results.First(r => r.ExternalIds.RuleId == "TestPolicy1");
        Assert.Contains("Registry Key:", policy1.Discussion);
        Assert.Contains("SOFTWARE\\Policies\\Test", policy1.Discussion);
    }

    [Fact]
    public void ParseAdmx_SetsIsManualFalse()
    {
        // Arrange
        var admxPath = GetFixturePath("test-admx.xml");
        var packName = "test-gpo-pack";

        // Act
        var results = GpoParser.Parse(admxPath, packName);

        // Assert
        Assert.All(results, r => Assert.False(r.IsManual, "GPO policies should be automated (IsManual = false)"));
    }

    [Fact]
    public void ParseAdmx_ExtractsPolicyName()
    {
        // Arrange
        var admxPath = GetFixturePath("test-admx.xml");
        var packName = "test-gpo-pack";

        // Act
        var results = GpoParser.Parse(admxPath, packName);

        // Assert
        Assert.Contains(results, r => r.ExternalIds.RuleId == "TestPolicy1");
        Assert.Contains(results, r => r.ExternalIds.RuleId == "TestPolicy2");
    }

    [Fact]
    public void ParseAdmx_SetsCheckText()
    {
        // Arrange
        var admxPath = GetFixturePath("test-admx.xml");
        var packName = "test-gpo-pack";

        // Act
        var results = GpoParser.Parse(admxPath, packName);

        // Assert
        var policy1 = results.First(r => r.ExternalIds.RuleId == "TestPolicy1");
        Assert.Contains("Verify registry value", policy1.CheckText);
        Assert.Contains("Enabled", policy1.CheckText);
    }
}

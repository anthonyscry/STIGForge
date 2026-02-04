using STIGForge.Content.Import;
using Xunit;

namespace STIGForge.UnitTests.Content;

public class ScapBundleParserTests
{
    private static string GetFixturePath(string filename)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "..", "..", "..", "fixtures", filename);
    }

    [Fact]
    public void ParseBundle_FindsXccdfFiles()
    {
        // Arrange
        var bundlePath = GetFixturePath("test-scap-bundle.zip");
        var packName = "test-pack";

        // Act
        var results = ScapBundleParser.Parse(bundlePath, packName);

        // Assert
        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.Single(results); // One rule from scap-bundle-xccdf.xml
    }

    [Fact]
    public void ParseBundle_ExtractsControlFromXccdf()
    {
        // Arrange
        var bundlePath = GetFixturePath("test-scap-bundle.zip");
        var packName = "test-pack";

        // Act
        var results = ScapBundleParser.Parse(bundlePath, packName);

        // Assert
        var control = results.First();
        Assert.Equal("SV-999999r1_rule", control.ExternalIds.RuleId);
        Assert.Equal("Test SCAP Bundle Rule", control.Title);
        Assert.Equal("medium", control.Severity);
    }

    [Fact]
    public void ParseBundle_HandlesNonExistentFile()
    {
        // Arrange
        var bundlePath = "nonexistent.zip";
        var packName = "test-pack";

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => ScapBundleParser.Parse(bundlePath, packName));
    }
}

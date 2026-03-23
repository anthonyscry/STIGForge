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
    public async Task ParseBundle_FindsXccdfFiles()
    {
        // Arrange
        var bundlePath = GetFixturePath("test-scap-bundle.zip");
        var packName = "test-pack";

        // Act
        var results = await ScapBundleParser.ParseAsync(bundlePath, packName);

        // Assert
        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.Single(results); // One rule from scap-bundle-xccdf.xml
    }

    [Fact]
    public async Task ParseBundle_ExtractsControlFromXccdf()
    {
        // Arrange
        var bundlePath = GetFixturePath("test-scap-bundle.zip");
        var packName = "test-pack";

        // Act
        var results = await ScapBundleParser.ParseAsync(bundlePath, packName);

        // Assert
        var control = results.First();
        Assert.Equal("SV-999999r1_rule", control.ExternalIds.RuleId);
        Assert.Equal("Test SCAP Bundle Rule", control.Title);
        Assert.Equal("medium", control.Severity);
    }

    [Fact]
    public async Task ParseBundle_HandlesNonExistentFile()
    {
        // Arrange
        var bundlePath = "nonexistent.zip";
        var packName = "test-pack";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => ScapBundleParser.ParseAsync(bundlePath, packName));
    }

    [Fact]
    public async Task ParseBundle_RejectsPathTraversalEntry()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), "scap-traversal-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../outside.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("blocked");
            }

            var ex = await Assert.ThrowsAsync<STIGForge.Content.Models.ParsingException>(() => ScapBundleParser.ParseAsync(tempZip, "test-pack"));
            Assert.Contains("IMPORT-ARCHIVE-002", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }
}

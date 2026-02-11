using STIGForge.Content.Import;
using STIGForge.Core.Models;
using System.Xml;
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

    [Fact]
    public void ParseXccdf_ExtractsFullBenchmarkMetadata()
    {
        var xml = CreateXccdfWithMetadata(
            version: "2.7",
            statusDate: "2025-10-01",
            platformIdRef: "cpe:/o:microsoft:windows_server_2022",
            rearMatter: "releaseinfo:--:Release: 7 Benchmark Date: 01 Oct 2025\nclassification:--:UNCLASSIFIED");
        var xmlPath = WriteTempXccdf(xml);

        try
        {
            var results = XccdfParser.Parse(xmlPath, "full-meta-pack");

            var control = Assert.Single(results);
            Assert.Equal("2.7", control.Revision.BenchmarkVersion);
            Assert.Equal("Release: 7 Benchmark Date: 01 Oct 2025", control.Revision.BenchmarkRelease);
            Assert.NotNull(control.Revision.BenchmarkDate);
            Assert.Equal(2025, control.Revision.BenchmarkDate!.Value.Year);
            Assert.Equal(10, control.Revision.BenchmarkDate!.Value.Month);
            Assert.Equal(1, control.Revision.BenchmarkDate!.Value.Day);
            Assert.Equal(OsTarget.Server2022, control.Applicability.OsTarget);
            Assert.Equal(ScopeTag.UnclassifiedOnly, control.Applicability.ClassificationScope);
            Assert.Equal(Confidence.High, control.Applicability.Confidence);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public void ParseXccdf_ExtractsPartialBenchmarkMetadata()
    {
        var xml = CreateXccdfWithMetadata(version: "3.1");
        var xmlPath = WriteTempXccdf(xml);

        try
        {
            var results = XccdfParser.Parse(xmlPath, "partial-meta-pack");

            var control = Assert.Single(results);
            Assert.Equal("3.1", control.Revision.BenchmarkVersion);
            Assert.Null(control.Revision.BenchmarkDate);
            Assert.Equal(Confidence.Medium, control.Applicability.Confidence);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public void ParseXccdf_NoBenchmarkMetadata_UsesFallbacks()
    {
        var xml = CreateXccdfWithMetadata();
        var xmlPath = WriteTempXccdf(xml);

        try
        {
            var results = XccdfParser.Parse(xmlPath, "no-meta-pack");

            var control = Assert.Single(results);
            Assert.Null(control.Revision.BenchmarkVersion);
            Assert.Null(control.Revision.BenchmarkRelease);
            Assert.Null(control.Revision.BenchmarkDate);
            Assert.Equal(OsTarget.Unknown, control.Applicability.OsTarget);
            Assert.Equal(ScopeTag.Unknown, control.Applicability.ClassificationScope);
            Assert.Equal(Confidence.Low, control.Applicability.Confidence);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Theory]
    [InlineData("cpe:/o:microsoft:windows_11", OsTarget.Win11)]
    [InlineData("cpe:/o:microsoft:win11", OsTarget.Win11)]
    [InlineData("cpe:/o:microsoft:windows_10", OsTarget.Win10)]
    [InlineData("cpe:/o:microsoft:win10", OsTarget.Win10)]
    [InlineData("cpe:/o:microsoft:windows_server_2019", OsTarget.Server2019)]
    [InlineData("cpe:/o:microsoft:windows_server_2022", OsTarget.Server2022)]
    [InlineData("cpe:/o:microsoft:linux", OsTarget.Unknown)]
    public void ParseXccdf_MapsPlatformCpeToOsTarget(string platformIdRef, OsTarget expectedTarget)
    {
        var xml = CreateXccdfWithMetadata(platformIdRef: platformIdRef);
        var xmlPath = WriteTempXccdf(xml);

        try
        {
            var results = XccdfParser.Parse(xmlPath, "platform-map-pack");

            var control = Assert.Single(results);
            Assert.Equal(expectedTarget, control.Applicability.OsTarget);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Theory]
    [InlineData("CLASSIFIED", ScopeTag.ClassifiedOnly)]
    [InlineData("UNCLASSIFIED", ScopeTag.UnclassifiedOnly)]
    [InlineData("MIXED", ScopeTag.Both)]
    [InlineData("BOTH", ScopeTag.Both)]
    [InlineData("UNKNOWN_VALUE", ScopeTag.Unknown)]
    public void ParseXccdf_MapsRearMatterClassificationToScope(string classification, ScopeTag expectedScope)
    {
        var rearMatter = $"classification:--:{classification}";
        var xml = CreateXccdfWithMetadata(rearMatter: rearMatter);
        var xmlPath = WriteTempXccdf(xml);

        try
        {
            var results = XccdfParser.Parse(xmlPath, "classification-map-pack");

            var control = Assert.Single(results);
            Assert.Equal(expectedScope, control.Applicability.ClassificationScope);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public void ParseXccdf_DtdIsProhibited()
    {
        var xmlPath = WriteTempXccdf("""
<!DOCTYPE Benchmark [<!ENTITY xxe "forbidden">]>
<Benchmark xmlns="http://checklists.nist.gov/xccdf/1.2" id="test-dtd-benchmark">
  <Rule id="SV-100000r1_rule" severity="medium">
    <title>DTD Rule</title>
    <description>DTD should be blocked.</description>
    <check system="manual">
      <check-content>Check content</check-content>
    </check>
    <fixtext>Fix content</fixtext>
  </Rule>
</Benchmark>
""");

        try
        {
            Assert.Throws<XmlException>(() => XccdfParser.Parse(xmlPath, "dtd-pack"));
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static string WriteTempXccdf(string xml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stigforge-xccdf-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    private static string CreateXccdfWithMetadata(
        string? version = null,
        string? statusDate = null,
        string? platformIdRef = null,
        string? rearMatter = null)
    {
        var versionElement = version == null ? string.Empty : $"<version>{version}</version>";
        var statusElement = statusDate == null ? string.Empty : $"<status date=\"{statusDate}\">accepted</status>";
        var platformElement = platformIdRef == null ? string.Empty : $"<platform idref=\"{platformIdRef}\" />";
        var rearMatterElement = rearMatter == null ? string.Empty : $"<rear-matter>{rearMatter}</rear-matter>";

        return $$"""
<?xml version="1.0" encoding="UTF-8"?>
<Benchmark xmlns="http://checklists.nist.gov/xccdf/1.2" id="test-meta-benchmark">
  {{versionElement}}
  {{statusElement}}
  {{platformElement}}
  {{rearMatterElement}}
  <Rule id="SV-100000r1_rule" severity="medium">
    <title>Metadata Rule</title>
    <description>Metadata validation rule.</description>
    <check system="manual">
      <check-content>Manually validate metadata.</check-content>
    </check>
    <fixtext>Apply metadata fix.</fixtext>
  </Rule>
</Benchmark>
""";
    }
}

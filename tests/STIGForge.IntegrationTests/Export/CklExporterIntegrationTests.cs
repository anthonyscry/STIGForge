using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using STIGForge.Export;
using STIGForge.Verify;

namespace STIGForge.IntegrationTests.Export;

public class CklExporterIntegrationTests : IDisposable
{
    private readonly string _testRoot;

    public CklExporterIntegrationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "stigforge_test_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }

    private void CreateBundleWithResults(string bundleRoot, List<ControlResult> results)
    {
        var verifyDir = Path.Combine(bundleRoot, "Verify");
        Directory.CreateDirectory(verifyDir);

        var report = new VerifyReport
        {
            Tool = "TestTool",
            ToolVersion = "1.0",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            FinishedAt = DateTimeOffset.UtcNow,
            OutputRoot = verifyDir,
            Results = results
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"), json);
    }

    [Fact]
    public void ExportCkl_WithResults_CreatesCklFile()
    {
        var bundleRoot = Path.Combine(_testRoot, "bundle1");
        Directory.CreateDirectory(bundleRoot);

        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = "V-100001",
                RuleId = "SV-100001r1_rule",
                Title = "Test Control 1",
                Severity = "high",
                Status = "Open",
                FindingDetails = "Found an issue",
                Comments = "Needs remediation",
                Tool = "TestTool",
                SourceFile = "test.xml"
            },
            new()
            {
                VulnId = "V-100002",
                RuleId = "SV-100002r1_rule",
                Title = "Test Control 2",
                Severity = "medium",
                Status = "NotAFinding",
                Tool = "TestTool",
                SourceFile = "test.xml"
            }
        };

        CreateBundleWithResults(bundleRoot, results);

    var result = CklExporter.ExportCkl(new CklExportRequest
    {
        BundleRoot = bundleRoot,
        HostName = "TESTHOST",
        StigId = "Test_STIG"
    });

        result.ControlCount.Should().Be(2);
        result.OutputPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(result.OutputPath).Should().BeTrue();

        var xml = File.ReadAllText(result.OutputPath);
        xml.Should().Contain("VULN");
        xml.Should().Contain("V-100001");
        xml.Should().Contain("V-100002");
    }

    [Fact]
    public void ExportCkl_EmptyBundle_ReturnsZeroCount()
    {
        // Bundle root exists but has no Verify directory
        var bundleRoot = Path.Combine(_testRoot, "bundle-empty");
        Directory.CreateDirectory(bundleRoot);

        var result = CklExporter.ExportCkl(new CklExportRequest
        {
            BundleRoot = bundleRoot
        });

    result.ControlCount.Should().Be(0);
  }

    [Fact]
    public void ExportCkl_MissingBundleRoot_Throws()
    {
        var act = () => CklExporter.ExportCkl(new CklExportRequest
        {
            BundleRoot = string.Empty
        });

    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void ExportCkl_DeduplicatesControls_PreservesMergedDetailsAndComments()
  {
    var bundleRoot = Path.Combine(_testRoot, "bundle-merge");
    Directory.CreateDirectory(bundleRoot);

    CreateBundleWithResults(bundleRoot, new List<ControlResult>
    {
      new()
      {
        VulnId = "V-50001",
        RuleId = "SV-50001r1_rule",
        Title = "Integration Merge",
        Severity = "medium",
        Status = "Open",
        FindingDetails = "Base detail",
        Comments = "Base comment",
        Tool = "TestTool",
        SourceFile = "test.xml"
      }
    });

    AddAdditionalReport(bundleRoot, "run2", new List<ControlResult>
    {
      new()
      {
        VulnId = "V-50001",
        RuleId = "SV-50001r1_rule",
        Title = "Integration Merge",
        Severity = "medium",
        Status = "NotAFinding",
        FindingDetails = "Secondary detail",
        Comments = "Secondary comment",
        Tool = "TestTool",
        SourceFile = "test.xml"
      }
    });

    var result = CklExporter.ExportCkl(new CklExportRequest { BundleRoot = bundleRoot });
    var doc = XDocument.Load(result.OutputPath);
    var vuln = doc.Descendants("VULN").Single();
    vuln.Element("FINDING_DETAILS")!.Value.Should().Contain("Base detail");
    vuln.Element("FINDING_DETAILS")!.Value.Should().Contain("Secondary detail");
    vuln.Element("COMMENTS")!.Value.Should().Contain("Base comment");
    vuln.Element("COMMENTS")!.Value.Should().Contain("Secondary comment");
  }

  [Fact]
  public void ExportCkl_DoesNotDuplicateRepeatedTextAcrossReports()
  {
    const string repeatedDetail = "Consistent detail";
    const string repeatedComment = "Consistent comment";
    var bundleRoot = Path.Combine(_testRoot, "bundle-repeated-text");
    Directory.CreateDirectory(bundleRoot);

    CreateBundleWithResults(bundleRoot, new List<ControlResult>
    {
      new()
      {
        VulnId = "V-50200",
        RuleId = "SV-50200r1_rule",
        Title = "Repeated Integration Text",
        Severity = "medium",
        Status = "Open",
        FindingDetails = repeatedDetail,
        Comments = repeatedComment,
        Tool = "TestTool",
        SourceFile = "test.xml"
      }
    });

    AddAdditionalReport(bundleRoot, "run2", new List<ControlResult>
    {
      new()
      {
        VulnId = "V-50200",
        RuleId = "SV-50200r1_rule",
        Title = "Repeated Integration Text",
        Severity = "medium",
        Status = "Open",
        FindingDetails = repeatedDetail,
        Comments = repeatedComment,
        Tool = "TestTool",
        SourceFile = "test.xml"
      }
    });

    AddAdditionalReport(bundleRoot, "run3", new List<ControlResult>
    {
      new()
      {
        VulnId = "V-50200",
        RuleId = "SV-50200r1_rule",
        Title = "Repeated Integration Text",
        Severity = "medium",
        Status = "Open",
        FindingDetails = repeatedDetail,
        Comments = repeatedComment,
        Tool = "TestTool",
        SourceFile = "test.xml"
      }
    });

    var result = CklExporter.ExportCkl(new CklExportRequest { BundleRoot = bundleRoot });
    var doc = XDocument.Load(result.OutputPath);
    var vuln = doc.Descendants("VULN").Single();

    vuln.Element("FINDING_DETAILS")!.Value.Should().Be(repeatedDetail);
    vuln.Element("COMMENTS")!.Value.Should().Be(repeatedComment);
  }

  private void AddAdditionalReport(string bundleRoot, string runName, IReadOnlyList<ControlResult> results)
  {
    var verifyDir = Path.Combine(bundleRoot, "Verify", runName);
    Directory.CreateDirectory(verifyDir);

    var report = new VerifyReport
    {
      Tool = "TestTool",
      ToolVersion = "1.0",
      StartedAt = DateTimeOffset.UtcNow,
      FinishedAt = DateTimeOffset.UtcNow,
      OutputRoot = verifyDir,
      Results = results
    };

    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = true
    });

    File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"), json);
  }
}

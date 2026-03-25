using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Evidence;
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

  private void CreateEvidenceArtifact(string bundleRoot, string controlId, string baseName, string contentText, string metadataJson)
  {
    var controlDir = Path.Combine(bundleRoot, "Evidence", "by_control", controlId);
    Directory.CreateDirectory(controlDir);
    File.WriteAllText(Path.Combine(controlDir, baseName + ".txt"), contentText);
    File.WriteAllText(Path.Combine(controlDir, baseName + ".json"), metadataJson);
  }

  [Fact]
  public void ExportCkl_WithEvidenceCompiler_PopulatesFindingDetails()
  {
    var bundleRoot = Path.Combine(_testRoot, "bundle-evidence");
    Directory.CreateDirectory(bundleRoot);

    CreateBundleWithResults(bundleRoot, new List<ControlResult>
    {
      new()
      {
        VulnId = "V-99901",
        RuleId = "SV-99901r1_rule",
        Title = "EnableLUA Test Control",
        Severity = "high",
        Status = "NotAFinding",
        Tool = "TestTool",
        SourceFile = "test.xml"
      }
    });

    var metadataJson = """
      {
        "ControlId": "V-99901",
        "Type": "Registry",
        "Source": "reg.exe",
        "TimestampUtc": "2026-03-20T14:30:00Z",
        "Sha256": "abc123"
      }
      """;

    CreateEvidenceArtifact(bundleRoot, "V-99901", "test_evidence", "HKLM\\EnableLUA = 1", metadataJson);

    var result = CklExporter.ExportCkl(new CklExportRequest
    {
      BundleRoot = bundleRoot,
      HostName = "TESTHOST",
      StigId = "Test_STIG"
    }, new EvidenceCompiler());

    result.ControlCount.Should().BeGreaterThan(0);
    File.Exists(result.OutputPath).Should().BeTrue();

    var doc = XDocument.Load(result.OutputPath);
    var vulns = doc.Descendants("VULN").ToList();
    vulns.Should().NotBeEmpty();

    var findingDetails = vulns
      .Select(v => v.Element("FINDING_DETAILS")?.Value ?? string.Empty)
      .ToList();

    findingDetails.Should().Contain(fd => fd.Contains("STIGForge Evidence Report"),
      "at least one VULN element should have FINDING_DETAILS containing 'STIGForge Evidence Report'");

    var comments = vulns
      .Select(v => v.Element("COMMENTS")?.Value ?? string.Empty)
      .ToList();

    comments.Should().Contain(c => !string.IsNullOrWhiteSpace(c),
      "at least one VULN element should have non-empty COMMENTS");
  }

  [Fact]
  public void ExportCkl_NullCompiler_BackwardCompatible()
  {
    var bundleRoot = Path.Combine(_testRoot, "bundle-no-compiler");
    Directory.CreateDirectory(bundleRoot);

    CreateBundleWithResults(bundleRoot, new List<ControlResult>
    {
      new()
      {
        VulnId = "V-99901",
        RuleId = "SV-99901r1_rule",
        Title = "EnableLUA Test Control",
        Severity = "high",
        Status = "NotAFinding",
        Tool = "TestTool",
        SourceFile = "test.xml"
      }
    });

    var metadataJson = """
      {
        "ControlId": "V-99901",
        "Type": "Registry",
        "Source": "reg.exe",
        "TimestampUtc": "2026-03-20T14:30:00Z",
        "Sha256": "abc123"
      }
      """;

    CreateEvidenceArtifact(bundleRoot, "V-99901", "test_evidence", "HKLM\\EnableLUA = 1", metadataJson);

    // Call without compiler  -  backward compatible path
    var result = CklExporter.ExportCkl(new CklExportRequest
    {
      BundleRoot = bundleRoot,
      HostName = "TESTHOST",
      StigId = "Test_STIG"
    });

    result.ControlCount.Should().BeGreaterThan(0);
    File.Exists(result.OutputPath).Should().BeTrue();

    var doc = XDocument.Load(result.OutputPath);
    var vulns = doc.Descendants("VULN").ToList();
    vulns.Should().NotBeEmpty();

    var findingDetails = vulns
      .Select(v => v.Element("FINDING_DETAILS")?.Value ?? string.Empty)
      .ToList();

    findingDetails.Should().NotContain(fd => fd.Contains("STIGForge Evidence Report"),
      "without an evidence compiler, FINDING_DETAILS should not contain 'STIGForge Evidence Report'");
  }

  [Fact]
  public void ExportCkl_CompilerThrows_GracefulDegradation()
  {
    var bundleRoot = Path.Combine(_testRoot, "bundle-throwing-compiler");
    Directory.CreateDirectory(bundleRoot);

    CreateBundleWithResults(bundleRoot, new List<ControlResult>
    {
      new()
      {
        VulnId = "V-99901",
        RuleId = "SV-99901r1_rule",
        Title = "EnableLUA Test Control",
        Severity = "high",
        Status = "NotAFinding",
        Tool = "TestTool",
        SourceFile = "test.xml"
      }
    });

    var result = CklExporter.ExportCkl(new CklExportRequest
    {
      BundleRoot = bundleRoot,
      HostName = "TESTHOST",
      StigId = "Test_STIG"
    }, new ThrowingCompiler());

    result.ControlCount.Should().BeGreaterThan(0);
    File.Exists(result.OutputPath).Should().BeTrue();
  }

  private sealed class ThrowingCompiler : IEvidenceCompiler
  {
    public CompiledEvidence? CompileEvidence(EvidenceCompilationInput input, string bundleRoot)
      => throw new InvalidOperationException("test exception");
  }
}

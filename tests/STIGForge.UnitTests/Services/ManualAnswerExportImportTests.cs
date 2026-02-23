using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public class ManualAnswerExportImportTests : IDisposable
{
  private readonly string _tempDir;
  private readonly ManualAnswerService _svc;

  public ManualAnswerExportImportTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-export-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(Path.Combine(_tempDir, "Manual"));
    _svc = new ManualAnswerService();
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  private string CreateBundleWithAnswers(params (string ruleId, string vulnId, string status, string? reason)[] answers)
  {
    var dir = Path.Combine(Path.GetTempPath(), "stigforge-export-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(Path.Combine(dir, "Manual"));

    var answerFile = new AnswerFile
    {
      ProfileId = "TestProfile",
      PackId = "TestPack",
      CreatedAt = DateTimeOffset.UtcNow,
      Answers = answers.Select(a => new ManualAnswer
      {
        RuleId = a.ruleId,
        VulnId = a.vulnId,
        Status = a.status,
        Reason = a.reason,
        UpdatedAt = DateTimeOffset.UtcNow
      }).ToList()
    };

    _svc.SaveAnswerFile(dir, answerFile);
    return dir;
  }

  [Fact]
  public void Export_IncludesMetadata()
  {
    var bundle = CreateBundleWithAnswers(
      ("SV-001r1", "V-001", "Pass", "Verified"),
      ("SV-002r1", "V-002", "Fail", "Non-compliant state"));

    var export = _svc.ExportAnswers(bundle, "Windows_11_STIG");

    export.StigId.Should().Be("Windows_11_STIG");
    export.ExportedAt.Should().NotBeNullOrWhiteSpace();
    export.ExportedBy.Should().NotBeNullOrWhiteSpace();
    export.Answers.Should().NotBeNull();
    export.Answers.Answers.Should().HaveCount(2);

    Directory.Delete(bundle, true);
  }

  [Fact]
  public void Import_MatchesByRuleId()
  {
    var target = CreateBundleWithAnswers(); // empty bundle

    var import = new AnswerFileExport
    {
      StigId = "Test",
      ExportedAt = DateTimeOffset.UtcNow.ToString("o"),
      ExportedBy = "tester",
      Answers = new AnswerFile
      {
        Answers = new List<ManualAnswer>
        {
          new() { RuleId = "SV-100r1", VulnId = "V-100", Status = "Pass", Reason = "OK" },
          new() { RuleId = "SV-200r1", VulnId = "V-200", Status = "Fail", Reason = "Bad config" }
        }
      }
    };

    var result = _svc.ImportAnswers(target, import);

    result.Imported.Should().Be(2);
    result.Skipped.Should().Be(0);

    var loaded = _svc.LoadAnswerFile(target);
    loaded.Answers.Should().HaveCount(2);
    loaded.Answers.Should().Contain(a => a.RuleId == "SV-100r1" && a.Status == "Pass");
    loaded.Answers.Should().Contain(a => a.RuleId == "SV-200r1" && a.Status == "Fail");

    Directory.Delete(target, true);
  }

  [Fact]
  public void Import_SkipsResolvedAnswers()
  {
    var target = CreateBundleWithAnswers(("SV-100r1", "V-100", "Pass", "Already resolved"));

    var import = new AnswerFileExport
    {
      StigId = "Test",
      Answers = new AnswerFile
      {
        Answers = new List<ManualAnswer>
        {
          new() { RuleId = "SV-100r1", VulnId = "V-100", Status = "Fail", Reason = "Imported override" }
        }
      }
    };

    var result = _svc.ImportAnswers(target, import);

    result.Imported.Should().Be(0);
    result.Skipped.Should().Be(1);
    result.SkippedControls.Should().Contain("SV-100r1");

    // Verify original answer preserved
    var loaded = _svc.LoadAnswerFile(target);
    var answer = loaded.Answers.First(a => a.RuleId == "SV-100r1");
    answer.Status.Should().Be("Pass");
    answer.Reason.Should().Be("Already resolved");

    Directory.Delete(target, true);
  }

  [Fact]
  public void Import_OverwritesOpenAnswers()
  {
    var target = CreateBundleWithAnswers(("SV-100r1", "V-100", "Open", null));

    var import = new AnswerFileExport
    {
      StigId = "Test",
      Answers = new AnswerFile
      {
        Answers = new List<ManualAnswer>
        {
          new() { RuleId = "SV-100r1", VulnId = "V-100", Status = "Pass", Reason = "Resolved in source" }
        }
      }
    };

    var result = _svc.ImportAnswers(target, import);

    result.Imported.Should().Be(1);
    result.Skipped.Should().Be(0);

    var loaded = _svc.LoadAnswerFile(target);
    var answer = loaded.Answers.First(a => a.RuleId == "SV-100r1");
    answer.Status.Should().Be("Pass");
    answer.Reason.Should().Be("Resolved in source");

    Directory.Delete(target, true);
  }

  [Fact]
  public void RoundTrip_ExportThenImport()
  {
    var source = CreateBundleWithAnswers(
      ("SV-001r1", "V-001", "Pass", "Verified OK"),
      ("SV-002r1", "V-002", "NotApplicable", "Not relevant to environment"),
      ("SV-003r1", "V-003", "Fail", "Missing configuration"));

    var exportPath = Path.Combine(_tempDir, "export.json");
    var export = _svc.ExportAnswers(source, "RoundTrip_STIG");
    _svc.WriteExportFile(exportPath, export);

    // Read back and import into fresh bundle
    var readBack = _svc.ReadExportFile(exportPath);
    readBack.StigId.Should().Be("RoundTrip_STIG");
    readBack.Answers.Answers.Should().HaveCount(3);

    var target = CreateBundleWithAnswers(); // empty
    var result = _svc.ImportAnswers(target, readBack);

    result.Imported.Should().Be(3);
    result.Skipped.Should().Be(0);

    var loaded = _svc.LoadAnswerFile(target);
    loaded.Answers.Should().HaveCount(3);
    loaded.Answers.Should().Contain(a => a.RuleId == "SV-001r1" && a.Status == "Pass");
    loaded.Answers.Should().Contain(a => a.RuleId == "SV-002r1" && a.Status == "NotApplicable");
    loaded.Answers.Should().Contain(a => a.RuleId == "SV-003r1" && a.Status == "Fail");

    Directory.Delete(source, true);
    Directory.Delete(target, true);
  }
}

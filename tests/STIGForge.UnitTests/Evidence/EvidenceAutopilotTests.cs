using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Evidence;

namespace STIGForge.UnitTests.Evidence;

public sealed class EvidenceAutopilotTests : IDisposable
{
  private readonly string _tempRoot;
  private readonly string _evidenceRoot;

  public EvidenceAutopilotTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-autopilot-test-" + Guid.NewGuid().ToString("N"));
    _evidenceRoot = Path.Combine(_tempRoot, "Evidence");
    Directory.CreateDirectory(_evidenceRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempRoot, true); } catch { }
  }

  [Fact]
  public async Task CollectEvidenceAsync_CreatesPerControlDirectoryAndSummary()
  {
    var autopilot = new EvidenceAutopilot(_evidenceRoot);
    var control = MakeControl("SV-777_rule", "V-777", "Check HKEY_LOCAL_MACHINE for setting compliance.");

    var result = await autopilot.CollectEvidenceAsync(control, CancellationToken.None);
    var controlDir = Path.Combine(_evidenceRoot, "by_control", "V-777");

    result.ControlId.Should().Be("V-777");
    Directory.Exists(controlDir).Should().BeTrue();
    File.Exists(Path.Combine(controlDir, "_collection_summary.txt")).Should().BeTrue();
    result.EvidenceFiles.Should().NotBeEmpty();
  }

  [Fact]
  public async Task CollectCommandEvidenceAsync_CommandUnavailable_WritesErrorFile()
  {
    var autopilot = new EvidenceAutopilot(_evidenceRoot);
    var outputDir = Path.Combine(_evidenceRoot, "by_control", "SV-999");
    Directory.CreateDirectory(outputDir);

    var files = await autopilot.CollectCommandEvidenceAsync(
      "this-command-does-not-exist-xyz",
      "",
      outputDir,
      CancellationToken.None);

    files.Should().ContainSingle();
    var errorPath = files[0];
    Path.GetFileName(errorPath).Should().Be("command_error.txt");
    File.Exists(errorPath).Should().BeTrue();
    File.ReadAllText(errorPath).Should().Contain("Failed to execute command");
  }

  [Fact]
  public async Task CollectFileEvidenceAsync_SourceMissing_WritesNotFoundMarker()
  {
    var autopilot = new EvidenceAutopilot(_evidenceRoot);
    var outputDir = Path.Combine(_evidenceRoot, "by_control", "SV-404");
    Directory.CreateDirectory(outputDir);

    var files = await autopilot.CollectFileEvidenceAsync(
      Path.Combine(_tempRoot, "missing.config"),
      outputDir,
      CancellationToken.None);

    files.Should().ContainSingle();
    var markerPath = files[0];
    Path.GetFileName(markerPath).Should().Be("file_not_found.txt");
    File.Exists(markerPath).Should().BeTrue();
    File.ReadAllText(markerPath).Should().Contain("File not found");
  }

  private static ControlRecord MakeControl(string ruleId, string vulnId, string checkText)
  {
    return new ControlRecord
    {
      ControlId = ruleId,
      Title = "Auto Evidence Control",
      Severity = "medium",
      CheckText = checkText,
      FixText = "Fix text",
      IsManual = true,
      ExternalIds = new ExternalIds
      {
        RuleId = ruleId,
        VulnId = vulnId,
        BenchmarkId = "WIN11"
      },
      Applicability = new Applicability
      {
        OsTarget = OsTarget.Win11,
        RoleTags = Array.Empty<RoleTemplate>(),
        ClassificationScope = ScopeTag.Both,
        Confidence = Confidence.High
      },
      Revision = new RevisionInfo
      {
        PackName = "EvidencePack"
      }
    };
  }
}

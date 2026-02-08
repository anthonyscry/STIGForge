using FluentAssertions;
using STIGForge.Verify;
using System.Text.Json;

namespace STIGForge.UnitTests.Verify;

public sealed class VerifyOrchestratorTests : IDisposable
{
  private readonly string _tempDir;

  public VerifyOrchestratorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-verify-orchestrator-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public void MergeReports_ManualResultOverridesAutomatedConflict()
  {
    var scap = Report("SCAP", Result("V-1", VerifyStatus.Fail, "SCAP", "scap.xml", "scap finding"));
    var eval = Report("Evaluate-STIG", Result("V-1", VerifyStatus.Fail, "Evaluate-STIG", "eval.xml", "eval finding"));
    var ckl = Report("Manual CKL", Result("V-1", VerifyStatus.Pass, "Manual CKL", "manual.ckl", "manual finding"));

    var orchestrator = new VerifyOrchestrator();
    var consolidated = orchestrator.MergeReports(new[] { scap, eval, ckl }, Array.Empty<string>());

    consolidated.Results.Should().HaveCount(1);
    consolidated.Results[0].Status.Should().Be(VerifyStatus.Pass);
    consolidated.Conflicts.Should().ContainSingle();
    consolidated.Conflicts[0].ResolutionReason.Should().Contain("Manual CKL");
  }

  [Fact]
  public void MergeReports_IsDeterministicWhenInputOrderChanges()
  {
    var r1 = Report("SCAP",
      Result("V-2", VerifyStatus.Pass, "SCAP", "1.xml", "comment-b", new[] { "C:/B.txt" }, new Dictionary<string, string> { ["z"] = "1", ["a"] = "2" }),
      Result("V-1", VerifyStatus.Fail, "SCAP", "2.xml", "comment-a", new[] { "C:/A.txt" }, new Dictionary<string, string> { ["m"] = "3" }));

    var r2 = Report("Evaluate-STIG",
      Result("V-1", VerifyStatus.Pass, "Evaluate-STIG", "3.xml", "comment-c", new[] { "C:/A.txt" }, new Dictionary<string, string> { ["b"] = "4" }));

    var orchestrator = new VerifyOrchestrator();
    var first = orchestrator.MergeReports(new[] { r1, r2 }, Array.Empty<string>());
    var second = orchestrator.MergeReports(new[] { r2, r1 }, Array.Empty<string>());

    BuildDeterministicSnapshot(first).Should().Be(BuildDeterministicSnapshot(second));
  }

  [Fact]
  public void ParseAndMergeResults_CollectsErrorsForUnreadableFiles()
  {
    var validCkl = Path.Combine(_tempDir, "valid.ckl");
    File.WriteAllText(validCkl, """
<CHECKLIST>
  <VULN>
    <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-500</ATTRIBUTE_DATA></STIG_DATA>
    <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-500</ATTRIBUTE_DATA></STIG_DATA>
    <STATUS>NotAFinding</STATUS>
  </VULN>
</CHECKLIST>
""");

    var missingPath = Path.Combine(_tempDir, "missing.xml");
    var orchestrator = new VerifyOrchestrator();

    var consolidated = orchestrator.ParseAndMergeResults(new[] { validCkl, missingPath });

    consolidated.Results.Should().ContainSingle();
    consolidated.DiagnosticMessages.Should().Contain(d => d.Contains("Failed to parse", StringComparison.Ordinal));
    consolidated.DiagnosticMessages.Should().Contain(d => d.Contains("missing.xml", StringComparison.Ordinal));
  }

  [Fact]
  public void MergeReports_MergesEvidenceAndCommentsWithoutCaseDuplicatePaths()
  {
    var first = Report("SCAP",
      Result("V-700", VerifyStatus.Fail, "SCAP", "s.xml", " same comment ", new[] { "C:/Evidence/A.txt" }, new Dictionary<string, string> { ["b"] = "2" }));

    var second = Report("Evaluate-STIG",
      Result("V-700", VerifyStatus.Pass, "Evaluate-STIG", "e.xml", "same comment", new[] { "c:/evidence/a.txt", "C:/Evidence/B.txt" }, new Dictionary<string, string> { ["a"] = "1" }));

    var orchestrator = new VerifyOrchestrator();
    var consolidated = orchestrator.MergeReports(new[] { first, second }, Array.Empty<string>());
    var merged = consolidated.Results.Single();

    merged.EvidencePaths.Should().ContainInOrder("C:/Evidence/A.txt", "C:/Evidence/B.txt");
    merged.Comments.Should().Be("same comment");
    merged.Metadata.Keys.Should().ContainInOrder("a", "scap_b");
  }

  private static string BuildDeterministicSnapshot(ConsolidatedVerifyReport report)
  {
    var options = new JsonSerializerOptions
    {
      WriteIndented = false
    };

    var snapshot = new
    {
      SourceReports = report.SourceReports.Select(r => new { r.Tool, r.ToolVersion, r.ResultCount, r.SourcePath }),
      Results = report.Results.Select(r => new
      {
        r.ControlId,
        r.Status,
        r.Tool,
        r.Comments,
        Evidence = r.EvidencePaths,
        Metadata = r.Metadata
      }),
      Conflicts = report.Conflicts.Select(c => new
      {
        c.ControlId,
        c.ResolvedStatus,
        c.ResolutionReason,
        Items = c.ConflictingResults.Select(i => new { i.Tool, i.Status, i.VerifiedAt })
      }),
      Diagnostics = report.DiagnosticMessages
    };

    return JsonSerializer.Serialize(snapshot, options);
  }

  private static NormalizedVerifyReport Report(string tool, params NormalizedVerifyResult[] results)
  {
    return new NormalizedVerifyReport
    {
      Tool = tool,
      ToolVersion = "1.0",
      StartedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
      FinishedAt = DateTimeOffset.Parse("2026-02-01T00:30:00Z"),
      OutputRoot = "/tmp",
      Results = results
    };
  }

  private static NormalizedVerifyResult Result(
    string controlId,
    VerifyStatus status,
    string tool,
    string source,
    string? comments,
    IReadOnlyList<string>? evidence = null,
    IReadOnlyDictionary<string, string>? metadata = null)
  {
    return new NormalizedVerifyResult
    {
      ControlId = controlId,
      VulnId = controlId,
      RuleId = "SV-" + controlId,
      Status = status,
      Tool = tool,
      SourceFile = source,
      VerifiedAt = DateTimeOffset.Parse("2026-02-01T00:15:00Z"),
      Comments = comments,
      EvidencePaths = evidence ?? Array.Empty<string>(),
      Metadata = metadata ?? new Dictionary<string, string>()
    };
  }
}

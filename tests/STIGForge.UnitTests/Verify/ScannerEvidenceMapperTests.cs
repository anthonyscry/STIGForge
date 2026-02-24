using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public sealed class ScannerEvidenceMapperTests
{
  [Fact]
  public void Map_MapsFindingsByRuleIdAndRetainsUnmappedWithWarning()
  {
    var canonical = new List<LocalWorkflowChecklistItem>
    {
      new() { StigId = "xccdf_org.test.benchmark", RuleId = "SV-1000r1_rule" }
    };

    var findings = new List<ControlResult>
    {
      new() { RuleId = "SV-1000r1_rule", Tool = "Evaluate-STIG", SourceFile = "mapped.ckl" },
      new() { RuleId = "SV-9999r1_rule", Tool = "Evaluate-STIG", SourceFile = "unmapped.ckl" }
    };

    var mapper = new ScannerEvidenceMapper();

    var result = mapper.Map(canonical, findings);

    result.ScannerEvidence.Should().ContainSingle(e => e.RuleId == "SV-1000r1_rule");
    result.Unmapped.Should().ContainSingle(u => u.Source.Contains("SV-9999r1_rule", StringComparison.Ordinal));
    result.Diagnostics.Should().ContainSingle(d => d.Contains("Unmapped scanner finding", StringComparison.Ordinal));
  }
}

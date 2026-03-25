using System.Text.Json;
using FluentAssertions;
using STIGForge.Core;
using STIGForge.Core.Abstractions;
using STIGForge.Evidence;

namespace STIGForge.UnitTests.Evidence;

public class EvidenceCompilerTests : IDisposable
{
    private readonly string _bundleRoot;

    public EvidenceCompilerTests()
    {
        _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-ec-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_bundleRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_bundleRoot, true); } catch { }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void CreateEvidenceEntry(
        string bundleRoot,
        string controlKey,
        string evidenceId,
        string type,
        string artifactContent,
        string? stepName = null,
        string? ruleId = null)
    {
        var controlDir = Path.Combine(bundleRoot, "Evidence", "by_control", controlKey);
        Directory.CreateDirectory(controlDir);

        // Write evidence artifact
        var evidencePath = Path.Combine(controlDir, evidenceId + ".txt");
        File.WriteAllText(evidencePath, artifactContent);

        // Write metadata JSON
        var metadata = new EvidenceMetadata
        {
            ControlId = controlKey,
            RuleId = ruleId ?? ("RULE:" + controlKey),
            Title = "Evidence for " + controlKey,
            Type = type,
            Source = "TestSource",
            TimestampUtc = DateTimeOffset.UtcNow.ToString("o"),
            Host = "TestHost",
            User = "TestUser",
            BundleRoot = bundleRoot,
            Sha256 = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
            StepName = stepName
        };

        var metaPath = Path.Combine(controlDir, evidenceId + ".json");
        File.WriteAllText(metaPath, JsonSerializer.Serialize(metadata, JsonOptions.Indented));
    }

    private static EvidenceCompilationInput MakeInput(
        string? vulnId = "SV-001r1",
        string? ruleId = "SV-001r1_rule",
        string? status = "pass",
        string? tool = "Evaluate-STIG",
        DateTimeOffset? verifiedAt = null)
    {
        return new EvidenceCompilationInput(
            VulnId: vulnId,
            RuleId: ruleId,
            Status: status,
            Tool: tool,
            VerifiedAt: verifiedAt ?? new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero),
            FindingDetails: null,
            Comments: null);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CompileEvidence_WithNoEvidenceDir_ReturnsNull()
    {
        // No Evidence/by_control/ directory exists  -  index will be empty
        var compiler = new EvidenceCompiler();
        var input = MakeInput();

        var result = compiler.CompileEvidence(input, _bundleRoot);

        result.Should().BeNull();
    }

    [Fact]
    public void CompileEvidence_WithArtifacts_ReturnsPopulated()
    {
        CreateEvidenceEntry(
            bundleRoot: _bundleRoot,
            controlKey: "SV-001r1",
            evidenceId: "evidence_001",
            type: "Command",
            artifactContent: "registry value EnableLUA = 1",
            stepName: "apply_registry");

        var compiler = new EvidenceCompiler();
        var input = MakeInput(vulnId: "SV-001r1", ruleId: null);

        var result = compiler.CompileEvidence(input, _bundleRoot);

        result.Should().NotBeNull();
        result!.FindingDetails.Should().Contain("=== STIGForge Evidence Report ===");
        result.FindingDetails.Should().Contain("SV-001r1");
        result.FindingDetails.Should().Contain("registry value EnableLUA = 1");
        result.Comments.Should().Contain("Verified compliant");
    }

    [Fact]
    public void CompileEvidence_WithNullIds_ReturnsNull()
    {
        var compiler = new EvidenceCompiler();
        var input = MakeInput(vulnId: null, ruleId: null);

        var result = compiler.CompileEvidence(input, _bundleRoot);

        result.Should().BeNull();
    }

    [Fact]
    public void CompileEvidence_LargeArtifact_Truncates()
    {
        var largeContent = new string('A', 10_000);
        CreateEvidenceEntry(
            bundleRoot: _bundleRoot,
            controlKey: "SV-002r1",
            evidenceId: "evidence_large",
            type: "File",
            artifactContent: largeContent);

        var compiler = new EvidenceCompiler();
        var input = MakeInput(vulnId: "SV-002r1", ruleId: null);

        var result = compiler.CompileEvidence(input, _bundleRoot);

        result.Should().NotBeNull();
        result!.FindingDetails.Should().Contain("[truncated]");
        // Ensure original 10000-char run is not fully present
        result.FindingDetails!.Length.Should().BeLessThan(largeContent.Length);
    }

    [Fact]
    public void CompileEvidence_CachesIndexPerBundle()
    {
        CreateEvidenceEntry(
            bundleRoot: _bundleRoot,
            controlKey: "SV-003r1",
            evidenceId: "evidence_003",
            type: "Registry",
            artifactContent: "cached evidence content");

        var compiler = new EvidenceCompiler();
        var input = MakeInput(vulnId: "SV-003r1", ruleId: null);

        // First call  -  builds and caches index
        var result1 = compiler.CompileEvidence(input, _bundleRoot);
        // Second call to same bundleRoot  -  should use cached index
        var result2 = compiler.CompileEvidence(input, _bundleRoot);

        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1!.FindingDetails.Should().Contain("SV-003r1");
        result2!.FindingDetails.Should().Contain("SV-003r1");
    }

    [Fact]
    public void CompileEvidence_ProbesBothVulnIdAndRuleId()
    {
        // Create entry keyed by VulnId
        CreateEvidenceEntry(
            bundleRoot: _bundleRoot,
            controlKey: "SV-004r1",
            evidenceId: "evidence_vuln",
            type: "Command",
            artifactContent: "vuln key evidence");

        // Create entry keyed by RuleId
        CreateEvidenceEntry(
            bundleRoot: _bundleRoot,
            controlKey: "SV-004r1_rule",
            evidenceId: "evidence_rule",
            type: "Command",
            artifactContent: "rule key evidence");

        var compiler = new EvidenceCompiler();
        var input = MakeInput(vulnId: "SV-004r1", ruleId: "SV-004r1_rule");

        var result = compiler.CompileEvidence(input, _bundleRoot);

        result.Should().NotBeNull();
        result!.FindingDetails.Should().Contain("vuln key evidence");
        result.FindingDetails.Should().Contain("rule key evidence");
    }

    [Fact]
    public void CompileEvidence_DeduplicatesEntriesByEvidenceId()
    {
        // Create entry with controlKey matching both vulnId and ruleId probes
        // (controlKey == vulnId == ruleId so the same entry would be found twice)
        CreateEvidenceEntry(
            bundleRoot: _bundleRoot,
            controlKey: "SV-005r1",
            evidenceId: "evidence_dedup",
            type: "Command",
            artifactContent: "dedup evidence content");

        var compiler = new EvidenceCompiler();
        // Both VulnId and RuleId are the same value  -  should deduplicate
        var input = MakeInput(vulnId: "SV-005r1", ruleId: "SV-005r1");

        var result = compiler.CompileEvidence(input, _bundleRoot);

        result.Should().NotBeNull();
        // The artifact content should appear exactly once (not duplicated)
        var count = CountOccurrences(result!.FindingDetails!, "dedup evidence content");
        count.Should().Be(1);
    }

    [Fact]
    public void CompileEvidence_ApplyHistory_IncludesStepName()
    {
        CreateEvidenceEntry(
            bundleRoot: _bundleRoot,
            controlKey: "SV-006r1",
            evidenceId: "evidence_step",
            type: "Command",
            artifactContent: "step evidence content",
            stepName: "powerstig_compile");

        var compiler = new EvidenceCompiler();
        var input = MakeInput(vulnId: "SV-006r1", ruleId: null);

        var result = compiler.CompileEvidence(input, _bundleRoot);

        result.Should().NotBeNull();
        result!.FindingDetails.Should().Contain("--- Apply History ---");
        result.FindingDetails.Should().Contain("powerstig_compile");
    }

    [Fact]
    public void CompileEvidence_DifferentBundleRoots_CachedSeparately()
    {
        var bundleRoot2 = Path.Combine(Path.GetTempPath(), "stigforge-ec-test2-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(bundleRoot2);

        try
        {
            CreateEvidenceEntry(
                bundleRoot: _bundleRoot,
                controlKey: "SV-007r1",
                evidenceId: "evidence_b1",
                type: "Registry",
                artifactContent: "bundle1 evidence");

            CreateEvidenceEntry(
                bundleRoot: bundleRoot2,
                controlKey: "SV-007r1",
                evidenceId: "evidence_b2",
                type: "Registry",
                artifactContent: "bundle2 evidence");

            var compiler = new EvidenceCompiler();
            var input = MakeInput(vulnId: "SV-007r1", ruleId: null);

            var result1 = compiler.CompileEvidence(input, _bundleRoot);
            var result2 = compiler.CompileEvidence(input, bundleRoot2);

            result1.Should().NotBeNull();
            result2.Should().NotBeNull();
            result1!.FindingDetails.Should().Contain("bundle1 evidence");
            result2!.FindingDetails.Should().Contain("bundle2 evidence");
        }
        finally
        {
            try { Directory.Delete(bundleRoot2, true); } catch { }
        }
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }

        return count;
    }
}

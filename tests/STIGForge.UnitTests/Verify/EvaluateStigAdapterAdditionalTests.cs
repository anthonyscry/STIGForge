using FluentAssertions;
using STIGForge.Verify;
using STIGForge.Verify.Adapters;

namespace STIGForge.UnitTests.Verify;

public sealed class EvaluateStigAdapterAdditionalTests : IDisposable
{
    private readonly string _tempDir;

    public EvaluateStigAdapterAdditionalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-eval-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── CanHandle ─────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_NonExistentFile_ReturnsFalse()
    {
        var adapter = new EvaluateStigAdapter();
        adapter.CanHandle(Path.Combine(_tempDir, "does_not_exist.xml")).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NonXmlExtension_ReturnsFalse()
    {
        var path = WriteTempFile("results.ckl", "<STIGChecks></STIGChecks>");
        var adapter = new EvaluateStigAdapter();
        adapter.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_XmlWithSTIGChecksRoot_ReturnsTrue()
    {
        var path = WriteTempFile("checks.xml", "<STIGChecks Version=\"1.0\"></STIGChecks>");
        var adapter = new EvaluateStigAdapter();
        adapter.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_XmlWithSTIGCheckDescendants_ReturnsTrue()
    {
        var path = WriteTempFile("nested.xml", "<Root><STIGCheck /></Root>");
        var adapter = new EvaluateStigAdapter();
        adapter.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_XmlWithFindingDescendants_ReturnsTrue()
    {
        var path = WriteTempFile("findings.xml", "<Root><Finding /></Root>");
        var adapter = new EvaluateStigAdapter();
        adapter.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_XmlWithUnrecognizedStructure_ReturnsFalse()
    {
        var path = WriteTempFile("other.xml", "<SomeOtherRoot><Item /></SomeOtherRoot>");
        var adapter = new EvaluateStigAdapter();
        adapter.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_MalformedXml_ReturnsFalse()
    {
        var path = WriteTempFile("bad.xml", "not valid xml <<<<");
        var adapter = new EvaluateStigAdapter();
        adapter.CanHandle(path).Should().BeFalse();
    }

    // ── ParseResults errors ───────────────────────────────────────────────────

    [Fact]
    public void ParseResults_NonExistentFile_ThrowsFileNotFoundException()
    {
        var adapter = new EvaluateStigAdapter();
        var act = () => adapter.ParseResults(Path.Combine(_tempDir, "ghost.xml"));
        act.Should().Throw<FileNotFoundException>();
    }

    // ── Status mapping ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("NonCompliant", VerifyStatus.Fail)]
    [InlineData("Fail", VerifyStatus.Fail)]
    [InlineData("Open", VerifyStatus.Fail)]
    [InlineData("NotChecked", VerifyStatus.NotReviewed)]
    [InlineData("Informational", VerifyStatus.Informational)]
    [InlineData("Error", VerifyStatus.Error)]
    [InlineData("SomeUnknownStatus", VerifyStatus.Unknown)]
    [InlineData("", VerifyStatus.NotReviewed)]
    public void ParseResults_StatusVariants_MapsCorrectly(string statusText, VerifyStatus expectedStatus)
    {
        var xml = $"""
<STIGChecks>
  <STIGCheck VulnID="V-1" RuleID="SV-1" Status="{statusText}" />
</STIGChecks>
""";
        var path = WriteTempFile($"eval-{statusText.Replace(" ", "_")}.xml", xml);
        var adapter = new EvaluateStigAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.Status == expectedStatus);
    }

    // ── Severity normalization ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Cat I", "high")]
    [InlineData("CatI", "high")]
    [InlineData("High", "high")]
    [InlineData("Cat II", "medium")]
    [InlineData("CatII", "medium")]
    [InlineData("Medium", "medium")]
    [InlineData("Cat III", "low")]
    [InlineData("CatIII", "low")]
    [InlineData("Low", "low")]
    [InlineData("CustomSev", "CustomSev")]
    public void ParseResults_SeverityVariants_NormalizesCorrectly(string inputSeverity, string expectedSeverity)
    {
        var xml = $"""
<STIGChecks>
  <STIGCheck VulnID="V-1" RuleID="SV-1" Severity="{inputSeverity}" Result="Compliant" />
</STIGChecks>
""";
        var path = WriteTempFile($"eval-sev-{inputSeverity.Replace(" ", "_")}.xml", xml);
        var adapter = new EvaluateStigAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.Severity == expectedSeverity);
    }

    [Fact]
    public void ParseResults_NullSeverity_ReturnsNullSeverity()
    {
        var xml = "<STIGChecks><STIGCheck VulnID=\"V-1\" RuleID=\"SV-1\" Result=\"Compliant\" /></STIGChecks>";
        var path = WriteTempFile("eval-no-sev.xml", xml);
        var adapter = new EvaluateStigAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.Severity == null);
    }

    // ── Metadata extraction ───────────────────────────────────────────────────

    [Fact]
    public void ParseResults_WithTestId_IncludesTestIdInMetadata()
    {
        var xml = """
<STIGChecks>
  <STIGCheck VulnID="V-100" RuleID="SV-100" TestID="T-42" Result="Compliant" />
</STIGChecks>
""";
        var path = WriteTempFile("eval-meta.xml", xml);
        var adapter = new EvaluateStigAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r =>
            r.Metadata.ContainsKey("test_id") && r.Metadata["test_id"] == "T-42");
    }

    [Fact]
    public void ParseResults_WithCheckContentAndFixText_IncludesInMetadata()
    {
        var xml = """
<STIGChecks>
  <STIGCheck VulnID="V-200" RuleID="SV-200" Result="Compliant">
    <CheckContent>Verify this setting.</CheckContent>
    <FixText>Apply the fix.</FixText>
  </STIGCheck>
</STIGChecks>
""";
        var path = WriteTempFile("eval-checkfix.xml", xml);
        var adapter = new EvaluateStigAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r =>
            r.Metadata.ContainsKey("check_content") && r.Metadata.ContainsKey("fix_text"));
    }

    // ── ToolName property ─────────────────────────────────────────────────────

    [Fact]
    public void ToolName_Is_EvaluateStig()
    {
        var adapter = new EvaluateStigAdapter();
        adapter.ToolName.Should().Be("Evaluate-STIG");
    }

    // ── Summary statistics ─────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_WithAllStatusTypes_ComputesSummaryCorrectly()
    {
        var xml = """
<STIGChecks>
  <STIGCheck VulnID="V-1" RuleID="SV-1" Result="Compliant" />
  <STIGCheck VulnID="V-2" RuleID="SV-2" Status="NonCompliant" />
  <STIGCheck VulnID="V-3" RuleID="SV-3" Status="NotApplicable" />
  <STIGCheck VulnID="V-4" RuleID="SV-4" Status="NotReviewed" />
  <STIGCheck VulnID="V-5" RuleID="SV-5" Status="Informational" />
  <STIGCheck VulnID="V-6" RuleID="SV-6" Status="Error" />
</STIGChecks>
""";
        var path = WriteTempFile("eval-all-statuses.xml", xml);
        var adapter = new EvaluateStigAdapter();

        var report = adapter.ParseResults(path);
        var summary = report.Summary;

        summary.TotalCount.Should().Be(6);
        summary.PassCount.Should().Be(1);
        summary.FailCount.Should().Be(1);
        summary.NotApplicableCount.Should().Be(1);
        summary.NotReviewedCount.Should().Be(1);
        summary.InformationalCount.Should().Be(1);
        summary.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void ParseResults_CompliancePercent_CalculatedCorrectly()
    {
        var xml = """
<STIGChecks>
  <STIGCheck VulnID="V-1" RuleID="SV-1" Result="Compliant" />
  <STIGCheck VulnID="V-2" RuleID="SV-2" Status="NonCompliant" />
  <STIGCheck VulnID="V-3" RuleID="SV-3" Status="Error" />
</STIGChecks>
""";
        var path = WriteTempFile("eval-compliance.xml", xml);
        var adapter = new EvaluateStigAdapter();

        var report = adapter.ParseResults(path);

        // 1 pass / (1 pass + 1 fail + 1 error) = 33.33%
        report.Summary.CompliancePercent.Should().BeApproximately(33.33, 0.1);
    }

    // ── Timestamp parsing variants ────────────────────────────────────────────

    [Fact]
    public void ParseResults_WithNoTimestampAttributes_FallsBackToFileTimestamp()
    {
        var xml = "<STIGChecks><STIGCheck VulnID=\"V-1\" RuleID=\"SV-1\" Result=\"Compliant\" /></STIGChecks>";
        var path = WriteTempFile("eval-no-timestamps.xml", xml);
        var adapter = new EvaluateStigAdapter();

        var report = adapter.ParseResults(path);

        // StartedAt and FinishedAt should be close to now (file was just created)
        report.StartedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ── FindingDetails and Comments extraction ────────────────────────────────

    [Fact]
    public void ParseResults_WithFindingDetailsAndComments_IncludesInResult()
    {
        var xml = """
<STIGChecks>
  <STIGCheck VulnID="V-1" RuleID="SV-1" Status="Open">
    <FindingDetails>Found a problem here.</FindingDetails>
    <Comments>Reviewer note.</Comments>
  </STIGCheck>
</STIGChecks>
""";
        var path = WriteTempFile("eval-details.xml", xml);
        var adapter = new EvaluateStigAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r =>
            r.FindingDetails == "Found a problem here."
            && r.Comments == "Reviewer note.");
    }

    // ── Element-style attributes (not just XML attributes) ──────────────────

    [Fact]
    public void ParseResults_WithChildElementsForMetadata_ParsesCorrectly()
    {
        var xml = """
<STIGChecks>
  <STIGCheck>
    <VulnID>V-500</VulnID>
    <RuleID>SV-500</RuleID>
    <Status>Compliant</Status>
    <Version>3.0</Version>
  </STIGCheck>
</STIGChecks>
""";
        var path = WriteTempFile("eval-elements.xml", xml);
        var adapter = new EvaluateStigAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r =>
            r.VulnId == "V-500" && r.RuleId == "SV-500" && r.Status == VerifyStatus.Pass);
    }

    private string WriteTempFile(string fileName, string content)
    {
        var fullPath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}

using FluentAssertions;
using STIGForge.Verify;
using STIGForge.Verify.Adapters;

namespace STIGForge.UnitTests.Verify;

public sealed class ScapResultAdapterAdditionalTests : IDisposable
{
    private readonly string _tempDir;
    private static readonly string XccdfNs = "http://checklists.nist.gov/xccdf/1.2";

    public ScapResultAdapterAdditionalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-scap-adapter-" + Guid.NewGuid().ToString("N"));
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
        var adapter = new ScapResultAdapter();
        adapter.CanHandle(Path.Combine(_tempDir, "ghost.xml")).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NonXmlExtension_ReturnsFalse()
    {
        var path = WriteTempFile("results.arf", $"<Benchmark xmlns=\"{XccdfNs}\"></Benchmark>");
        var adapter = new ScapResultAdapter();
        adapter.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_XmlWithXccdfBenchmarkRoot_ReturnsTrue()
    {
        var path = WriteTempFile("bench.xml", $"<Benchmark xmlns=\"{XccdfNs}\"></Benchmark>");
        var adapter = new ScapResultAdapter();
        adapter.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_XmlWithXccdfTestResultRoot_ReturnsTrue()
    {
        var path = WriteTempFile("testresult.xml", $"<TestResult xmlns=\"{XccdfNs}\"></TestResult>");
        var adapter = new ScapResultAdapter();
        adapter.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_XmlWithNonXccdfNamespace_ReturnsFalse()
    {
        var path = WriteTempFile("wrong-ns.xml", "<Benchmark xmlns=\"http://other.namespace.example\"></Benchmark>");
        var adapter = new ScapResultAdapter();
        adapter.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_MalformedXml_ReturnsFalse()
    {
        var path = WriteTempFile("bad.xml", "not xml <<<");
        var adapter = new ScapResultAdapter();
        adapter.CanHandle(path).Should().BeFalse();
    }

    // ── ParseResults errors ───────────────────────────────────────────────────

    [Fact]
    public void ParseResults_NonExistentFile_ThrowsFileNotFoundException()
    {
        var adapter = new ScapResultAdapter();
        var act = () => adapter.ParseResults(Path.Combine(_tempDir, "missing.xml"));
        act.Should().Throw<FileNotFoundException>();
    }

    // ── Status mapping ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("fail", VerifyStatus.Fail)]
    [InlineData("open", VerifyStatus.Fail)]
    [InlineData("notchecked", VerifyStatus.NotReviewed)]
    [InlineData("notselected", VerifyStatus.NotReviewed)]
    [InlineData("notreviewed", VerifyStatus.NotReviewed)]
    [InlineData("informational", VerifyStatus.Informational)]
    [InlineData("error", VerifyStatus.Error)]
    [InlineData("unknown", VerifyStatus.Unknown)]
    [InlineData("something_else", VerifyStatus.Unknown)]
    [InlineData("", VerifyStatus.Unknown)]
    public void ParseResults_ScapStatusVariants_MapsCorrectly(string statusText, VerifyStatus expected)
    {
        var xml = BuildScapXml($"""
<rule-result idref="SV-1" weight="5.0">
  <result>{statusText}</result>
</rule-result>
""");
        var path = WriteTempFile($"scap-{statusText.Replace("_", "-")}.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.Status == expected);
    }

    [Theory]
    [InlineData("notafinding", VerifyStatus.Pass)]
    [InlineData("na", VerifyStatus.NotApplicable)]
    public void ParseResults_ScapPassAndNaAliases_MapsCorrectly(string statusText, VerifyStatus expected)
    {
        var xml = BuildScapXml($"""
<rule-result idref="SV-1">
  <result>{statusText}</result>
</rule-result>
""");
        var path = WriteTempFile($"scap-alias-{statusText}.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.Status == expected);
    }

    // ── Weight → severity mapping ─────────────────────────────────────────────

    [Theory]
    [InlineData("10.0", "high")]
    [InlineData("9.0", "high")]
    [InlineData("8.9", "medium")]
    [InlineData("4.0", "medium")]
    [InlineData("3.9", "low")]
    [InlineData("0.1", "low")]
    public void ParseResults_WeightToSeverity_MapsCorrectly(string weight, string expectedSeverity)
    {
        var xml = BuildScapXml($"""
<rule-result idref="SV-W" weight="{weight}">
  <result>pass</result>
</rule-result>
""");
        var path = WriteTempFile($"scap-weight-{weight.Replace(".", "_")}.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.Severity == expectedSeverity);
    }

    [Theory]
    [InlineData("0.0")]
    [InlineData("not_a_number")]
    public void ParseResults_InvalidOrZeroWeight_SeverityIsNull(string weight)
    {
        var xml = BuildScapXml($"""
<rule-result idref="SV-W" weight="{weight}">
  <result>pass</result>
</rule-result>
""");
        var path = WriteTempFile($"scap-badweight.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.Severity == null);
    }

    [Fact]
    public void ParseResults_MissingWeight_SeverityIsNull()
    {
        var xml = BuildScapXml("""
<rule-result idref="SV-W">
  <result>pass</result>
</rule-result>
""");
        var path = WriteTempFile("scap-no-weight.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.Severity == null);
    }

    // ── metadata extraction ───────────────────────────────────────────────────

    [Fact]
    public void ParseResults_WithCceIdent_IncludesCceInMetadata()
    {
        var xml = BuildScapXml("""
<rule-result idref="SV-500" weight="5.0">
  <result>pass</result>
  <ident system="http://cce.mitre.org">CCE-1234-5</ident>
</rule-result>
""");
        var path = WriteTempFile("scap-cce.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r =>
            r.Metadata.ContainsKey("cce_id") && r.Metadata["cce_id"] == "CCE-1234-5");
    }

    [Fact]
    public void ParseResults_WithCheckContentRef_IncludesHrefAndNameInMetadata()
    {
        var xml = BuildScapXml("""
<rule-result idref="SV-600" weight="5.0">
  <result>fail</result>
  <check system="urn:xccdf:check:oval:1">
    <check-content-ref href="benchmark.xml" name="oval:test:1" />
  </check>
</rule-result>
""");
        var path = WriteTempFile("scap-checkref.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r =>
            r.Metadata.ContainsKey("check_href")
            && r.Metadata.ContainsKey("check_name"));
    }

    [Fact]
    public void ParseResults_WithTimestampAttribute_UsesRuleTimestamp()
    {
        var xml = BuildScapXml("""
<rule-result idref="SV-700" weight="5.0" time="2026-03-15T08:00:00Z">
  <result>pass</result>
</rule-result>
""");
        var path = WriteTempFile("scap-timestamp.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r =>
            r.VerifiedAt == DateTimeOffset.Parse("2026-03-15T08:00:00Z"));
    }

    // ── Finding details from message elements ─────────────────────────────────

    [Fact]
    public void ParseResults_WithSingleMessage_FindingDetailsPopulated()
    {
        var xml = BuildScapXml("""
<rule-result idref="SV-800">
  <result>fail</result>
  <message>Setting is incorrect.</message>
</rule-result>
""");
        var path = WriteTempFile("scap-msg.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.FindingDetails == "Setting is incorrect.");
    }

    [Fact]
    public void ParseResults_WithMultipleMessages_FindingDetailsJoined()
    {
        var xml = BuildScapXml("""
<rule-result idref="SV-900">
  <result>fail</result>
  <message>First message.</message>
  <message>Second message.</message>
</rule-result>
""");
        var path = WriteTempFile("scap-multimsg.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r =>
            r.FindingDetails != null && r.FindingDetails.Contains("First message.") && r.FindingDetails.Contains("Second message."));
    }

    [Fact]
    public void ParseResults_WithNoMessages_FindingDetailsIsNull()
    {
        var xml = BuildScapXml("""
<rule-result idref="SV-1000">
  <result>pass</result>
</rule-result>
""");
        var path = WriteTempFile("scap-no-msg.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.FindingDetails == null);
    }

    // ── Summary statistics ────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_AllStatusTypes_SummaryCountsAreCorrect()
    {
        var xml = BuildScapXml("""
<rule-result idref="SV-1"><result>pass</result></rule-result>
<rule-result idref="SV-2"><result>fail</result></rule-result>
<rule-result idref="SV-3"><result>notapplicable</result></rule-result>
<rule-result idref="SV-4"><result>notchecked</result></rule-result>
<rule-result idref="SV-5"><result>informational</result></rule-result>
<rule-result idref="SV-6"><result>error</result></rule-result>
""");
        var path = WriteTempFile("scap-summary.xml", xml);
        var adapter = new ScapResultAdapter();

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
        var xml = BuildScapXml("""
<rule-result idref="SV-1"><result>pass</result></rule-result>
<rule-result idref="SV-2"><result>fail</result></rule-result>
""");
        var path = WriteTempFile("scap-compliance.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        // 1 pass / (1 pass + 1 fail) = 50%
        report.Summary.CompliancePercent.Should().BeApproximately(50.0, 0.01);
    }

    [Fact]
    public void ParseResults_NoPassOrFail_ComplianceIsZero()
    {
        var xml = BuildScapXml("""
<rule-result idref="SV-1"><result>notchecked</result></rule-result>
""");
        var path = WriteTempFile("scap-zero-compliance.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Summary.CompliancePercent.Should().Be(0.0);
    }

    // ── ToolName property ─────────────────────────────────────────────────────

    [Fact]
    public void ToolName_IsScap()
    {
        var adapter = new ScapResultAdapter();
        adapter.ToolName.Should().Be("SCAP");
    }

    // ── non-ident VulnId extraction ───────────────────────────────────────────

    [Fact]
    public void ParseResults_WithNonCceIdent_UsedAsVulnId()
    {
        var xml = BuildScapXml("""
<rule-result idref="SV-1100" weight="5.0">
  <result>pass</result>
  <ident system="urn:example:vuln">V-1100</ident>
</rule-result>
""");
        var path = WriteTempFile("scap-vulnid.xml", xml);
        var adapter = new ScapResultAdapter();

        var report = adapter.ParseResults(path);

        report.Results.Should().ContainSingle(r => r.VulnId == "V-1100");
    }

    private static string BuildScapXml(string ruleResults) =>
        $"""
<Benchmark xmlns="{XccdfNs}">
  <TestResult version="1.2">
    <start-time>2026-01-01T00:00:00Z</start-time>
    <end-time>2026-01-01T01:00:00Z</end-time>
    {ruleResults}
  </TestResult>
</Benchmark>
""";

    private string WriteTempFile(string fileName, string content)
    {
        var fullPath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}

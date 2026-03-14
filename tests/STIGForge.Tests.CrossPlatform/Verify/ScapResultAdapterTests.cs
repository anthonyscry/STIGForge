using FluentAssertions;
using STIGForge.Tests.CrossPlatform.Helpers;
using STIGForge.Verify;
using STIGForge.Verify.Adapters;

namespace STIGForge.Tests.CrossPlatform.Verify;

public sealed class ScapResultAdapterTests
{
    private const string XccdfNs = "http://checklists.nist.gov/xccdf/1.2";

    private readonly ScapResultAdapter _sut = new();

    // Minimal valid XCCDF Benchmark XML with one passing rule-result
    private static string BuildBenchmarkXml(string ruleResultsXml, string version = "1.0",
        string startTime = "2024-03-01T10:00:00Z", string endTime = "2024-03-01T11:00:00Z") => $"""
        <xccdf:Benchmark xmlns:xccdf="{XccdfNs}" id="xccdf_benchmark_1">
          <xccdf:TestResult id="xccdf_result_1" version="{version}">
            <xccdf:start-time>{startTime}</xccdf:start-time>
            <xccdf:end-time>{endTime}</xccdf:end-time>
            {ruleResultsXml}
          </xccdf:TestResult>
        </xccdf:Benchmark>
        """;

    private static string SingleRuleResult(string idref, string result, string weight = "10.0",
        string time = "2024-03-01T10:30:00Z") => $"""
        <xccdf:rule-result idref="{idref}" weight="{weight}" time="{time}">
          <xccdf:result>{result}</xccdf:result>
        </xccdf:rule-result>
        """;

    // ── CanHandle ──────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_FileDoesNotExist_ReturnsFalse()
    {
        _sut.CanHandle("/no/such/file.xml").Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NonXmlExtension_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var path = dir.File("results.ckl");
        File.WriteAllText(path, "<CHECKLIST/>");
        _sut.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_ValidBenchmarkXml_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var path = dir.File("scap.xml");
        File.WriteAllText(path, BuildBenchmarkXml(string.Empty));
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_StandaloneTestResultXml_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var path = dir.File("tr.xml");
        File.WriteAllText(path, $"""<xccdf:TestResult xmlns:xccdf="{XccdfNs}" id="r1"/>""");
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_MalformedXml_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var path = dir.File("bad.xml");
        File.WriteAllText(path, "<<not xml>>");
        _sut.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_XmlWithoutXccdfNamespace_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var path = dir.File("other.xml");
        File.WriteAllText(path, "<Benchmark><TestResult/></Benchmark>");
        _sut.CanHandle(path).Should().BeFalse();
    }

    // ── ParseResults – file-not-found / malformed ─────────────────────────────

    [Fact]
    public void ParseResults_FileNotFound_ThrowsFileNotFoundException()
    {
        var act = () => _sut.ParseResults("/does/not/exist.xml");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ParseResults_MalformedXml_ThrowsInvalidDataException()
    {
        using var dir = new TempDirectory();
        var path = dir.File("broken.xml");
        File.WriteAllText(path, "<<broken>>");
        var act = () => _sut.ParseResults(path);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*VERIFY-SCAP-XML-001*");
    }

    // ── ParseResults – no TestResult element ────────────────────────────────

    [Fact]
    public void ParseResults_NoTestResultElement_ReturnsEmptyReportWithDiagnostic()
    {
        using var dir = new TempDirectory();
        var path = dir.File("notr.xml");
        File.WriteAllText(path, $"""<xccdf:Benchmark xmlns:xccdf="{XccdfNs}"><xccdf:Group/></xccdf:Benchmark>""");

        var report = _sut.ParseResults(path);
        report.Results.Should().BeEmpty();
        report.DiagnosticMessages.Should().ContainMatch("*TestResult*");
    }

    // ── ParseResults – happy path ────────────────────────────────────────────

    [Fact]
    public void ParseResults_SinglePassingRule_ParsedCorrectly()
    {
        using var dir = new TempDirectory();
        var path = dir.File("pass.xml");
        File.WriteAllText(path, BuildBenchmarkXml(
            SingleRuleResult("xccdf_rule_V-220697", "pass", weight: "10.0"), version: "1.3"));

        var report = _sut.ParseResults(path);

        report.Tool.Should().Be("SCAP");
        report.ToolVersion.Should().Be("1.3");
        report.Results.Should().HaveCount(1);
        var r = report.Results[0];
        r.RuleId.Should().Be("xccdf_rule_V-220697");
        r.Status.Should().Be(VerifyStatus.Pass);
        r.Severity.Should().Be("high");   // weight 10.0 → high
    }

    [Fact]
    public void ParseResults_MultipleRuleResults_AllParsed()
    {
        using var dir = new TempDirectory();
        var path = dir.File("multi.xml");
        var rules = $"""
            {SingleRuleResult("xccdf_rule_V-1", "pass",           weight: "10.0")}
            {SingleRuleResult("xccdf_rule_V-2", "fail",           weight: "5.0")}
            {SingleRuleResult("xccdf_rule_V-3", "notapplicable",  weight: "1.0")}
            {SingleRuleResult("xccdf_rule_V-4", "notchecked",     weight: "0.0")}
            {SingleRuleResult("xccdf_rule_V-5", "informational",  weight: "3.0")}
            {SingleRuleResult("xccdf_rule_V-6", "error",          weight: "10.0")}
            """;
        File.WriteAllText(path, BuildBenchmarkXml(rules));

        var report = _sut.ParseResults(path);

        report.Results.Should().HaveCount(6);
        report.Summary.PassCount.Should().Be(1);
        report.Summary.FailCount.Should().Be(1);
        report.Summary.NotApplicableCount.Should().Be(1);
        report.Summary.NotReviewedCount.Should().Be(1);
        report.Summary.InformationalCount.Should().Be(1);
        report.Summary.ErrorCount.Should().Be(1);
        report.Summary.TotalCount.Should().Be(6);
    }

    // ── Status mapping ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("pass",          VerifyStatus.Pass)]
    [InlineData("notafinding",   VerifyStatus.Pass)]
    [InlineData("fail",          VerifyStatus.Fail)]
    [InlineData("open",          VerifyStatus.Fail)]
    [InlineData("notapplicable", VerifyStatus.NotApplicable)]
    [InlineData("na",            VerifyStatus.NotApplicable)]
    [InlineData("notchecked",    VerifyStatus.NotReviewed)]
    [InlineData("notselected",   VerifyStatus.NotReviewed)]
    [InlineData("notreviewed",   VerifyStatus.NotReviewed)]
    [InlineData("informational", VerifyStatus.Informational)]
    [InlineData("error",         VerifyStatus.Error)]
    [InlineData("unknown",       VerifyStatus.Unknown)]
    [InlineData("somethingelse", VerifyStatus.Unknown)]
    [InlineData("",              VerifyStatus.Unknown)]
    public void ParseResults_StatusMapping_IsCorrect(string rawStatus, VerifyStatus expected)
    {
        using var dir = new TempDirectory();
        var path = dir.File("s.xml");
        File.WriteAllText(path, BuildBenchmarkXml(SingleRuleResult("xccdf_rule_V-1", rawStatus)));

        var report = _sut.ParseResults(path);
        report.Results[0].Status.Should().Be(expected);
    }

    // ── Weight → Severity mapping ────────────────────────────────────────────

    [Theory]
    [InlineData("9.0",  "high")]
    [InlineData("10.0", "high")]
    [InlineData("4.0",  "medium")]
    [InlineData("8.9",  "medium")]
    [InlineData("0.5",  "low")]
    [InlineData("3.9",  "low")]
    [InlineData("0.0",  null)]
    [InlineData("",     null)]
    public void ParseResults_WeightToSeverityMapping_IsCorrect(string weight, string? expectedSeverity)
    {
        using var dir = new TempDirectory();
        var path = dir.File("w.xml");
        File.WriteAllText(path, BuildBenchmarkXml(SingleRuleResult("xccdf_rule_V-1", "pass", weight: weight)));

        var report = _sut.ParseResults(path);
        report.Results[0].Severity.Should().Be(expectedSeverity);
    }

    // ── Ident and check-content-ref ──────────────────────────────────────────

    [Fact]
    public void ParseResults_IdentElements_PopulatedInMetadata()
    {
        using var dir = new TempDirectory();
        var path = dir.File("ident.xml");
        File.WriteAllText(path, $"""
            <xccdf:Benchmark xmlns:xccdf="{XccdfNs}" id="b1">
              <xccdf:TestResult id="r1">
                <xccdf:rule-result idref="xccdf_rule_V-220697" weight="10.0">
                  <xccdf:result>pass</xccdf:result>
                  <xccdf:ident system="http://iase.disa.mil/cci">CCI-000001</xccdf:ident>
                  <xccdf:ident system="https://nvd.nist.gov/cce">CCE-12345-6</xccdf:ident>
                  <xccdf:check>
                    <xccdf:check-content-ref href="oval.xml" name="oval:check:1"/>
                  </xccdf:check>
                </xccdf:rule-result>
              </xccdf:TestResult>
            </xccdf:Benchmark>
            """);

        var report = _sut.ParseResults(path);
        var meta = report.Results[0].Metadata;
        meta.Should().ContainKey("cce_id").WhoseValue.Should().Be("CCE-12345-6");
        meta.Should().ContainKey("check_href").WhoseValue.Should().Be("oval.xml");
        meta.Should().ContainKey("check_name").WhoseValue.Should().Be("oval:check:1");
        meta.Should().ContainKey("rule_id").WhoseValue.Should().Be("xccdf_rule_V-220697");
    }

    // ── Message finding details ───────────────────────────────────────────────

    [Fact]
    public void ParseResults_MessageElements_JoinedIntoFindingDetails()
    {
        using var dir = new TempDirectory();
        var path = dir.File("msg.xml");
        File.WriteAllText(path, $"""
            <xccdf:Benchmark xmlns:xccdf="{XccdfNs}" id="b1">
              <xccdf:TestResult id="r1">
                <xccdf:rule-result idref="xccdf_rule_V-1" weight="5.0">
                  <xccdf:result>fail</xccdf:result>
                  <xccdf:message>First finding line.</xccdf:message>
                  <xccdf:message>Second finding line.</xccdf:message>
                </xccdf:rule-result>
              </xccdf:TestResult>
            </xccdf:Benchmark>
            """);

        var report = _sut.ParseResults(path);
        report.Results[0].FindingDetails.Should()
            .Contain("First finding line.")
            .And.Contain("Second finding line.");
    }

    // ── Timestamps ───────────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_StartAndEndTime_Parsed()
    {
        using var dir = new TempDirectory();
        var path = dir.File("ts.xml");
        File.WriteAllText(path, BuildBenchmarkXml(
            SingleRuleResult("xccdf_rule_V-1", "pass"),
            startTime: "2024-07-04T08:00:00Z",
            endTime: "2024-07-04T09:30:00Z"));

        var report = _sut.ParseResults(path);
        report.StartedAt.UtcDateTime.Hour.Should().Be(8);
        report.FinishedAt.UtcDateTime.Hour.Should().Be(9);
    }

    // ── Compliance percent ────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_CompliancePercent_CalculatedCorrectly()
    {
        using var dir = new TempDirectory();
        var path = dir.File("comp.xml");
        var rules = $"""
            {SingleRuleResult("xccdf_rule_V-1", "pass",  weight: "5.0")}
            {SingleRuleResult("xccdf_rule_V-2", "pass",  weight: "5.0")}
            {SingleRuleResult("xccdf_rule_V-3", "fail",  weight: "5.0")}
            {SingleRuleResult("xccdf_rule_V-4", "error", weight: "5.0")}
            """;
        File.WriteAllText(path, BuildBenchmarkXml(rules));

        var report = _sut.ParseResults(path);
        // 2 pass / (2 pass + 1 fail + 1 error) = 50%
        report.Summary.CompliancePercent.Should().BeApproximately(50.0, 0.01);
    }

    // ── ToolName ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToolName_IsScap()
    {
        _sut.ToolName.Should().Be("SCAP");
    }
}

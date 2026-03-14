using FluentAssertions;
using STIGForge.Tests.CrossPlatform.Helpers;
using STIGForge.Verify;
using STIGForge.Verify.Adapters;

namespace STIGForge.Tests.CrossPlatform.Verify;

public sealed class CklAdapterTests
{
    private readonly CklAdapter _sut = new();

    // ── XML builders ─────────────────────────────────────────────────────────

    private static string BuildVuln(string vulnNum, string ruleId, string ruleTitle,
        string severity, string status, string? finding = null, string? comments = null,
        string? severityOverride = null, string? severityJustification = null)
    {
        var overrideEl = severityOverride is null ? "" : $"<SEVERITY_OVERRIDE>{severityOverride}</SEVERITY_OVERRIDE>";
        var justEl = severityJustification is null ? "" : $"<SEVERITY_JUSTIFICATION>{severityJustification}</SEVERITY_JUSTIFICATION>";
        return $"""
            <VULN>
              <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>{vulnNum}</ATTRIBUTE_DATA></STIG_DATA>
              <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>{ruleId}</ATTRIBUTE_DATA></STIG_DATA>
              <STIG_DATA><VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>{ruleTitle}</ATTRIBUTE_DATA></STIG_DATA>
              <STIG_DATA><VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE><ATTRIBUTE_DATA>{severity}</ATTRIBUTE_DATA></STIG_DATA>
              <STATUS>{status}</STATUS>
              <FINDING_DETAILS>{finding ?? ""}</FINDING_DETAILS>
              <COMMENTS>{comments ?? ""}</COMMENTS>
              {overrideEl}
              {justEl}
            </VULN>
            """;
    }

    private static string BuildCklXml(string vulnsXml, string version = "3") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <CHECKLIST>
          <ASSET>
            <ROLE>None</ROLE>
            <HOST_NAME>TEST-HOST</HOST_NAME>
          </ASSET>
          <STIGS>
            <iSTIG>
              <STIG_INFO>
                <SI_DATA>
                  <SID_NAME>version</SID_NAME>
                  <SID_DATA>{version}</SID_DATA>
                </SI_DATA>
                <SI_DATA>
                  <SID_NAME>title</SID_NAME>
                  <SID_DATA>Sample STIG</SID_DATA>
                </SI_DATA>
              </STIG_INFO>
              {vulnsXml}
            </iSTIG>
          </STIGS>
        </CHECKLIST>
        """;

    // ── CanHandle ──────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_FileDoesNotExist_ReturnsFalse()
    {
        _sut.CanHandle("/no/such/file.ckl").Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NonCklExtension_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var path = dir.File("checklist.xml");
        File.WriteAllText(path, BuildCklXml(string.Empty));
        _sut.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_ValidCklFile_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var path = dir.File("checklist.ckl");
        File.WriteAllText(path, BuildCklXml(string.Empty));
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_CklExtensionWrongRoot_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var path = dir.File("wrong.ckl");
        File.WriteAllText(path, "<SOMETHING_ELSE/>");
        _sut.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_MalformedXml_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var path = dir.File("bad.ckl");
        File.WriteAllText(path, "<<not xml>>");
        _sut.CanHandle(path).Should().BeFalse();
    }

    // ── ParseResults – file-not-found / malformed ─────────────────────────────

    [Fact]
    public void ParseResults_FileNotFound_ThrowsFileNotFoundException()
    {
        var act = () => _sut.ParseResults("/does/not/exist.ckl");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ParseResults_MalformedXml_ThrowsInvalidDataException()
    {
        using var dir = new TempDirectory();
        var path = dir.File("bad.ckl");
        File.WriteAllText(path, "<<broken>>");
        var act = () => _sut.ParseResults(path);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*VERIFY-CKL-XML-001*");
    }

    // ── ParseResults – empty checklist ────────────────────────────────────────

    [Fact]
    public void ParseResults_EmptyChecklist_ReturnsReportWithZeroResults()
    {
        using var dir = new TempDirectory();
        var path = dir.File("empty.ckl");
        File.WriteAllText(path, BuildCklXml(string.Empty));

        var report = _sut.ParseResults(path);
        report.Tool.Should().Be("Manual CKL");
        report.Results.Should().BeEmpty();
        report.Summary.TotalCount.Should().Be(0);
    }

    // ── ParseResults – happy path ────────────────────────────────────────────

    [Fact]
    public void ParseResults_SinglePassingVuln_ParsedCorrectly()
    {
        using var dir = new TempDirectory();
        var path = dir.File("pass.ckl");
        File.WriteAllText(path, BuildCklXml(BuildVuln(
            "V-220697", "SV-220697r569187_rule", "Sample Rule Title",
            "high", "NotAFinding",
            finding: "No issues found.",
            comments: "Verified by scanner.")));

        var report = _sut.ParseResults(path);

        report.Results.Should().HaveCount(1);
        var r = report.Results[0];
        r.ControlId.Should().Be("V-220697");
        r.VulnId.Should().Be("V-220697");
        r.RuleId.Should().Be("SV-220697r569187_rule");
        r.Title.Should().Be("Sample Rule Title");
        r.Severity.Should().Be("high");
        r.Status.Should().Be(VerifyStatus.Pass);
        r.FindingDetails.Should().Be("No issues found.");
        r.Comments.Should().Be("Verified by scanner.");
        r.Tool.Should().Be("Manual CKL");
    }

    [Fact]
    public void ParseResults_MultipleVulns_AllParsed()
    {
        using var dir = new TempDirectory();
        var path = dir.File("multi.ckl");
        var vulns = string.Concat(
            BuildVuln("V-1", "SV-1r_rule", "Rule 1", "high",   "NotAFinding"),
            BuildVuln("V-2", "SV-2r_rule", "Rule 2", "medium", "Open",          finding: "Open issue"),
            BuildVuln("V-3", "SV-3r_rule", "Rule 3", "low",    "Not_Applicable"),
            BuildVuln("V-4", "SV-4r_rule", "Rule 4", "high",   "Not_Reviewed"),
            BuildVuln("V-5", "SV-5r_rule", "Rule 5", "medium", "informational"),
            BuildVuln("V-6", "SV-6r_rule", "Rule 6", "low",    "error")
        );
        File.WriteAllText(path, BuildCklXml(vulns));

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
    [InlineData("NotAFinding",   VerifyStatus.Pass)]
    [InlineData("pass",          VerifyStatus.Pass)]
    [InlineData("Open",          VerifyStatus.Fail)]
    [InlineData("fail",          VerifyStatus.Fail)]
    [InlineData("Not_Applicable",VerifyStatus.NotApplicable)]
    [InlineData("na",            VerifyStatus.NotApplicable)]
    [InlineData("Not_Reviewed",  VerifyStatus.NotReviewed)]
    [InlineData("notchecked",    VerifyStatus.NotReviewed)]
    [InlineData("informational", VerifyStatus.Informational)]
    [InlineData("error",         VerifyStatus.Error)]
    [InlineData("unknown",       VerifyStatus.Unknown)]
    [InlineData("something",     VerifyStatus.Unknown)]
    [InlineData("",              VerifyStatus.NotReviewed)]
    public void ParseResults_StatusMapping_IsCorrect(string rawStatus, VerifyStatus expected)
    {
        using var dir = new TempDirectory();
        var path = dir.File("status.ckl");
        File.WriteAllText(path, BuildCklXml(BuildVuln("V-1", "SV-1r_rule", "R1", "high", rawStatus)));

        var report = _sut.ParseResults(path);
        report.Results[0].Status.Should().Be(expected);
    }

    // ── Severity override ────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_SeverityOverride_TakesPrecedenceOverStigDataSeverity()
    {
        using var dir = new TempDirectory();
        var path = dir.File("override.ckl");
        File.WriteAllText(path, BuildCklXml(BuildVuln(
            "V-1", "SV-1r_rule", "R1", "high", "NotAFinding",
            severityOverride: "medium",
            severityJustification: "Risk accepted via risk acceptance document.")));

        var report = _sut.ParseResults(path);
        var r = report.Results[0];
        r.Severity.Should().Be("medium");
        r.Metadata.Should().ContainKey("severity_override").WhoseValue.Should().Be("medium");
        r.Metadata.Should().ContainKey("severity_justification").WhoseValue.Should().Contain("risk acceptance");
    }

    // ── STIG_INFO version ────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_StigInfoVersion_PopulatedInToolVersion()
    {
        using var dir = new TempDirectory();
        var path = dir.File("ver.ckl");
        File.WriteAllText(path, BuildCklXml(BuildVuln("V-1", "SV-1r_rule", "R1", "high", "NotAFinding"),
            version: "7"));

        var report = _sut.ParseResults(path);
        report.ToolVersion.Should().Be("7");
    }

    [Fact]
    public void ParseResults_NoStigInfo_ToolVersionFallsBackToUnknown()
    {
        using var dir = new TempDirectory();
        var path = dir.File("nosi.ckl");
        File.WriteAllText(path, """
            <CHECKLIST>
              <STIGS>
                <iSTIG>
                  <VULN>
                    <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-1</ATTRIBUTE_DATA></STIG_DATA>
                    <STATUS>NotAFinding</STATUS>
                    <FINDING_DETAILS></FINDING_DETAILS>
                    <COMMENTS></COMMENTS>
                  </VULN>
                </iSTIG>
              </STIGS>
            </CHECKLIST>
            """);

        var report = _sut.ParseResults(path);
        report.ToolVersion.Should().Be("unknown");
    }

    // ── Metadata – ckl_ prefix for all STIG_DATA ─────────────────────────────

    [Fact]
    public void ParseResults_AllStigDataDumpedToMetadata()
    {
        using var dir = new TempDirectory();
        var path = dir.File("meta.ckl");
        File.WriteAllText(path, BuildCklXml(BuildVuln(
            "V-220697", "SV-220697r569187_rule", "Rule Title", "high", "NotAFinding")));

        var report = _sut.ParseResults(path);
        var meta = report.Results[0].Metadata;
        meta.Should().ContainKey("ckl_Vuln_Num").WhoseValue.Should().Be("V-220697");
        meta.Should().ContainKey("ckl_Rule_ID");
        meta.Should().ContainKey("ckl_Severity");
    }

    // ── VulnTitle fallback ────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_VulnTitleFallback_UsedWhenRuleTitleMissing()
    {
        using var dir = new TempDirectory();
        var path = dir.File("vt.ckl");
        File.WriteAllText(path, """
            <CHECKLIST>
              <STIGS>
                <iSTIG>
                  <STIG_INFO/>
                  <VULN>
                    <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-900</ATTRIBUTE_DATA></STIG_DATA>
                    <STIG_DATA><VULN_ATTRIBUTE>Vuln_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Fallback Title</ATTRIBUTE_DATA></STIG_DATA>
                    <STIG_DATA><VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE><ATTRIBUTE_DATA>medium</ATTRIBUTE_DATA></STIG_DATA>
                    <STATUS>Open</STATUS>
                    <FINDING_DETAILS></FINDING_DETAILS>
                    <COMMENTS></COMMENTS>
                  </VULN>
                </iSTIG>
              </STIGS>
            </CHECKLIST>
            """);

        var report = _sut.ParseResults(path);
        report.Results[0].Title.Should().Be("Fallback Title");
    }

    // ── ControlId fallback to RuleId ─────────────────────────────────────────

    [Fact]
    public void ParseResults_NoVulnNum_ControlIdFallsBackToRuleId()
    {
        using var dir = new TempDirectory();
        var path = dir.File("noid.ckl");
        File.WriteAllText(path, """
            <CHECKLIST>
              <STIGS>
                <iSTIG>
                  <STIG_INFO/>
                  <VULN>
                    <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-rule_only</ATTRIBUTE_DATA></STIG_DATA>
                    <STATUS>NotAFinding</STATUS>
                    <FINDING_DETAILS></FINDING_DETAILS>
                    <COMMENTS></COMMENTS>
                  </VULN>
                </iSTIG>
              </STIGS>
            </CHECKLIST>
            """);

        var report = _sut.ParseResults(path);
        report.Results[0].ControlId.Should().Be("SV-rule_only");
    }

    // ── Compliance percent ────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_CompliancePercent_CalculatedCorrectly()
    {
        using var dir = new TempDirectory();
        var path = dir.File("comp.ckl");
        var vulns = string.Concat(
            BuildVuln("V-1", "SV-1r_rule", "R1", "high", "NotAFinding"),
            BuildVuln("V-2", "SV-2r_rule", "R2", "high", "NotAFinding"),
            BuildVuln("V-3", "SV-3r_rule", "R3", "high", "Open")
        );
        File.WriteAllText(path, BuildCklXml(vulns));

        var report = _sut.ParseResults(path);
        // 2 pass / (2 pass + 1 fail) ≈ 66.67%
        report.Summary.CompliancePercent.Should().BeApproximately(66.67, 0.1);
    }

    // ── ToolName ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToolName_IsManualCkl()
    {
        _sut.ToolName.Should().Be("Manual CKL");
    }

    // ── RawArtifactPath ───────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_RawArtifactPath_IsAbsolutePath()
    {
        using var dir = new TempDirectory();
        var path = dir.File("artifact.ckl");
        File.WriteAllText(path, BuildCklXml(BuildVuln("V-1", "SV-1r_rule", "R1", "high", "NotAFinding")));

        var report = _sut.ParseResults(path);
        report.RawArtifactPath.Should().Be(Path.GetFullPath(path));
        report.Results[0].RawArtifactPath.Should().Be(Path.GetFullPath(path));
    }
}

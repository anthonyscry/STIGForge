using FluentAssertions;
using STIGForge.Tests.CrossPlatform.Helpers;
using STIGForge.Verify;
using STIGForge.Verify.Adapters;

namespace STIGForge.Tests.CrossPlatform.Verify;

public sealed class EvaluateStigAdapterTests
{
    private readonly EvaluateStigAdapter _sut = new();

    // ── CanHandle ──────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_FileDoesNotExist_ReturnsFalse()
    {
        _sut.CanHandle("/nonexistent/path/file.xml").Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NonXmlExtension_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var path = dir.File("results.txt");
        File.WriteAllText(path, "<STIGChecks/>");
        _sut.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_XmlWithSTIGChecksRoot_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var path = dir.File("results.xml");
        File.WriteAllText(path, "<STIGChecks><STIGCheck VulnID=\"V-1\"/></STIGChecks>");
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_XmlWithFindingDescendant_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var path = dir.File("results.xml");
        File.WriteAllText(path, "<Results><Finding VulnID=\"V-1\"/></Results>");
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_MalformedXml_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var path = dir.File("bad.xml");
        File.WriteAllText(path, "<<not valid xml>>");
        _sut.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_XmlWithUnrecognizedStructure_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var path = dir.File("other.xml");
        File.WriteAllText(path, "<SomeOtherRoot><Item/></SomeOtherRoot>");
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
        var path = dir.File("bad.xml");
        File.WriteAllText(path, "<<broken xml>>");
        var act = () => _sut.ParseResults(path);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*VERIFY-EVAL-XML-001*");
    }

    // ── ParseResults – empty / no checks ────────────────────────────────────

    [Fact]
    public void ParseResults_EmptyRootNoChecks_ReturnsReportWithDiagnostic()
    {
        using var dir = new TempDirectory();
        var path = dir.File("empty.xml");
        File.WriteAllText(path, "<STIGChecks/>");
        var report = _sut.ParseResults(path);
        report.Tool.Should().Be("Evaluate-STIG");
        report.Results.Should().BeEmpty();
        report.DiagnosticMessages.Should().NotBeEmpty();
    }

    [Fact]
    public void ParseResults_VersionInAttribute_IsPopulated()
    {
        using var dir = new TempDirectory();
        var path = dir.File("ver.xml");
        File.WriteAllText(path, "<STIGChecks Version=\"2.4\"><STIGCheck VulnID=\"V-1\" Status=\"Pass\"/></STIGChecks>");
        var report = _sut.ParseResults(path);
        report.ToolVersion.Should().Be("2.4");
    }

    [Fact]
    public void ParseResults_VersionInChildElement_IsPopulated()
    {
        using var dir = new TempDirectory();
        var path = dir.File("ver.xml");
        File.WriteAllText(path,
            "<STIGChecks><Version>3.1</Version><STIGCheck VulnID=\"V-1\" Status=\"Pass\"/></STIGChecks>");
        var report = _sut.ParseResults(path);
        report.ToolVersion.Should().Be("3.1");
    }

    // ── ParseResults – happy path ────────────────────────────────────────────

    [Fact]
    public void ParseResults_SingleSTIGCheckAsAttributes_ParsesCorrectly()
    {
        using var dir = new TempDirectory();
        var path = dir.File("single.xml");
        File.WriteAllText(path, """
            <STIGChecks Version="2.0"
                        StartTime="2024-01-15T10:00:00Z"
                        EndTime="2024-01-15T10:30:00Z">
              <STIGCheck VulnID="V-220697"
                         RuleID="SV-220697r569187_rule"
                         Title="Sample rule"
                         Severity="high"
                         Status="Compliant"
                         VerifiedAt="2024-01-15T10:25:00Z"/>
            </STIGChecks>
            """);

        var report = _sut.ParseResults(path);

        report.Results.Should().HaveCount(1);
        var r = report.Results[0];
        r.ControlId.Should().Be("V-220697");
        r.VulnId.Should().Be("V-220697");
        r.RuleId.Should().Be("SV-220697r569187_rule");
        r.Title.Should().Be("Sample rule");
        r.Severity.Should().Be("high");
        r.Status.Should().Be(VerifyStatus.Pass);
        r.Tool.Should().Be("Evaluate-STIG");
    }

    [Fact]
    public void ParseResults_CheckElementsAsChildElements_ParsesCorrectly()
    {
        using var dir = new TempDirectory();
        var path = dir.File("child.xml");
        File.WriteAllText(path, """
            <STIGChecks>
              <STIGCheck>
                <VulnID>V-100001</VulnID>
                <RuleID>SV-100001r_rule</RuleID>
                <Title>Child element title</Title>
                <Severity>medium</Severity>
                <Status>NonCompliant</Status>
                <FindingDetails>This is noncompliant because...</FindingDetails>
                <Comments>Reviewer comment</Comments>
              </STIGCheck>
            </STIGChecks>
            """);

        var report = _sut.ParseResults(path);

        report.Results.Should().HaveCount(1);
        var r = report.Results[0];
        r.ControlId.Should().Be("V-100001");
        r.Status.Should().Be(VerifyStatus.Fail);
        r.FindingDetails.Should().Be("This is noncompliant because...");
        r.Comments.Should().Be("Reviewer comment");
    }

    [Fact]
    public void ParseResults_MultipleChecks_AllParsed()
    {
        using var dir = new TempDirectory();
        var path = dir.File("multi.xml");
        File.WriteAllText(path, """
            <STIGChecks>
              <STIGCheck VulnID="V-1" Status="Compliant"    Severity="high"/>
              <STIGCheck VulnID="V-2" Status="NonCompliant" Severity="medium"/>
              <STIGCheck VulnID="V-3" Status="NotApplicable"/>
              <STIGCheck VulnID="V-4" Status="NotReviewed"/>
              <STIGCheck VulnID="V-5" Status="Informational"/>
              <STIGCheck VulnID="V-6" Status="Error"/>
            </STIGChecks>
            """);

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

    [Fact]
    public void ParseResults_FindingDescendantsUsed_WhenNoSTIGCheck()
    {
        using var dir = new TempDirectory();
        var path = dir.File("finding.xml");
        File.WriteAllText(path, """
            <Results>
              <Finding ID="V-900" Result="Pass"/>
            </Results>
            """);

        var report = _sut.ParseResults(path);
        report.Results.Should().HaveCount(1);
        report.Results[0].Status.Should().Be(VerifyStatus.Pass);
    }

    // ── Status mapping ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("compliant",      VerifyStatus.Pass)]
    [InlineData("pass",           VerifyStatus.Pass)]
    [InlineData("NotAFinding",    VerifyStatus.Pass)]
    [InlineData("noncompliant",   VerifyStatus.Fail)]
    [InlineData("fail",           VerifyStatus.Fail)]
    [InlineData("open",           VerifyStatus.Fail)]
    [InlineData("notapplicable",  VerifyStatus.NotApplicable)]
    [InlineData("na",             VerifyStatus.NotApplicable)]
    [InlineData("notreviewed",    VerifyStatus.NotReviewed)]
    [InlineData("notchecked",     VerifyStatus.NotReviewed)]
    [InlineData("informational",  VerifyStatus.Informational)]
    [InlineData("error",          VerifyStatus.Error)]
    [InlineData("somethingweird", VerifyStatus.Unknown)]
    [InlineData("",               VerifyStatus.NotReviewed)]
    public void ParseResults_StatusMapping_IsCorrect(string rawStatus, VerifyStatus expected)
    {
        using var dir = new TempDirectory();
        var path = dir.File("status.xml");
        File.WriteAllText(path, $"""
            <STIGChecks>
              <STIGCheck VulnID="V-1" Status="{rawStatus}"/>
            </STIGChecks>
            """);

        var report = _sut.ParseResults(path);
        report.Results[0].Status.Should().Be(expected);
    }

    // ── Severity mapping ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("cat i",   "high")]
    [InlineData("cati",    "high")]
    [InlineData("high",    "high")]
    [InlineData("cat ii",  "medium")]
    [InlineData("catii",   "medium")]
    [InlineData("medium",  "medium")]
    [InlineData("cat iii", "low")]
    [InlineData("catiii",  "low")]
    [InlineData("low",     "low")]
    public void ParseResults_SeverityMapping_IsCorrect(string rawSeverity, string expected)
    {
        using var dir = new TempDirectory();
        var path = dir.File("sev.xml");
        File.WriteAllText(path, $"""
            <STIGChecks>
              <STIGCheck VulnID="V-1" Status="Pass" Severity="{rawSeverity}"/>
            </STIGChecks>
            """);

        var report = _sut.ParseResults(path);
        report.Results[0].Severity.Should().Be(expected);
    }

    // ── Metadata fields ──────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_MetadataFields_PopulatedCorrectly()
    {
        using var dir = new TempDirectory();
        var path = dir.File("meta.xml");
        File.WriteAllText(path, """
            <STIGChecks>
              <STIGCheck VulnID="V-1" Status="Pass" TestID="T-42">
                <CheckContent>Run the following command...</CheckContent>
                <FixText>Apply patch XYZ.</FixText>
              </STIGCheck>
            </STIGChecks>
            """);

        var report = _sut.ParseResults(path);
        var meta = report.Results[0].Metadata;
        meta.Should().ContainKey("test_id").WhoseValue.Should().Be("T-42");
        meta.Should().ContainKey("check_content").WhoseValue.Should().Contain("Run the following");
        meta.Should().ContainKey("fix_text").WhoseValue.Should().Contain("Apply patch");
    }

    // ── Timestamps ───────────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_StartAndEndTimeAttributes_Parsed()
    {
        using var dir = new TempDirectory();
        var path = dir.File("ts.xml");
        File.WriteAllText(path, """
            <STIGChecks StartTime="2024-06-01T08:00:00Z" EndTime="2024-06-01T09:00:00Z">
              <STIGCheck VulnID="V-1" Status="Pass"/>
            </STIGChecks>
            """);

        var report = _sut.ParseResults(path);
        report.StartedAt.UtcDateTime.Hour.Should().Be(8);
        report.FinishedAt.UtcDateTime.Hour.Should().Be(9);
    }

    // ── Summary compliance percent ────────────────────────────────────────────

    [Fact]
    public void ParseResults_CompliancePercent_CalculatedCorrectly()
    {
        using var dir = new TempDirectory();
        var path = dir.File("compliance.xml");
        File.WriteAllText(path, """
            <STIGChecks>
              <STIGCheck VulnID="V-1" Status="Pass"/>
              <STIGCheck VulnID="V-2" Status="Pass"/>
              <STIGCheck VulnID="V-3" Status="fail"/>
            </STIGChecks>
            """);

        var report = _sut.ParseResults(path);
        report.Summary.CompliancePercent.Should().BeApproximately(66.67, 0.1);
    }

    // ── ToolName ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToolName_IsEvaluateStig()
    {
        _sut.ToolName.Should().Be("Evaluate-STIG");
    }
}

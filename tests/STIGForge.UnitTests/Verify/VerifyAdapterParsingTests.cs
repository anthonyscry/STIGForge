using FluentAssertions;
using STIGForge.Verify;
using STIGForge.Verify.Adapters;

namespace STIGForge.UnitTests.Verify;

public sealed class VerifyAdapterParsingTests : IDisposable
{
  private readonly string _tempDir;

  public VerifyAdapterParsingTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-verify-adapters-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public void CklAdapter_ParseResults_MapsStatusVariants()
  {
    var filePath = WriteTempFile("sample.ckl", """
<CHECKLIST>
  <STIGS>
    <iSTIG>
      <STIG_INFO>
        <SI_DATA>
          <SID_NAME>version</SID_NAME>
          <SID_DATA>2.14</SID_DATA>
        </SI_DATA>
      </STIG_INFO>
      <VULN>
        <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-100</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-100</ATTRIBUTE_DATA></STIG_DATA>
        <STATUS>not_reviewed</STATUS>
      </VULN>
      <VULN>
        <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-101</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-101</ATTRIBUTE_DATA></STIG_DATA>
        <STATUS>OPEN</STATUS>
      </VULN>
      <VULN>
        <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-102</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-102</ATTRIBUTE_DATA></STIG_DATA>
        <STATUS>mystery</STATUS>
      </VULN>
    </iSTIG>
  </STIGS>
</CHECKLIST>
""");

    var adapter = new CklAdapter();
    var report = adapter.ParseResults(filePath);

    report.Results.Should().HaveCount(3);
    report.Results.Select(r => r.Status).Should().ContainInOrder(VerifyStatus.NotReviewed, VerifyStatus.Fail, VerifyStatus.Unknown);
    report.ToolVersion.Should().Be("2.14");
  }

  [Fact]
  public void EvaluateStigAdapter_ParseResults_ReturnsDiagnosticWhenNoChecks()
  {
    var filePath = WriteTempFile("evaluate-empty.xml", "<STIGChecks Version=\"1.0\"></STIGChecks>");

    var adapter = new EvaluateStigAdapter();
    var report = adapter.ParseResults(filePath);

    report.Results.Should().BeEmpty();
    report.DiagnosticMessages.Should().Contain(d => d.Contains("No check/finding elements", StringComparison.Ordinal));
  }

  [Fact]
  public void EvaluateStigAdapter_ParseResults_MapsStatusAndTimestamp()
  {
    var filePath = WriteTempFile("evaluate.xml", """
<STIGChecks Version="2.0" StartTime="2026-02-01T10:00:00Z" EndTime="2026-02-01T10:30:00Z">
  <STIGCheck VulnID="V-200" RuleID="SV-200" Status="Not_Applicable" VerifiedAt="2026-02-01T10:05:00Z" />
  <STIGCheck VulnID="V-201" RuleID="SV-201" Result="Compliant" />
</STIGChecks>
""");

    var adapter = new EvaluateStigAdapter();
    var report = adapter.ParseResults(filePath);

    report.Results.Should().HaveCount(2);
    report.Results[0].Status.Should().Be(VerifyStatus.NotApplicable);
    report.Results[0].VerifiedAt.Should().Be(DateTimeOffset.Parse("2026-02-01T10:05:00Z"));
    report.Results[1].Status.Should().Be(VerifyStatus.Pass);
  }

  [Fact]
  public void ScapResultAdapter_ParseResults_UsesRuleTimestampAndStatusNormalization()
  {
    var filePath = WriteTempFile("scap.xml", """
<Benchmark xmlns="http://checklists.nist.gov/xccdf/1.2">
  <TestResult version="1.1">
    <start-time>2026-02-01T12:00:00Z</start-time>
    <end-time>2026-02-01T12:30:00Z</end-time>
    <rule-result idref="SV-300" time="2026-02-01T12:05:00Z" weight="10.0">
      <result>not_selected</result>
      <ident system="urn:example:vuln">V-300</ident>
    </rule-result>
    <rule-result idref="SV-301" weight="1.0">
      <result>NOT_APPLICABLE</result>
      <ident system="urn:example:vuln">V-301</ident>
    </rule-result>
  </TestResult>
</Benchmark>
""");

    var adapter = new ScapResultAdapter();
    var report = adapter.ParseResults(filePath);

    report.Results.Should().HaveCount(2);
    report.Results[0].Status.Should().Be(VerifyStatus.NotReviewed);
    report.Results[0].VerifiedAt.Should().Be(DateTimeOffset.Parse("2026-02-01T12:05:00Z"));
    report.Results[0].Severity.Should().Be("high");
    report.Results[1].Status.Should().Be(VerifyStatus.NotApplicable);
    report.Results[1].Severity.Should().Be("low");
  }

  [Fact]
  public void ScapResultAdapter_ParseResults_WhenNoTestResult_ReturnsDiagnostic()
  {
    var filePath = WriteTempFile("scap-empty.xml", "<Benchmark xmlns=\"http://checklists.nist.gov/xccdf/1.2\"></Benchmark>");

    var adapter = new ScapResultAdapter();
    var report = adapter.ParseResults(filePath);

    report.Results.Should().BeEmpty();
    report.DiagnosticMessages.Should().Contain(d => d.Contains("No TestResult", StringComparison.Ordinal));
  }

  private string WriteTempFile(string fileName, string content)
  {
    var fullPath = Path.Combine(_tempDir, fileName);
    File.WriteAllText(fullPath, content);
    return fullPath;
  }
}

using FluentAssertions;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public sealed class CklImporterTests : IDisposable
{
  private readonly string _tempDir;

  public CklImporterTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-ckl-importer-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public void Import_WithValidChecklist_ParsesAssetAndVulnerabilities()
  {
    var path = WriteCkl("valid.ckl", BuildChecklistXml());
    var importer = new CklImporter();

    var result = importer.Import(path);

    result.FilePath.Should().Be(path);
    result.AssetName.Should().Be("ASSET-01");
    result.HostName.Should().Be("HOST-01");
    result.Findings.Should().HaveCount(4);
  }

  [Fact]
  public void Import_ParsesAssetMetadataFields()
  {
    var path = WriteCkl("asset.ckl", BuildChecklistXml());
    var importer = new CklImporter();

    var result = importer.Import(path);

    result.AssetName.Should().Be("ASSET-01");
    result.HostName.Should().Be("HOST-01");
    result.HostIp.Should().Be("10.10.10.10");
    result.HostMac.Should().Be("00-11-22-33-44-55");
    result.HostFqdn.Should().Be("host01.example.mil");
  }

  [Fact]
  public void Import_ParsesStigInfo()
  {
    var path = WriteCkl("stig.ckl", BuildChecklistXml());
    var importer = new CklImporter();

    var result = importer.Import(path);

    result.StigTitle.Should().Be("Windows 11 STIG");
    result.StigVersion.Should().Be("3");
    result.StigRelease.Should().Be("Release: 12 Benchmark Date: 2026-02-01");
  }

  [Fact]
  public void Import_ParsesVulnerabilityStatuses()
  {
    var path = WriteCkl("statuses.ckl", BuildChecklistXml());
    var importer = new CklImporter();

    var result = importer.Import(path);

    result.Findings.Select(f => f.Status).Should().Equal("NotAFinding", "Open", "Not_Applicable", "Not_Reviewed");
  }

  [Fact]
  public void Import_ParsesStigDataElements()
  {
    var path = WriteCkl("stig-data.ckl", BuildChecklistXml());
    var importer = new CklImporter();

    var result = importer.Import(path);
    var finding = result.Findings[0];

    finding.VulnId.Should().Be("V-10001");
    finding.RuleId.Should().Be("SV-10001r1_rule");
    finding.RuleTitle.Should().Be("Account lockout threshold");
    finding.Severity.Should().Be("high");
  }

  [Fact]
  public void ToControlResults_MapsStatusesToControlStatusValues()
  {
    var path = WriteCkl("map-status.ckl", BuildChecklistXml());
    var importer = new CklImporter();
    var checklist = importer.Import(path);

    var results = importer.ToControlResults(checklist);

    results.Select(r => r.Status).Should().Equal("Pass", "Fail", "NotApplicable", "NotReviewed");
    results.Select(r => r.VulnId).Should().Equal("V-10001", "V-10002", "V-10003", "V-10004");
  }

  [Fact]
  public void Import_WithMissingFile_ThrowsFileNotFoundException()
  {
    var importer = new CklImporter();
    var path = Path.Combine(_tempDir, "missing.ckl");

    var act = () => importer.Import(path);

    act.Should().Throw<FileNotFoundException>()
      .Which.FileName.Should().Be(path);
  }

  [Fact]
  public void Import_WithInvalidXml_ThrowsXmlException()
  {
    var path = WriteCkl("invalid.ckl", "<CHECKLIST><ASSET></CHECKLIST>");
    var importer = new CklImporter();

    var act = () => importer.Import(path);

    act.Should().Throw<System.Xml.XmlException>();
  }

  [Fact]
  public void Import_WithSeverityCodes_MapsToCanonicalSeverity()
  {
    var xml = BuildChecklistXml(
      BuildVuln("V-20001", "SV-20001r1_rule", "Cat I", "I", "Open"),
      BuildVuln("V-20002", "SV-20002r1_rule", "Cat II", "II", "Open"),
      BuildVuln("V-20003", "SV-20003r1_rule", "Cat III", "III", "Open"));
    var path = WriteCkl("severity.ckl", xml);
    var importer = new CklImporter();

    var result = importer.Import(path);

    result.Findings.Select(f => f.Severity).Should().Equal("high", "medium", "low");
  }

  private string WriteCkl(string fileName, string xml)
  {
    var path = Path.Combine(_tempDir, fileName);
    File.WriteAllText(path, xml);
    return path;
  }

  private static string BuildChecklistXml(params string[]? vulns)
  {
    var vulnXml = vulns is { Length: > 0 }
      ? string.Join(Environment.NewLine, vulns)
      : string.Join(
        Environment.NewLine,
        BuildVuln("V-10001", "SV-10001r1_rule", "Account lockout threshold", "high", "NotAFinding"),
        BuildVuln("V-10002", "SV-10002r1_rule", "Password complexity", "medium", "Open"),
        BuildVuln("V-10003", "SV-10003r1_rule", "Anonymous SID lookup", "low", "Not_Applicable"),
        BuildVuln("V-10004", "SV-10004r1_rule", "Legacy protocol disable", "unknown", "Not_Reviewed"));

    return $$"""
    <CHECKLIST>
      <ASSET>
        <ASSET_NAME>ASSET-01</ASSET_NAME>
        <HOST_NAME>HOST-01</HOST_NAME>
        <HOST_IP>10.10.10.10</HOST_IP>
        <HOST_MAC>00-11-22-33-44-55</HOST_MAC>
        <HOST_FQDN>host01.example.mil</HOST_FQDN>
      </ASSET>
      <STIGS>
        <iSTIG>
          <STIG_INFO>
            <title>Windows 11 STIG</title>
            <version>3</version>
            <releaseinfo>Release: 12 Benchmark Date: 2026-02-01</releaseinfo>
          </STIG_INFO>
          {{vulnXml}}
        </iSTIG>
      </STIGS>
    </CHECKLIST>
    """;
  }

  private static string BuildVuln(string vulnNum, string ruleVer, string ruleTitle, string severity, string status)
  {
    return $$"""
    <VULN>
      <STIG_DATA>
        <VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE>
        <ATTRIBUTE_DATA>{{vulnNum}}</ATTRIBUTE_DATA>
      </STIG_DATA>
      <STIG_DATA>
        <VULN_ATTRIBUTE>Rule_Ver</VULN_ATTRIBUTE>
        <ATTRIBUTE_DATA>{{ruleVer}}</ATTRIBUTE_DATA>
      </STIG_DATA>
      <STIG_DATA>
        <VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE>
        <ATTRIBUTE_DATA>{{ruleTitle}}</ATTRIBUTE_DATA>
      </STIG_DATA>
      <STIG_DATA>
        <VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE>
        <ATTRIBUTE_DATA>{{severity}}</ATTRIBUTE_DATA>
      </STIG_DATA>
      <STATUS>{{status}}</STATUS>
      <COMMENTS>Comment for {{vulnNum}}</COMMENTS>
      <FINDING_DETAILS>Details for {{vulnNum}}</FINDING_DETAILS>
    </VULN>
    """;
  }
}

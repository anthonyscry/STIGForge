using FluentAssertions;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public sealed class NessusImporterTests : IDisposable
{
  private readonly string _root;

  public NessusImporterTests()
  {
    _root = Path.Combine(Path.GetTempPath(), "stigforge-nessus-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose()
  {
    try { Directory.Delete(_root, true); } catch { }
  }

  [Fact]
  public void Import_ValidSingleHostSingleFinding_ParsesExpectedFields()
  {
    var file = WriteNessusFile(
      CreateHost("host-a", "10.0.0.11", "Windows 11",
        CreateReportItem(pluginId: "1001", pluginName: "Single Finding", severity: 2)));

    var sut = new NessusImporter();

    var findings = sut.Import(file);

    findings.Should().HaveCount(1);
    findings[0].PluginId.Should().Be("1001");
    findings[0].PluginName.Should().Be("Single Finding");
    findings[0].HostName.Should().Be("host-a");
    findings[0].Severity.Should().Be(2);
  }

  [Fact]
  public void Import_MultipleHostsAndFindings_ReturnsAllFindingsSortedBySeverity()
  {
    var file = WriteNessusFile(
      CreateHost("host-a", "10.0.0.12", "Windows 11",
        CreateReportItem(pluginId: "1002", pluginName: "Low", severity: 1),
        CreateReportItem(pluginId: "1003", pluginName: "Critical", severity: 4)),
      CreateHost("host-b", "10.0.0.13", "Windows Server",
        CreateReportItem(pluginId: "1004", pluginName: "Medium", severity: 2)));

    var sut = new NessusImporter();

    var findings = sut.Import(file);

    findings.Should().HaveCount(3);
    findings.Select(f => f.PluginId).Should().Contain(new[] { "1002", "1003", "1004" });
    findings[0].Severity.Should().Be(4);
    findings[1].Severity.Should().Be(2);
    findings[2].Severity.Should().Be(1);
  }

  [Fact]
  public void Import_DescriptionContainsRuleId_ExtractsStigRuleIdViaRegex()
  {
    var file = WriteNessusFile(
      CreateHost("host-a", "10.0.0.14", "Windows",
        CreateReportItem(
          pluginId: "1005",
          pluginName: "Rule extraction",
          severity: 2,
          description: "Finding maps to SV-55555r3_rule in the benchmark.")));

    var sut = new NessusImporter();

    var findings = sut.Import(file);

    findings.Should().ContainSingle();
    findings[0].StigRuleId.Should().Be("SV-55555r3_rule");
  }

  [Fact]
  public void Import_WithCveElements_ParsesCveList()
  {
    var file = WriteNessusFile(
      CreateHost("host-a", "10.0.0.15", "Windows",
        CreateReportItem(
          pluginId: "1006",
          pluginName: "CVE extraction",
          severity: 3,
          cves: ["CVE-2024-1000", "CVE-2024-2000"])));

    var sut = new NessusImporter();

    var findings = sut.Import(file);

    findings.Should().ContainSingle();
    findings[0].CveList.Should().Equal("CVE-2024-1000", "CVE-2024-2000");
  }

  [Fact]
  public void Import_SeverityLevelsZeroToFour_ParsesIntegerSeverityValues()
  {
    var file = WriteNessusFile(
      CreateHost("host-a", "10.0.0.16", "Windows",
        CreateReportItem(pluginId: "s0", pluginName: "Info", severity: 0),
        CreateReportItem(pluginId: "s1", pluginName: "Low", severity: 1),
        CreateReportItem(pluginId: "s2", pluginName: "Medium", severity: 2),
        CreateReportItem(pluginId: "s3", pluginName: "High", severity: 3),
        CreateReportItem(pluginId: "s4", pluginName: "Critical", severity: 4)));

    var sut = new NessusImporter();

    var findings = sut.Import(file);

    findings.Select(f => f.Severity).Should().Equal(4, 3, 2, 1, 0);
    findings.Select(f => f.SeverityName).Should().Equal("Critical", "High", "Medium", "Low", "Info");
  }

  [Fact]
  public void Import_WithHostProperties_ParsesHostIpAndOperatingSystem()
  {
    var file = WriteNessusFile(
      CreateHost("host-c", "192.168.56.20", "Windows Server 2022",
        CreateReportItem(pluginId: "1007", pluginName: "Host properties", severity: 1)));

    var sut = new NessusImporter();

    var findings = sut.Import(file);

    findings.Should().ContainSingle();
    findings[0].HostIp.Should().Be("192.168.56.20");
    findings[0].OperatingSystem.Should().Be("Windows Server 2022");
  }

  [Fact]
  public void Import_MissingFile_ThrowsFileNotFoundException()
  {
    var sut = new NessusImporter();
    var missing = Path.Combine(_root, "does-not-exist.nessus");

    Action act = () => sut.Import(missing);

    act.Should().Throw<FileNotFoundException>();
  }

  [Fact]
  public void Import_EmptyReport_ReturnsEmptyList()
  {
    var file = WriteNessusFile();
    var sut = new NessusImporter();

    var findings = sut.Import(file);

    findings.Should().BeEmpty();
  }

  private string WriteNessusFile(params string[] reportHosts)
  {
    var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".nessus");
    var xml = """
<?xml version="1.0" encoding="UTF-8"?>
<NessusClientData_v2>
  <Report name="ImporterTests">
__REPORT_HOSTS__
  </Report>
</NessusClientData_v2>
""";

    xml = xml.Replace("__REPORT_HOSTS__", string.Join(Environment.NewLine, reportHosts));
    File.WriteAllText(path, xml);
    return path;
  }

  private static string CreateHost(string hostName, string hostIp, string operatingSystem, params string[] reportItems)
  {
    var host = """
    <ReportHost name="__HOST_NAME__">
      <HostProperties>
        <tag name="host-ip">__HOST_IP__</tag>
        <tag name="operating-system">__OS__</tag>
      </HostProperties>
__REPORT_ITEMS__
    </ReportHost>
""";

    host = host
      .Replace("__HOST_NAME__", hostName)
      .Replace("__HOST_IP__", hostIp)
      .Replace("__OS__", operatingSystem)
      .Replace("__REPORT_ITEMS__", string.Join(Environment.NewLine, reportItems));

    return host;
  }

  private static string CreateReportItem(
    string pluginId,
    string pluginName,
    int severity,
    string? description = null,
    IReadOnlyList<string>? cves = null)
  {
    var cveXml = cves == null ? string.Empty : string.Join(string.Empty, cves.Select(cve => $"<cve>{cve}</cve>"));
    var safeDescription = description ?? pluginName;

    return $"""
      <ReportItem pluginID="{pluginId}" pluginName="{pluginName}" severity="{severity}" port="443" protocol="tcp" svc_name="https">
        <description>{safeDescription}</description>
        <solution>Apply vendor patch.</solution>
        <see_also>https://example.test/reference</see_also>
        {cveXml}
      </ReportItem>
""";
  }
}

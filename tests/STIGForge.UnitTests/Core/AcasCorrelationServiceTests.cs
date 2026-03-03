using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

#pragma warning disable xUnit1030

namespace STIGForge.UnitTests.Core;

public sealed class AcasCorrelationServiceTests : IDisposable
{
  private readonly string _root;

  public AcasCorrelationServiceTests()
  {
    _root = Path.Combine(Path.GetTempPath(), "stigforge-acas-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose()
  {
    try { Directory.Delete(_root, true); } catch { }
  }

  [Fact]
  public void Correlate_WithStigRuleIdMatch_CreatesStigRuleCorrelation()
  {
    var file = WriteNessusFile(
      CreateReportItem(pluginId: "10001", pluginName: "Rule ID finding", severity: 2, stigRuleId: "SV-10001r1_rule"));

    var controlRepo = new InMemoryControlRepository([
      MakeControl(controlId: "AC-1", title: "Rule ID control", ruleId: "SV-10001r1_rule")
    ]);

    var sut = new AcasCorrelationService(new NessusImporter(), controlRepo);

    var result = sut.Correlate(file, "bundle-a");

    result.TotalFindings.Should().Be(1);
    result.CorrelatedCount.Should().Be(1);
    result.UnmatchedCount.Should().Be(0);
    result.Correlations.Should().ContainSingle();
    result.Correlations[0].CorrelationType.Should().Be("StigRuleId");
    result.Correlations[0].Control.ControlId.Should().Be("AC-1");
  }

  [Fact]
  public void Correlate_WithTitleKeywordMatch_UsesFuzzyTitleCorrelation()
  {
    var file = WriteNessusFile(
      CreateReportItem(pluginId: "10002", pluginName: "Disable legacy protocol service", severity: 1));

    var controlRepo = new InMemoryControlRepository([
      MakeControl(controlId: "AC-2", title: "Windows should disable legacy protocol support")
    ]);

    var sut = new AcasCorrelationService(new NessusImporter(), controlRepo);

    var result = sut.Correlate(file, "bundle-a");

    result.CorrelatedCount.Should().Be(1);
    result.Correlations[0].CorrelationType.Should().Be("TitleMatch");
    result.Correlations[0].Control.ControlId.Should().Be("AC-2");
  }

  [Fact]
  public void Correlate_WithCveMatch_UsesCveCorrelation()
  {
    var file = WriteNessusFile(
      CreateReportItem(pluginId: "10003", pluginName: "Kernel vulnerability", severity: 2, cves: ["CVE-2024-12345"]));

    var controlRepo = new InMemoryControlRepository([
      MakeControl(controlId: "AC-3", title: "Kernel security", discussion: "Mitigates CVE-2024-12345")
    ]);

    var sut = new AcasCorrelationService(new NessusImporter(), controlRepo);

    var result = sut.Correlate(file, "bundle-a");

    result.CorrelatedCount.Should().Be(1);
    result.Correlations[0].CorrelationType.Should().Be("CveMatch");
    result.Correlations[0].Control.ControlId.Should().Be("AC-3");
  }

  [Fact]
  public void Correlate_WithoutAnyMatch_TracksUnmatchedFinding()
  {
    var file = WriteNessusFile(
      CreateReportItem(pluginId: "10004", pluginName: "Unmapped plugin", severity: 3));

    var controlRepo = new InMemoryControlRepository([
      MakeControl(controlId: "AC-4", title: "Completely unrelated control")
    ]);

    var sut = new AcasCorrelationService(new NessusImporter(), controlRepo);

    var result = sut.Correlate(file, "bundle-a");

    result.CorrelatedCount.Should().Be(0);
    result.UnmatchedCount.Should().Be(1);
    result.UnmatchedFindings.Should().ContainSingle();
    result.UnmatchedFindings[0].PluginId.Should().Be("10004");
  }

  [Fact]
  public void Correlate_WithMixedMatchesAndUnmatched_ReturnsExpectedCountsAndOrdering()
  {
    var file = WriteNessusFile(
      CreateReportItem(pluginId: "10005", pluginName: "Rule-based", severity: 1, stigRuleId: "SV-10005r1_rule"),
      CreateReportItem(pluginId: "10006", pluginName: "Disable legacy protocol service", severity: 4),
      CreateReportItem(pluginId: "10007", pluginName: "No map", severity: 3));

    var controlRepo = new InMemoryControlRepository([
      MakeControl(controlId: "AC-5", title: "Rule control", ruleId: "SV-10005r1_rule"),
      MakeControl(controlId: "AC-6", title: "Disable legacy protocol support")
    ]);

    var sut = new AcasCorrelationService(new NessusImporter(), controlRepo);

    var result = sut.Correlate(file, "bundle-a");

    result.TotalFindings.Should().Be(3);
    result.CorrelatedCount.Should().Be(2);
    result.UnmatchedCount.Should().Be(1);
    result.Correlations.Should().HaveCount(2);
    result.Correlations[0].Finding.Severity.Should().Be(4);
    result.UnmatchedFindings.Should().ContainSingle();
    result.UnmatchedFindings[0].PluginId.Should().Be("10007");
  }

  [Fact]
  public async Task CorrelateAsync_WithCancellationToken_ThrowsOperationCanceledException()
  {
    var file = WriteNessusFile(CreateReportItem(pluginId: "10008", pluginName: "Cancelable", severity: 1));
    var controlRepo = new InMemoryControlRepository([]);
    var sut = new AcasCorrelationService(new NessusImporter(), controlRepo);

    using var cts = new CancellationTokenSource();
    cts.Cancel();

    Func<Task> act = async () => await sut.CorrelateAsync(file, "bundle-a", cts.Token).ConfigureAwait(false);

    await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
  }

  [Fact]
  public void Correlate_WithHighManualControl_SetsAcasHighNotReviewedMismatch()
  {
    var file = WriteNessusFile(
      CreateReportItem(pluginId: "10009", pluginName: "Manual high", severity: 3, stigRuleId: "SV-10009r1_rule"));

    var controlRepo = new InMemoryControlRepository([
      MakeControl(controlId: "AC-9", title: "Manual review", ruleId: "SV-10009r1_rule", severity: "medium", isManual: true)
    ]);

    var sut = new AcasCorrelationService(new NessusImporter(), controlRepo);

    var result = sut.Correlate(file, "bundle-a");

    result.Correlations.Should().ContainSingle();
    result.Correlations[0].MismatchType.Should().Be("AcasHighNotReviewed");
  }

  [Fact]
  public void Correlate_WithHighAndLowControlSeverity_SetsAcasHighSeverityMismatch()
  {
    var file = WriteNessusFile(
      CreateReportItem(pluginId: "10010", pluginName: "Severity mismatch", severity: 4, stigRuleId: "SV-10010r1_rule"));

    var controlRepo = new InMemoryControlRepository([
      MakeControl(controlId: "AC-10", title: "Low control", ruleId: "SV-10010r1_rule", severity: "low", isManual: false)
    ]);

    var sut = new AcasCorrelationService(new NessusImporter(), controlRepo);

    var result = sut.Correlate(file, "bundle-a");

    result.Correlations.Should().ContainSingle();
    result.Correlations[0].MismatchType.Should().Be("AcasHighSeverityMismatch");
  }

  [Fact]
  public void Correlate_WithEmptyFindings_ReturnsEmptyCorrelationResult()
  {
    var file = WriteNessusFile();
    var controlRepo = new InMemoryControlRepository([
      MakeControl(controlId: "AC-11", title: "Unused control")
    ]);
    var sut = new AcasCorrelationService(new NessusImporter(), controlRepo);

    var result = sut.Correlate(file, "bundle-a");

    result.TotalFindings.Should().Be(0);
    result.CorrelatedCount.Should().Be(0);
    result.UnmatchedCount.Should().Be(0);
    result.Correlations.Should().BeEmpty();
    result.UnmatchedFindings.Should().BeEmpty();
  }

  private string WriteNessusFile(params string[] reportItems)
  {
    var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".nessus");
    var xml = """
<?xml version="1.0" encoding="UTF-8"?>
<NessusClientData_v2>
  <Report name="AcasTestReport">
    <ReportHost name="host-a">
      <HostProperties>
        <tag name="host-ip">10.0.0.10</tag>
        <tag name="operating-system">Windows</tag>
      </HostProperties>
__REPORT_ITEMS__
    </ReportHost>
  </Report>
</NessusClientData_v2>
""";

    xml = xml.Replace("__REPORT_ITEMS__", string.Join(Environment.NewLine, reportItems));
    File.WriteAllText(path, xml);
    return path;
  }

  private static string CreateReportItem(string pluginId, string pluginName, int severity, string? stigRuleId = null, IReadOnlyList<string>? cves = null)
  {
    var cveXml = cves == null ? string.Empty : string.Join(string.Empty, cves.Select(cve => $"<cve>{cve}</cve>"));
    var stigXml = string.IsNullOrWhiteSpace(stigRuleId) ? string.Empty : $"<stig_severity rule_id=\"{stigRuleId}\" version=\"V1R1\">high</stig_severity>";

    return $"""
      <ReportItem pluginID="{pluginId}" pluginName="{pluginName}" severity="{severity}" port="443" protocol="tcp" svc_name="https">
        <description>{pluginName}</description>
        {cveXml}
        {stigXml}
      </ReportItem>
""";
  }

  private static ControlRecord MakeControl(string controlId, string title, string? ruleId = null, string severity = "medium", bool isManual = false, string? discussion = null)
  {
    return new ControlRecord
    {
      ControlId = controlId,
      Title = title,
      Severity = severity,
      IsManual = isManual,
      Discussion = discussion,
      ExternalIds = new ExternalIds { RuleId = ruleId, VulnId = "V-" + controlId },
      Applicability = new Applicability { ClassificationScope = ScopeTag.Both, Confidence = Confidence.High },
      Revision = new RevisionInfo { PackName = "TestPack" }
    };
  }

  private sealed class InMemoryControlRepository : IControlRepository
  {
    private readonly IReadOnlyList<ControlRecord> _controls;

    public InMemoryControlRepository(IReadOnlyList<ControlRecord> controls)
    {
      _controls = controls;
    }

    public Task SaveControlsAsync(string packId, IReadOnlyList<ControlRecord> controls, CancellationToken ct)
    {
      return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ControlRecord>> ListControlsAsync(string packId, CancellationToken ct)
    {
      return Task.FromResult(_controls);
    }

    public Task<bool> VerifySchemaAsync(CancellationToken ct)
    {
      return Task.FromResult(true);
    }
  }
}

#pragma warning restore xUnit1030

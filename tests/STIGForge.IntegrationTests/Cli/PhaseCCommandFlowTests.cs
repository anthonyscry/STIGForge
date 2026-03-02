using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.IntegrationTests.Cli;

public sealed class PhaseCCommandFlowTests : IDisposable
{
  private readonly string _tempRoot;

  public PhaseCCommandFlowTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-phasec-cli-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempRoot, true); } catch { }
  }

  [Fact]
  public async Task DriftCheckAndHistory_ServiceFlow_ReturnsExpectedSnapshots()
  {
    var dbPath = Path.Combine(_tempRoot, "phasec-drift.db");
    var cs = "Data Source=" + dbPath;
    DbBootstrap.EnsureCreated(cs);

    var bundleRoot = CreateBundleRoot("bundle-drift");
    WriteVerifyResults(bundleRoot, new[]
    {
      new { ruleId = "SV-1001", status = "NotAFinding" },
      new { ruleId = "SV-1002", status = "Open" }
    });

    var commands = CreateCommandService(cs, new TestProcessRunner("<Rsop />"));

    var first = await commands.DriftCheckAsync(bundleRoot, autoRemediate: false, CancellationToken.None);
    first.DriftEvents.Should().HaveCount(2);
    first.DriftEvents.Should().OnlyContain(e => e.ChangeType == DriftChangeTypes.BaselineEstablished);

    WriteVerifyResults(bundleRoot, new[]
    {
      new { ruleId = "SV-1001", status = "Open" },
      new { ruleId = "SV-1002", status = "Open" }
    });

    var second = await commands.DriftCheckAsync(bundleRoot, autoRemediate: false, CancellationToken.None);
    second.DriftEvents.Should().ContainSingle(e =>
      e.RuleId == "SV-1001"
      && e.ChangeType == DriftChangeTypes.StateChanged
      && e.PreviousState == "Pass"
      && e.CurrentState == "Open");

    var history = await commands.DriftHistoryAsync(bundleRoot, "SV-1001", 10, CancellationToken.None);
    history.Should().NotBeEmpty();
    history.Should().OnlyContain(h => h.RuleId == "SV-1001");
  }

  [Fact]
  public async Task RollbackCreateListApply_ServiceFlow_RoundTripsSnapshot()
  {
    var dbPath = Path.Combine(_tempRoot, "phasec-rollback.db");
    var cs = "Data Source=" + dbPath;
    DbBootstrap.EnsureCreated(cs);

    var bundleRoot = CreateBundleRoot("bundle-rollback");
    var runner = new TestProcessRunner(
      """
<Rsop>
  <Policy path="HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\\EnableSmartScreen" value="0" gpoName="DomainBaseline" />
</Rsop>
""");

    var commands = CreateCommandService(cs, runner);

    var created = await commands.RollbackCreateAsync(bundleRoot, "pre-hardening", CancellationToken.None);
    created.SnapshotId.Should().NotBeNullOrWhiteSpace();
    created.RollbackScriptPath.Should().NotBeNullOrWhiteSpace();
    File.Exists(created.RollbackScriptPath).Should().BeTrue();

    var listed = await commands.RollbackListAsync(bundleRoot, 10, CancellationToken.None);
    listed.Should().ContainSingle(s => s.SnapshotId == created.SnapshotId);

    var apply = await commands.RollbackApplyAsync(created.SnapshotId, CancellationToken.None);
    apply.Success.Should().BeTrue();
    runner.Commands.Should().Contain(c =>
      string.Equals(c.FileName, "powershell.exe", StringComparison.OrdinalIgnoreCase)
      && c.Arguments.Contains("-File", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task GpoConflicts_ServiceFlow_DetectsValueOverrides()
  {
    var dbPath = Path.Combine(_tempRoot, "phasec-gpo.db");
    var cs = "Data Source=" + dbPath;
    DbBootstrap.EnsureCreated(cs);

    var bundleRoot = CreateBundleRoot("bundle-gpo");
    var controls = new[]
    {
      new ControlRecord
      {
        Title = @"Registry Policy: HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableSmartScreen = 1"
      }
    };
    var controlsPath = Path.Combine(bundleRoot, "Manifest", "pack_controls.json");
    File.WriteAllText(controlsPath, JsonSerializer.Serialize(controls));

    var runner = new TestProcessRunner(
      """
<Rsop>
  <Policy path="HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\\EnableSmartScreen" value="0" gpoName="DomainBaseline" />
</Rsop>
""");
    var commands = CreateCommandService(cs, runner);

    var conflicts = await commands.GpoConflictsAsync(bundleRoot, CancellationToken.None);
    conflicts.Should().ContainSingle();
    conflicts[0].ConflictType.Should().Be("ValueOverride");
    conflicts[0].GpoName.Should().Be("DomainBaseline");
    conflicts[0].LocalValue.Should().Be("1");
    conflicts[0].GpoValue.Should().Be("0");
  }

  [Fact]
  public async Task NessusAndAcasImport_ServiceFlow_ParsesAndCorrelatesFindings()
  {
    var dbPath = Path.Combine(_tempRoot, "phasec-acas.db");
    var cs = "Data Source=" + dbPath;
    DbBootstrap.EnsureCreated(cs);

    var bundleRoot = CreateBundleRoot("bundle-acas");
    var controls = new[]
    {
      new ControlRecord
      {
        ControlId = "AC-1",
        Title = "Remote Desktop is restricted",
        ExternalIds = new ExternalIds { RuleId = "SV-10001r1_rule", VulnId = "V-10001" },
        Applicability = new Applicability { ClassificationScope = ScopeTag.Both, Confidence = Confidence.High },
        Revision = new RevisionInfo()
      }
    };
    await new SqliteJsonControlRepository(cs).SaveControlsAsync(bundleRoot, controls, CancellationToken.None);

    var nessusPath = Path.Combine(_tempRoot, "sample.nessus");
    await File.WriteAllTextAsync(nessusPath,
      """
      <NessusClientData_v2>
        <Report name="sample">
          <ReportHost name="host1">
            <ReportItem pluginID="1001" pluginName="Remote Desktop is restricted" severity="3" port="0" protocol="tcp" svc_name="general">
              <description>Includes SV-10001r1_rule reference</description>
              <cve>CVE-2024-0001</cve>
            </ReportItem>
          </ReportHost>
        </Report>
      </NessusClientData_v2>
      """);

    var commands = CreateCommandService(cs, new TestProcessRunner("<Rsop />"));
    var findings = await commands.NessusImportAsync(nessusPath, CancellationToken.None);
    findings.Should().HaveCount(1);

    var correlated = await commands.AcasImportAsync(nessusPath, bundleRoot, CancellationToken.None);
    correlated.CorrelatedCount.Should().Be(1);
    correlated.UnmatchedCount.Should().Be(0);
  }

  [Fact]
  public async Task CklImportAndExport_ServiceFlow_RoundTripsFiles()
  {
    var dbPath = Path.Combine(_tempRoot, "phasec-ckl.db");
    var cs = "Data Source=" + dbPath;
    DbBootstrap.EnsureCreated(cs);

    var bundleRoot = CreateBundleRoot("bundle-ckl");
    var controlsPath = Path.Combine(bundleRoot, "Manifest", "pack_controls.json");
    await File.WriteAllTextAsync(controlsPath, JsonSerializer.Serialize(new[]
    {
      new ControlRecord
      {
        ControlId = "AC-10",
        Title = "Example control",
        ExternalIds = new ExternalIds { RuleId = "SV-AC-10", VulnId = "V-AC-10" },
        Applicability = new Applicability { ClassificationScope = ScopeTag.Both, Confidence = Confidence.High },
        Revision = new RevisionInfo()
      }
    }));

    var cklPath = Path.Combine(_tempRoot, "sample.ckl");
    await File.WriteAllTextAsync(cklPath,
      """
      <CHECKLIST>
        <ASSET>
          <ASSET_NAME>HOST1</ASSET_NAME>
          <HOST_NAME>HOST1</HOST_NAME>
        </ASSET>
        <STIGS>
          <iSTIG>
            <STIG_INFO>
              <title>Win11 STIG</title>
              <version>1</version>
              <releaseinfo>R1</releaseinfo>
            </STIG_INFO>
            <VULN>
              <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-AC-10</ATTRIBUTE_DATA></STIG_DATA>
              <STIG_DATA><VULN_ATTRIBUTE>Rule_Ver</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-AC-10</ATTRIBUTE_DATA></STIG_DATA>
              <STIG_DATA><VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Example control</ATTRIBUTE_DATA></STIG_DATA>
              <STIG_DATA><VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE><ATTRIBUTE_DATA>medium</ATTRIBUTE_DATA></STIG_DATA>
              <STATUS>Not_Reviewed</STATUS>
            </VULN>
          </iSTIG>
        </STIGS>
      </CHECKLIST>
      """);

    var commands = CreateCommandService(cs, new TestProcessRunner("<Rsop />"));
    var checklist = await commands.CklImportAsync(cklPath, CancellationToken.None);
    checklist.Findings.Should().NotBeEmpty();

    var outputPath = Path.Combine(_tempRoot, "exported.ckl");
    var exported = await commands.CklExportAsync(bundleRoot, outputPath, "HOST1", "Win11 STIG", CancellationToken.None);
    exported.Should().Be(outputPath);
    File.Exists(outputPath).Should().BeTrue();
  }

  private PhaseCCommandService CreateCommandService(string cs, IProcessRunner runner)
  {
    var driftRepo = new SqliteDriftRepository(cs);
    var rollbackRepo = new SqliteRollbackRepository(cs);
    var controlRepo = new SqliteJsonControlRepository(cs);
    var drift = new DriftDetectionService(driftRepo);
    var rollback = new RollbackService(rollbackRepo, runner);
    var gpo = new GpoConflictDetector(runner);
    var nessus = new NessusImporter();
    var acas = new AcasCorrelationService(nessus, controlRepo);
    var cklImporter = new CklImporter();
    var cklExporter = new CklExporter();
    var emass = new EmassPackageGenerator();
    return new PhaseCCommandService(drift, rollback, gpo, nessus, acas, cklImporter, cklExporter, emass);
  }

  private string CreateBundleRoot(string name)
  {
    var root = Path.Combine(_tempRoot, name);
    Directory.CreateDirectory(Path.Combine(root, "Manifest"));
    Directory.CreateDirectory(Path.Combine(root, "Manual"));
    Directory.CreateDirectory(Path.Combine(root, "Apply"));
    Directory.CreateDirectory(Path.Combine(root, "Verify", "run-1"));

    File.WriteAllText(Path.Combine(root, "Manifest", "manifest.json"), "{}");
    File.WriteAllText(Path.Combine(root, "Manifest", "pack_controls.json"), "[]");
    File.WriteAllText(Path.Combine(root, "Manifest", "overlays.json"), "[]");
    File.WriteAllText(Path.Combine(root, "Manual", "answers.json"), "{}");
    File.WriteAllText(Path.Combine(root, "Apply", "RunApply.ps1"), "Write-Output 'ok'");
    File.WriteAllText(Path.Combine(root, "Apply", "apply_run.json"), "{}");

    return root;
  }

  private static void WriteVerifyResults(string bundleRoot, object[] results)
  {
    var outputPath = Path.Combine(bundleRoot, "Verify", "run-1", "consolidated-results.json");
    var payload = JsonSerializer.Serialize(new { results });
    File.WriteAllText(outputPath, payload);
  }

  private sealed class TestProcessRunner : IProcessRunner
  {
    private readonly string _rsopXml;

    public TestProcessRunner(string rsopXml)
    {
      _rsopXml = rsopXml;
    }

    public List<(string FileName, string Arguments)> Commands { get; } = new();

    public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
      var fileName = startInfo.FileName ?? string.Empty;
      var arguments = startInfo.Arguments ?? string.Empty;
      Commands.Add((fileName, arguments));

      if (string.Equals(fileName, "gpresult.exe", StringComparison.OrdinalIgnoreCase)
          && arguments.StartsWith("/x ", StringComparison.OrdinalIgnoreCase))
      {
        var path = ExtractQuotedPath(arguments);
        if (!string.IsNullOrWhiteSpace(path))
          File.WriteAllText(path, _rsopXml, Encoding.UTF8);

        return Task.FromResult(new ProcessResult { ExitCode = 0, StandardOutput = "RSOP generated" });
      }

      if (string.Equals(fileName, "gpresult.exe", StringComparison.OrdinalIgnoreCase))
      {
        return Task.FromResult(new ProcessResult
        {
          ExitCode = 0,
          StandardOutput = "Applied Group Policy Objects"
        });
      }

      if (string.Equals(fileName, "powershell.exe", StringComparison.OrdinalIgnoreCase)
          && arguments.Contains("-EncodedCommand", StringComparison.OrdinalIgnoreCase))
      {
        var decodedScript = DecodeEncodedCommand(arguments);
        if (decodedScript.Contains("Get-CimInstance Win32_Service", StringComparison.Ordinal))
          return Task.FromResult(new ProcessResult { ExitCode = 0, StandardOutput = "Running|Auto" });

        return Task.FromResult(new ProcessResult { ExitCode = 0, StandardOutput = "1" });
      }

      return Task.FromResult(new ProcessResult { ExitCode = 0, StandardOutput = "rollback complete" });
    }

    public bool ExistsInPath(string fileName)
    {
      return true;
    }

    private static string ExtractQuotedPath(string arguments)
    {
      var firstQuote = arguments.IndexOf('"');
      if (firstQuote < 0)
        return string.Empty;

      var secondQuote = arguments.IndexOf('"', firstQuote + 1);
      if (secondQuote <= firstQuote)
        return string.Empty;

      return arguments.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
    }

    private static string DecodeEncodedCommand(string arguments)
    {
      const string marker = "-EncodedCommand ";
      var index = arguments.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
      if (index < 0)
        return string.Empty;

      var encoded = arguments.Substring(index + marker.Length).Trim();
      var bytes = Convert.FromBase64String(encoded);
      return Encoding.Unicode.GetString(bytes);
    }
  }
}

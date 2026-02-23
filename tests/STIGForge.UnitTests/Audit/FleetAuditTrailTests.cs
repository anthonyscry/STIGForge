using System.Text.Json;
using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Services;
using STIGForge.Export;
using STIGForge.Infrastructure.Storage;
using STIGForge.Infrastructure.System;

namespace STIGForge.UnitTests.Audit;

public sealed class FleetAuditTrailTests : IDisposable
{
  private readonly string _dbPath;
  private readonly AuditTrailService _audit;

  public FleetAuditTrailTests()
  {
    _dbPath = Path.Combine(Path.GetTempPath(), "stigforge_fleet_audit_" + Guid.NewGuid().ToString("N") + ".db");
    var connectionString = "Data Source=" + _dbPath;
    DbBootstrap.EnsureCreated(connectionString);
    _audit = new AuditTrailService(connectionString, new SystemClock());
  }

  public void Dispose()
  {
    try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
  }

  [Fact]
  public async Task FleetExecute_RecordsSummaryAuditEntry()
  {
    var svc = new FleetService(credentialStore: null, audit: _audit);
    var request = new FleetRequest
    {
      Targets = new List<FleetTarget>
      {
        new() { HostName = "host-a" },
        new() { HostName = "host-b" }
      },
      Operation = "apply",
      MaxConcurrency = 2,
      TimeoutSeconds = 5
    };

    // Will fail since no real hosts, but audit should still be recorded
    try { await svc.ExecuteAsync(request, CancellationToken.None); } catch { }

    var entries = await _audit.QueryAsync(new AuditQuery { Action = "fleet-apply", Limit = 10 }, CancellationToken.None);
    entries.Should().NotBeEmpty();
    entries.Should().Contain(e => e.Action == "fleet-apply");
  }

  [Fact]
  public async Task FleetExecute_RecordsPerHostEntries()
  {
    var svc = new FleetService(credentialStore: null, audit: _audit);
    var request = new FleetRequest
    {
      Targets = new List<FleetTarget>
      {
        new() { HostName = "host-x" },
        new() { HostName = "host-y" }
      },
      Operation = "verify",
      MaxConcurrency = 2,
      TimeoutSeconds = 5
    };

    try { await svc.ExecuteAsync(request, CancellationToken.None); } catch { }

    var entries = await _audit.QueryAsync(new AuditQuery { Action = "fleet-verify-host", Limit = 10 }, CancellationToken.None);
    entries.Should().NotBeEmpty();
    var hostNames = entries.Select(e => e.Target).ToList();
    hostNames.Should().Contain("host-x");
    hostNames.Should().Contain("host-y");
  }

  [Fact]
  public async Task FleetStatus_RecordsAuditEntry()
  {
    var svc = new FleetService(credentialStore: null, audit: _audit);
    var targets = new List<FleetTarget>
    {
      new() { HostName = "status-host" }
    };

    try { await svc.CheckStatusAsync(targets, CancellationToken.None); } catch { }

    var entries = await _audit.QueryAsync(new AuditQuery { Action = "fleet-status", Limit = 10 }, CancellationToken.None);
    entries.Should().NotBeEmpty();
    entries[0].Target.Should().Contain("status-host");
  }

  [Fact]
  public async Task ChainIntegrity_ValidAfterFleetEntries()
  {
    var svc = new FleetService(credentialStore: null, audit: _audit);

    // Record several fleet entries
    try
    {
      await svc.CheckStatusAsync(
        new List<FleetTarget> { new() { HostName = "chain-host" } },
        CancellationToken.None);
    }
    catch { }

    try
    {
      await svc.ExecuteAsync(new FleetRequest
      {
        Targets = new List<FleetTarget> { new() { HostName = "chain-host" } },
        Operation = "apply",
        MaxConcurrency = 1,
        TimeoutSeconds = 5
      }, CancellationToken.None);
    }
    catch { }

    var isValid = await _audit.VerifyIntegrityAsync(CancellationToken.None);
    isValid.Should().BeTrue();
  }

  [Fact]
  public async Task AttestationImport_RecordsAuditEntry()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "att_audit_" + Guid.NewGuid().ToString("N"));
    try
    {
      // Setup test package
      var attestDir = Path.Combine(tempDir, "05_Attestations");
      Directory.CreateDirectory(attestDir);

      var package = new
      {
        attestations = new[]
        {
          new { controlId = "V-1001", complianceStatus = "Pending", systemName = "Test", bundleId = "test" }
        },
        generatedAt = DateTimeOffset.Now,
        systemName = "Test"
      };
      File.WriteAllText(Path.Combine(attestDir, "attestations.json"),
        JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true }));

      // Setup CSV
      var csvLines = new[]
      {
        "Control ID,Attestor Name,Attestor Role,Attestation Date (YYYY-MM-DD),Compliance Status (Compliant/NonCompliant/PartiallyCompliant),Compliance Evidence,Known Limitations,Next Review Date (YYYY-MM-DD)",
        "V-1001,Alice,Admin,2026-02-20,Compliant,Evidence,None,2026-08-20"
      };
      var csvPath = Path.Combine(tempDir, "filled.csv");
      File.WriteAllLines(csvPath, csvLines);

      var result = AttestationImporter.ImportAttestations(tempDir, csvPath, _audit);
      result.Updated.Should().Be(1);

      var entries = await _audit.QueryAsync(new AuditQuery { Action = "import-attestations", Limit = 10 }, CancellationToken.None);
      entries.Should().NotBeEmpty();
      entries[0].Detail.Should().Contain("Updated=1");
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }
}

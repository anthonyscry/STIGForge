using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using STIGForge.Apply.Snapshot;

namespace STIGForge.UnitTests.Apply;

public class RollbackScriptGeneratorTests : IDisposable
{
  private readonly string _tempDir;
  private readonly RollbackScriptGenerator _generator;

  public RollbackScriptGeneratorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-rollback-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(_tempDir);
    _generator = new RollbackScriptGenerator(NullLogger<RollbackScriptGenerator>.Instance);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  private SnapshotResult CreateSnapshot()
  {
    var secPolicy = Path.Combine(_tempDir, "secpol.inf");
    var auditPolicy = Path.Combine(_tempDir, "auditpol.csv");
    File.WriteAllText(secPolicy, "[System Access]\nMinimumPasswordLength = 14");
    File.WriteAllText(auditPolicy, "Machine Name,Policy Target,Subcategory,Setting");

    return new SnapshotResult
    {
      SnapshotId = "snap-" + Guid.NewGuid().ToString("N")[..6],
      CreatedAt = DateTimeOffset.UtcNow,
      SecurityPolicyPath = secPolicy,
      AuditPolicyPath = auditPolicy
    };
  }

  [Fact]
  public void GenerateScript_ValidSnapshot_CreatesPs1File()
  {
    var snapshot = CreateSnapshot();

    var scriptPath = _generator.GenerateScript(snapshot);

    File.Exists(scriptPath).Should().BeTrue();
    scriptPath.Should().EndWith(".ps1");
  }

  [Fact]
  public void GenerateScript_ContainsSeceditCommand()
  {
    var snapshot = CreateSnapshot();

    var scriptPath = _generator.GenerateScript(snapshot);
    var content = File.ReadAllText(scriptPath);

    content.Should().Contain("secedit");
    content.Should().Contain(snapshot.SecurityPolicyPath);
  }

  [Fact]
  public void GenerateScript_ContainsAuditpolCommand()
  {
    var snapshot = CreateSnapshot();

    var scriptPath = _generator.GenerateScript(snapshot);
    var content = File.ReadAllText(scriptPath);

    content.Should().Contain("auditpol");
    content.Should().Contain(snapshot.AuditPolicyPath);
  }

  [Fact]
  public void GenerateScript_WithLgpoState_IncludesLgpoRestore()
  {
    var snapshot = CreateSnapshot();
    var lgpoPath = Path.Combine(_tempDir, "lgpo_state");
    File.WriteAllText(lgpoPath, "lgpo state data");
    snapshot.LgpoStatePath = lgpoPath;

    var scriptPath = _generator.GenerateScript(snapshot);
    var content = File.ReadAllText(scriptPath);

    content.Should().Contain("LGPO.exe");
    content.Should().Contain(lgpoPath);
  }

  [Fact]
  public void GenerateScript_NullSnapshot_Throws()
  {
    var act = () => _generator.GenerateScript(null!);
    act.Should().Throw<ArgumentNullException>();
  }
}

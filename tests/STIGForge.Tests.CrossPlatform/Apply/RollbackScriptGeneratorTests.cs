using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using STIGForge.Apply.Snapshot;

namespace STIGForge.Tests.CrossPlatform.Apply;

/// <summary>
/// Tests for <see cref="RollbackScriptGenerator"/>.
/// Uses real temp-file I/O to verify script content and path behavior.
/// </summary>
public sealed class RollbackScriptGeneratorTests : IDisposable
{
    private readonly RollbackScriptGenerator _sut = new(NullLogger<RollbackScriptGenerator>.Instance);
    private readonly List<string> _tempDirs = [];

    // ── helpers ──────────────────────────────────────────────────────────────

    private string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stigforge_rsg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>Creates a valid <see cref="SnapshotResult"/> with real temp files on disk.</summary>
    private SnapshotResult MakeValidSnapshot(
        string? dir = null,
        string? snapshotId = null,
        string? lgpoStatePath = null)
    {
        dir ??= MakeTempDir();
        var secPol = Path.Combine(dir, "secedit.inf");
        var auditPol = Path.Combine(dir, "auditpol.csv");
        File.WriteAllText(secPol, "[Unicode]\r\nUnicode=yes");
        File.WriteAllText(auditPol, "Machine Name,Policy Target,Subcategory,GUID,Inclusion Setting");

        return new SnapshotResult
        {
            SnapshotId = snapshotId ?? "snap-001",
            CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            SecurityPolicyPath = secPol,
            AuditPolicyPath = auditPol,
            LgpoStatePath = lgpoStatePath
        };
    }

    // ── ArgumentNullException / ArgumentException ─────────────────────────────

    [Fact]
    public void GenerateScript_NullSnapshot_Throws()
    {
        var act = () => _sut.GenerateScript(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GenerateScript_EmptySnapshotId_Throws()
    {
        var snap = MakeValidSnapshot();
        snap.SnapshotId = string.Empty;

        var act = () => _sut.GenerateScript(snap);
        act.Should().Throw<ArgumentException>().WithMessage("*SnapshotId*");
    }

    [Fact]
    public void GenerateScript_EmptySecurityPolicyPath_Throws()
    {
        var snap = MakeValidSnapshot();
        snap.SecurityPolicyPath = string.Empty;

        var act = () => _sut.GenerateScript(snap);
        act.Should().Throw<ArgumentException>().WithMessage("*SecurityPolicyPath*");
    }

    [Fact]
    public void GenerateScript_EmptyAuditPolicyPath_Throws()
    {
        var snap = MakeValidSnapshot();
        snap.AuditPolicyPath = string.Empty;

        var act = () => _sut.GenerateScript(snap);
        act.Should().Throw<ArgumentException>().WithMessage("*AuditPolicyPath*");
    }

    // ── FileNotFoundException ────────────────────────────────────────────────

    [Fact]
    public void GenerateScript_MissingSecurityFile_ThrowsFileNotFound()
    {
        var snap = MakeValidSnapshot();
        snap.SecurityPolicyPath = Path.Combine(MakeTempDir(), "does_not_exist.inf");

        var act = () => _sut.GenerateScript(snap);
        act.Should().Throw<FileNotFoundException>();
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void GenerateScript_ValidSnapshot_WritesPs1File()
    {
        var snap = MakeValidSnapshot();

        var path = _sut.GenerateScript(snap);

        File.Exists(path).Should().BeTrue();
        path.Should().EndWith(".ps1");
    }

    [Fact]
    public void GenerateScript_ValidSnapshot_ReturnsCorrectPath()
    {
        var snap = MakeValidSnapshot(snapshotId: "snap-xyz");

        var path = _sut.GenerateScript(snap);

        var expectedDir = Path.GetDirectoryName(snap.SecurityPolicyPath)!;
        path.Should().StartWith(expectedDir);
        path.Should().Contain("snap-xyz");
    }

    // ── Escaping ─────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateScript_SecurityPolicyPath_WithSingleQuote_IsEscapedInScript()
    {
        var dir = MakeTempDir();
        var secPol = Path.Combine(dir, "sec'pol.inf");
        var auditPol = Path.Combine(dir, "auditpol.csv");
        File.WriteAllText(secPol, "[Unicode]");
        File.WriteAllText(auditPol, "header");

        var snap = new SnapshotResult
        {
            SnapshotId = "esc-test",
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityPolicyPath = secPol,
            AuditPolicyPath = auditPol
        };

        var scriptPath = _sut.GenerateScript(snap);
        var content = File.ReadAllText(scriptPath);

        // Single quote must be doubled: sec''pol.inf
        content.Should().Contain("sec''pol.inf");
    }

    [Fact]
    public void GenerateScript_AuditPolicyPath_WithSingleQuote_IsEscapedInScript()
    {
        var dir = MakeTempDir();
        var secPol = Path.Combine(dir, "secedit.inf");
        var auditPol = Path.Combine(dir, "audit'pol.csv");
        File.WriteAllText(secPol, "[Unicode]");
        File.WriteAllText(auditPol, "header");

        var snap = new SnapshotResult
        {
            SnapshotId = "esc-audit",
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityPolicyPath = secPol,
            AuditPolicyPath = auditPol
        };

        var scriptPath = _sut.GenerateScript(snap);
        var content = File.ReadAllText(scriptPath);

        content.Should().Contain("audit''pol.csv");
    }

    // ── LGPO section ─────────────────────────────────────────────────────────

    [Fact]
    public void GenerateScript_LgpoPath_PresentAndExists_IsIncludedInScript()
    {
        var dir = MakeTempDir();
        var lgpoPath = Path.Combine(dir, "lgpo.zip");
        File.WriteAllText(lgpoPath, "lgpo data");

        var snap = MakeValidSnapshot(dir: dir, lgpoStatePath: lgpoPath);

        var scriptPath = _sut.GenerateScript(snap);
        var content = File.ReadAllText(scriptPath);

        content.Should().Contain("LGPO.exe");
        content.Should().Contain("lgpo.zip");
    }

    [Fact]
    public void GenerateScript_LgpoPath_PresentButMissing_IsNotIncludedInScript()
    {
        var dir = MakeTempDir();
        var missingLgpo = Path.Combine(dir, "missing_lgpo.zip");
        // Intentionally NOT creating the file

        var snap = MakeValidSnapshot(dir: dir, lgpoStatePath: missingLgpo);

        var scriptPath = _sut.GenerateScript(snap);
        var content = File.ReadAllText(scriptPath);

        content.Should().NotContain("LGPO.exe");
    }

    [Fact]
    public void GenerateScript_LgpoPath_Null_IsNotIncludedInScript()
    {
        var snap = MakeValidSnapshot(lgpoStatePath: null);

        var scriptPath = _sut.GenerateScript(snap);
        var content = File.ReadAllText(scriptPath);

        content.Should().NotContain("LGPO.exe");
    }

    // ── Script content ────────────────────────────────────────────────────────

    [Fact]
    public void GenerateScript_Script_ContainsSnapshotId()
    {
        var snap = MakeValidSnapshot(snapshotId: "snap-id-check-123");

        var scriptPath = _sut.GenerateScript(snap);
        var content = File.ReadAllText(scriptPath);

        content.Should().Contain("snap-id-check-123");
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best effort */ }
        }
    }
}

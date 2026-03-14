using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Moq;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class ImportInboxScannerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly Mock<IHashingService> _hashSvc = new();
    private readonly ImportInboxScanner _sut;

    public ImportInboxScannerTests()
    {
        _hashSvc.Setup(h => h.Sha256FileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        _sut = new ImportInboxScanner(_hashSvc.Object);
    }

    public void Dispose() => _temp.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string CreateZip(string folder, string zipName, IEnumerable<(string name, string content)> entries)
    {
        var zipPath = System.IO.Path.Combine(folder, zipName);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
        return zipPath;
    }

    private static string CreateEmptyZip(string folder, string name)
    {
        var path = System.IO.Path.Combine(folder, name);
        using var _ = ZipFile.Open(path, ZipArchiveMode.Create);
        return path;
    }

    // ── ScanAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_EmptyFolder_ReturnsNoCandidatesAndNoWarnings()
    {
        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_NullPath_ReturnsWarning()
    {
        var result = await _sut.ScanAsync(null!, CancellationToken.None);

        result.Candidates.Should().BeEmpty();
        result.Warnings.Should().ContainSingle()
              .Which.Should().Contain("Import folder not found");
    }

    [Fact]
    public async Task ScanAsync_WhitespacePath_ReturnsWarning()
    {
        var result = await _sut.ScanAsync("   ", CancellationToken.None);

        result.Candidates.Should().BeEmpty();
        result.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task ScanAsync_NonExistentFolder_ReturnsWarning()
    {
        var result = await _sut.ScanAsync(System.IO.Path.Combine(_temp.Path, "does-not-exist"), CancellationToken.None);

        result.Candidates.Should().BeEmpty();
        result.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task ScanAsync_ZipWithEvaluateStigScript_DetectsEvaluateStigTool()
    {
        CreateZip(_temp.Path, "evaluate-stig.zip",
            [("Evaluate-STIG.ps1", "# evaluate-stig script")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().ContainSingle()
              .Which.Should().Match<ImportInboxCandidate>(c =>
                  c.ArtifactKind == ImportArtifactKind.Tool &&
                  c.ToolKind == ToolArtifactKind.EvaluateStig &&
                  c.Confidence == DetectionConfidence.High);
    }

    [Fact]
    public async Task ScanAsync_ZipWithSccExecutable_DetectsSccTool()
    {
        CreateZip(_temp.Path, "scc-tool.zip",
            [("tools/cscc.exe", "binary")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().Contain(c =>
            c.ArtifactKind == ImportArtifactKind.Tool &&
            c.ToolKind == ToolArtifactKind.Scc);
    }

    [Fact]
    public async Task ScanAsync_ZipWithSccExeAtRoot_DetectsSccTool()
    {
        CreateZip(_temp.Path, "scc-root.zip",
            [("scc.exe", "binary")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().Contain(c =>
            c.ArtifactKind == ImportArtifactKind.Tool &&
            c.ToolKind == ToolArtifactKind.Scc);
    }

    [Fact]
    public async Task ScanAsync_ZipWithPowerStigModule_DetectsPowerStigTool()
    {
        CreateZip(_temp.Path, "powerstig.zip",
            [("PowerSTIG/PowerSTIG.psd1", "# module manifest")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().Contain(c =>
            c.ArtifactKind == ImportArtifactKind.Tool &&
            c.ToolKind == ToolArtifactKind.PowerStig);
    }

    [Fact]
    public async Task ScanAsync_ZipWithLgpoExecutable_DetectsLgpoTool()
    {
        CreateZip(_temp.Path, "lgpo-tool.zip",
            [("LGPO.exe", "binary")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().Contain(c =>
            c.ArtifactKind == ImportArtifactKind.Tool &&
            c.ToolKind == ToolArtifactKind.Lgpo);
    }

    [Fact]
    public async Task ScanAsync_ZipWithAdmxFile_DetectsAdmxArtifact()
    {
        CreateZip(_temp.Path, "admx-templates.zip",
            [("templates/custom.admx", "<policyDefinitions/>")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().Contain(c => c.ArtifactKind == ImportArtifactKind.Admx);
    }

    [Fact]
    public async Task ScanAsync_ZipWithGpoLocalPoliciesPath_DetectsGpoArtifact()
    {
        CreateZip(_temp.Path, "gpo-bundle.zip",
            [(".support files/local policies/Windows 10/machine/Registry.pol", "POL")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().Contain(c => c.ArtifactKind == ImportArtifactKind.Gpo);
    }

    [Fact]
    public async Task ScanAsync_ZipWithNoRecognizedContent_ReturnsUnknownCandidate()
    {
        CreateZip(_temp.Path, "mystery.zip",
            [("readme.txt", "nothing useful here")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().ContainSingle()
              .Which.ArtifactKind.Should().Be(ImportArtifactKind.Unknown);
    }

    [Fact]
    public async Task ScanAsync_CancellationTokenAlreadyCancelled_Throws()
    {
        CreateEmptyZip(_temp.Path, "a.zip");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _sut.ScanAsync(_temp.Path, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ScanAsync_ZipInSubdirectory_IsFound()
    {
        var sub = System.IO.Path.Combine(_temp.Path, "sub");
        Directory.CreateDirectory(sub);
        CreateZip(sub, "nested-tool.zip",
            [("LGPO.exe", "binary")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().Contain(c =>
            c.ArtifactKind == ImportArtifactKind.Tool &&
            c.ToolKind == ToolArtifactKind.Lgpo);
    }

    [Fact]
    public async Task ScanAsync_MultipleZips_ReturnsCandidatesForEach()
    {
        CreateZip(_temp.Path, "scc.zip", [("cscc.exe", "")]);
        CreateZip(_temp.Path, "lgpo.zip", [("LGPO.exe", "")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ScanAsync_ZipFileSha256_IsPopulatedFromHashingService()
    {
        const string expectedHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CreateZip(_temp.Path, "tool.zip", [("LGPO.exe", "")]);

        var result = await _sut.ScanAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().Contain(c => c.Sha256 == expectedHash);
    }

    // ── ScanWithCanonicalChecklistAsync ──────────────────────────────────────

    [Fact]
    public async Task ScanWithCanonicalChecklistAsync_EmptyFolder_ReturnsEmptyChecklist()
    {
        var result = await _sut.ScanWithCanonicalChecklistAsync(_temp.Path, CancellationToken.None);

        result.Candidates.Should().BeEmpty();
        result.CanonicalChecklist.Should().NotBeNull();
    }
}

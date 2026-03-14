using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Evidence;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Evidence;

public sealed class EvidenceCollectorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly EvidenceCollector _sut = new();

    public void Dispose() => _temp.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private EvidenceWriteRequest BaseRequest(string? bundleRoot = null) => new()
    {
        BundleRoot = bundleRoot ?? _temp.Path,
        ControlId = "AC-1",
        RuleId = "V-12345",
        Title = "Test evidence",
        Type = EvidenceArtifactType.Command,
        ContentText = "some output text"
    };

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    // ── argument validation ───────────────────────────────────────────────────

    [Fact]
    public void WriteEvidence_EmptyBundleRoot_ThrowsArgumentException()
    {
        var req = BaseRequest(bundleRoot: "");

        var act = () => _sut.WriteEvidence(req);

        act.Should().Throw<ArgumentException>().WithMessage("*BundleRoot*");
    }

    [Fact]
    public void WriteEvidence_WhitespaceBundleRoot_ThrowsArgumentException()
    {
        var req = BaseRequest(bundleRoot: "   ");

        var act = () => _sut.WriteEvidence(req);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WriteEvidence_NonExistentBundleRoot_ThrowsDirectoryNotFoundException()
    {
        var req = BaseRequest(bundleRoot: Path.Combine(_temp.Path, "no-such-dir"));

        var act = () => _sut.WriteEvidence(req);

        act.Should().Throw<DirectoryNotFoundException>().WithMessage("*Bundle root not found*");
    }

    // ── happy path: content text ──────────────────────────────────────────────

    [Fact]
    public void WriteEvidence_WithContentText_WritesEvidenceFile()
    {
        var result = _sut.WriteEvidence(BaseRequest());

        File.Exists(result.EvidencePath).Should().BeTrue();
        File.ReadAllText(result.EvidencePath, Encoding.UTF8).Should().Be("some output text");
    }

    [Fact]
    public void WriteEvidence_WithContentText_WritesMetadataJson()
    {
        var result = _sut.WriteEvidence(BaseRequest());

        File.Exists(result.MetadataPath).Should().BeTrue();
        var json = File.ReadAllText(result.MetadataPath);
        json.Should().Contain("V-12345");
    }

    [Fact]
    public void WriteEvidence_ReturnsMatchingSha256()
    {
        var result = _sut.WriteEvidence(BaseRequest());

        var expected = ComputeSha256(result.EvidencePath);
        result.Sha256.Should().Be(expected);
    }

    [Fact]
    public void WriteEvidence_WithRuleId_UsesRuleIdAsControlKey()
    {
        var req = BaseRequest();
        req.RuleId = "V-99999";

        var result = _sut.WriteEvidence(req);

        result.EvidenceDir.Should().Contain("V-99999");
    }

    [Fact]
    public void WriteEvidence_NoRuleId_UsesControlIdAsControlKey()
    {
        var req = BaseRequest();
        req.RuleId = null;
        req.ControlId = "CM-6";

        var result = _sut.WriteEvidence(req);

        result.EvidenceDir.Should().Contain("CM-6");
    }

    [Fact]
    public void WriteEvidence_NoControlOrRuleId_FallsBackToUnknown()
    {
        var req = BaseRequest();
        req.RuleId = null;
        req.ControlId = null;

        var result = _sut.WriteEvidence(req);

        result.EvidenceDir.Should().Contain("UNKNOWN");
    }

    // ── evidence path via source file ─────────────────────────────────────────

    [Fact]
    public void WriteEvidence_WithSourceFilePath_CopiesFileInsteadOfText()
    {
        var srcPath = _temp.File("source.txt");
        File.WriteAllText(srcPath, "file content", Encoding.UTF8);

        var req = BaseRequest();
        req.ContentText = null;
        req.SourceFilePath = srcPath;

        var result = _sut.WriteEvidence(req);

        File.Exists(result.EvidencePath).Should().BeTrue();
        File.ReadAllText(result.EvidencePath).Should().Be("file content");
    }

    [Fact]
    public void WriteEvidence_WithSourceFilePath_PreservesFileExtension()
    {
        var srcPath = _temp.File("report.xml");
        File.WriteAllText(srcPath, "<root/>", Encoding.UTF8);

        var req = BaseRequest();
        req.ContentText = null;
        req.SourceFilePath = srcPath;

        var result = _sut.WriteEvidence(req);

        Path.GetExtension(result.EvidencePath).Should().Be(".xml");
    }

    // ── extension and type resolution ────────────────────────────────────────

    [Fact]
    public void WriteEvidence_ScreenshotType_UsesPngExtension()
    {
        var req = BaseRequest();
        req.Type = EvidenceArtifactType.Screenshot;
        req.ContentText = "fake-png";

        var result = _sut.WriteEvidence(req);

        Path.GetExtension(result.EvidencePath).Should().Be(".png");
    }

    [Fact]
    public void WriteEvidence_ExplicitExtension_UsesProvidedExtension()
    {
        var req = BaseRequest();
        req.FileExtension = "csv";
        req.ContentText = "col1,col2";

        var result = _sut.WriteEvidence(req);

        Path.GetExtension(result.EvidencePath).Should().Be(".csv");
    }

    [Fact]
    public void WriteEvidence_ExplicitExtensionWithDot_UsesProvidedExtension()
    {
        var req = BaseRequest();
        req.FileExtension = ".log";
        req.ContentText = "log entry";

        var result = _sut.WriteEvidence(req);

        Path.GetExtension(result.EvidencePath).Should().Be(".log");
    }

    // ── metadata content ──────────────────────────────────────────────────────

    [Fact]
    public void WriteEvidence_Metadata_ContainsSha256MatchingFile()
    {
        var result = _sut.WriteEvidence(BaseRequest());

        var metadata = JsonSerializer.Deserialize<EvidenceMetadata>(File.ReadAllText(result.MetadataPath))!;
        metadata.Sha256.Should().Be(result.Sha256);
    }

    [Fact]
    public void WriteEvidence_WithTags_TagsPersistedInMetadata()
    {
        var req = BaseRequest();
        req.Tags = new Dictionary<string, string> { ["env"] = "prod", ["owner"] = "ops" };

        var result = _sut.WriteEvidence(req);

        var metadata = JsonSerializer.Deserialize<EvidenceMetadata>(
            File.ReadAllText(result.MetadataPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        metadata.Tags.Should().ContainKey("env").WhoseValue.Should().Be("prod");
    }

    [Fact]
    public void WriteEvidence_WithRunAndStep_PersistedInMetadata()
    {
        var req = BaseRequest();
        req.RunId = "run-001";
        req.StepName = "apply_dsc";

        var result = _sut.WriteEvidence(req);

        var metadata = JsonSerializer.Deserialize<EvidenceMetadata>(
            File.ReadAllText(result.MetadataPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        metadata.RunId.Should().Be("run-001");
        metadata.StepName.Should().Be("apply_dsc");
    }

    [Fact]
    public void WriteEvidence_EvidenceIdIsNonEmpty()
    {
        var result = _sut.WriteEvidence(BaseRequest());

        result.EvidenceId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WriteEvidence_CreatesEvidenceByControlDirectory()
    {
        var result = _sut.WriteEvidence(BaseRequest());

        result.EvidenceDir.Should().StartWith(Path.Combine(_temp.Path, "Evidence", "by_control"));
        Directory.Exists(result.EvidenceDir).Should().BeTrue();
    }
}

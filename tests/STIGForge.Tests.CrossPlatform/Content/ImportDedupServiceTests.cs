using FluentAssertions;
using STIGForge.Content.Import;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class ImportDedupServiceTests
{
    private readonly ImportDedupService _svc = new();

    // ── Empty input ──────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_EmptyCandidates_ReturnsEmptyOutcome()
    {
        var result = _svc.Resolve([]);

        result.Winners.Should().BeEmpty();
        result.Suppressed.Should().BeEmpty();
        result.Decisions.Should().BeEmpty();
    }

    // ── Single candidate is always the winner ────────────────────────────────

    [Fact]
    public void Resolve_SingleCandidate_IsWinner()
    {
        var candidate = MakeCandidate("pack-a.zip", ImportArtifactKind.Stig, "STIG-001", "1.0");
        var result = _svc.Resolve([candidate]);

        result.Winners.Should().HaveCount(1);
        result.Suppressed.Should().BeEmpty();
        result.Winners[0].ContentKey.Should().Be("STIG-001");
    }

    // ── Dedup by logical content key ─────────────────────────────────────────

    [Fact]
    public void Resolve_TwoCandidates_SameContentKey_SameHash_OnlyOneWinner()
    {
        var a = MakeCandidate("a.zip", ImportArtifactKind.Stig, "STIG-001", "V1R1", sha: "aaaa");
        var b = MakeCandidate("b.zip", ImportArtifactKind.Stig, "STIG-001", "V1R1", sha: "aaaa");

        var result = _svc.Resolve([a, b]);

        result.Winners.Should().HaveCount(1);
        result.Suppressed.Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_TwoCandidates_DifferentContentKeys_BothWin()
    {
        var a = MakeCandidate("a.zip", ImportArtifactKind.Stig, "STIG-001", "V1R1");
        var b = MakeCandidate("b.zip", ImportArtifactKind.Stig, "STIG-002", "V1R1");

        var result = _svc.Resolve([a, b]);

        result.Winners.Should().HaveCount(2);
        result.Suppressed.Should().BeEmpty();
    }

    // ── STIG version ranking ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_ForStig_HigherVersionWins()
    {
        var older = MakeStigCandidate("old.zip", "STIG-001", versionTag: "V1R2");
        var newer = MakeStigCandidate("new.zip", "STIG-001", versionTag: "V2R1");

        var result = _svc.Resolve([older, newer]);

        result.Winners.Should().HaveCount(1);
        result.Winners[0].VersionTag.Should().Be("V2R1");
    }

    [Fact]
    public void Resolve_ForStig_TiedVersion_HigherReleaseDateWins()
    {
        var older = MakeStigCandidate("old.zip", "STIG-001", versionTag: "V1R1",
            benchmarkDate: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = MakeStigCandidate("new.zip", "STIG-001", versionTag: "V1R1",
            benchmarkDate: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var result = _svc.Resolve([older, newer]);

        result.Winners.Should().HaveCount(1);
        result.Winners[0].BenchmarkDate.Should().Be(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
    }

    // ── SCAP duplicate with different hashes ─────────────────────────────────

    [Fact]
    public void Resolve_DuplicateScap_DifferentHashes_NoNiwcEnhanced_UsesDeterministicFallback()
    {
        var a = MakeCandidate("a.zip", ImportArtifactKind.Scap, "SCAP-001", "V1R1", sha: "hash-a");
        var b = MakeCandidate("b.zip", ImportArtifactKind.Scap, "SCAP-001", "V1R1", sha: "hash-b");

        var result = _svc.Resolve([a, b]);

        result.Winners.Should().HaveCount(1);
        result.Suppressed.Should().HaveCount(1);
        result.Decisions.Should().HaveCount(1);
        result.Decisions[0].Should().Contain("NIWC Enhanced not found");
    }

    [Fact]
    public void Resolve_DuplicateScap_DifferentHashes_WithNiwcEnhanced_SelectsNiwc()
    {
        var standard = MakeCandidate("a.zip", ImportArtifactKind.Scap, "SCAP-001", "V1R1", sha: "hash-a");
        var niwc = MakeCandidate("b.zip", ImportArtifactKind.Scap, "SCAP-001", "V1R1", sha: "hash-b",
            provenance: ImportProvenance.ConsolidatedBundle, isNiwc: true);

        var result = _svc.Resolve([standard, niwc]);

        result.Winners.Should().HaveCount(1);
        result.Winners[0].IsNiwcEnhanced.Should().BeTrue();
        result.Decisions[0].Should().Contain("NIWC Enhanced");
    }

    // ── Tool kind grouping ───────────────────────────────────────────────────

    [Fact]
    public void Resolve_TwoToolCandidates_SameToolKind_OneWinner()
    {
        var a = new ImportInboxCandidate
        {
            ZipPath = "bundle-a.zip", FileName = "a.zip",
            ArtifactKind = ImportArtifactKind.Tool,
            ToolKind = ToolArtifactKind.EvaluateStig,
            Sha256 = "sha-a"
        };
        var b = new ImportInboxCandidate
        {
            ZipPath = "bundle-b.zip", FileName = "b.zip",
            ArtifactKind = ImportArtifactKind.Tool,
            ToolKind = ToolArtifactKind.EvaluateStig,
            Sha256 = "sha-b"
        };

        var result = _svc.Resolve([a, b]);

        result.Winners.Should().HaveCount(1);
        result.Suppressed.Should().HaveCount(1);
    }

    // ── Output ordering ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_Winners_OrderedByZipPathThenArtifactKindThenContentKey()
    {
        var a = MakeCandidate("c-pack.zip", ImportArtifactKind.Scap, "SCAP-X", "V1R1");
        var b = MakeCandidate("a-pack.zip", ImportArtifactKind.Stig, "STIG-X", "V1R1");
        var c = MakeCandidate("b-pack.zip", ImportArtifactKind.Gpo, "GPO-X", "V1R1");

        var result = _svc.Resolve([a, b, c]);

        result.Winners.Should().HaveCount(3);
        result.Winners[0].ZipPath.Should().Be("a-pack.zip");
        result.Winners[1].ZipPath.Should().Be("b-pack.zip");
        result.Winners[2].ZipPath.Should().Be("c-pack.zip");
    }

    // ── Decisions list ───────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoDuplicates_DecisionsIsEmpty()
    {
        var a = MakeCandidate("a.zip", ImportArtifactKind.Stig, "STIG-001", "V1R1");
        var b = MakeCandidate("b.zip", ImportArtifactKind.Stig, "STIG-002", "V1R1");

        var result = _svc.Resolve([a, b]);

        result.Decisions.Should().BeEmpty();
    }

    // ── SHA-256 used for grouping when content key is absent ─────────────────

    [Fact]
    public void Resolve_NoContentKey_GroupsByArtifactKindAndSha()
    {
        var a = new ImportInboxCandidate
        {
            ZipPath = "a.zip", FileName = "a.zip",
            ArtifactKind = ImportArtifactKind.Scap,
            ContentKey = string.Empty,
            Sha256 = "shared-sha"
        };
        var b = new ImportInboxCandidate
        {
            ZipPath = "b.zip", FileName = "b.zip",
            ArtifactKind = ImportArtifactKind.Scap,
            ContentKey = string.Empty,
            Sha256 = "shared-sha"
        };

        var result = _svc.Resolve([a, b]);

        result.Winners.Should().HaveCount(1);
        result.Suppressed.Should().HaveCount(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ImportInboxCandidate MakeCandidate(
        string zipPath,
        ImportArtifactKind kind,
        string contentKey,
        string versionTag,
        string sha = "abc123",
        ImportProvenance provenance = ImportProvenance.Unknown,
        bool isNiwc = false) =>
        new()
        {
            ZipPath = zipPath,
            FileName = Path.GetFileName(zipPath),
            ArtifactKind = kind,
            ContentKey = contentKey,
            VersionTag = versionTag,
            Sha256 = sha,
            ImportedFrom = provenance,
            IsNiwcEnhanced = isNiwc
        };

    private static ImportInboxCandidate MakeStigCandidate(
        string zipPath,
        string contentKey,
        string versionTag = "",
        DateTimeOffset? benchmarkDate = null) =>
        new()
        {
            ZipPath = zipPath,
            FileName = Path.GetFileName(zipPath),
            ArtifactKind = ImportArtifactKind.Stig,
            ContentKey = contentKey,
            VersionTag = versionTag,
            Sha256 = Guid.NewGuid().ToString(),
            BenchmarkDate = benchmarkDate
        };
}

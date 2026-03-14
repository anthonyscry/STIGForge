using System.Text.Json;
using FluentAssertions;
using STIGForge.Evidence;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Evidence;

public sealed class EvidenceIndexServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private EvidenceIndexService CreateSut() => new(_temp.Path);

    private string ControlDir(string controlKey)
    {
        var dir = Path.Combine(_temp.Path, "Evidence", "by_control", controlKey);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteEvidencePair(string controlDir, string baseName,
        EvidenceMetadata metadata, string evidenceContent = "evidence data")
    {
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(controlDir, baseName + ".json"), json);
        File.WriteAllText(Path.Combine(controlDir, baseName + ".txt"), evidenceContent);
    }

    private static EvidenceMetadata MakeMeta(string controlId, string ruleId, string type = "Command",
        string? runId = null, string? stepName = null, string? supersedesId = null,
        Dictionary<string, string>? tags = null)
        => new()
        {
            ControlId = controlId,
            RuleId = ruleId,
            Title = $"Test evidence for {controlId}",
            Type = type,
            Source = "Tests",
            TimestampUtc = DateTimeOffset.UtcNow.ToString("o"),
            Host = "test-host",
            User = "tester",
            BundleRoot = string.Empty,
            Sha256 = "abc123",
            RunId = runId,
            StepName = stepName,
            SupersedesEvidenceId = supersedesId,
            Tags = tags
        };

    // ── BuildIndexAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task BuildIndexAsync_NoByCControlDir_ReturnsEmptyIndex()
    {
        var sut = CreateSut();

        var index = await sut.BuildIndexAsync();

        index.TotalEntries.Should().Be(0);
        index.Entries.Should().BeEmpty();
        index.BundleRoot.Should().Be(_temp.Path);
    }

    [Fact]
    public async Task BuildIndexAsync_EmptyByControlDir_ReturnsEmptyIndex()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "Evidence", "by_control"));
        var sut = CreateSut();

        var index = await sut.BuildIndexAsync();

        index.TotalEntries.Should().Be(0);
    }

    [Fact]
    public async Task BuildIndexAsync_SingleEntry_PopulatesIndexCorrectly()
    {
        var dir = ControlDir("AC-1");
        WriteEvidencePair(dir, "evidence_20240101_120000_000_command_abcdef",
            MakeMeta("AC-1", "V-11111"));
        var sut = CreateSut();

        var index = await sut.BuildIndexAsync();

        index.TotalEntries.Should().Be(1);
        var entry = index.Entries.Should().ContainSingle().Subject;
        entry.ControlKey.Should().Be("AC-1");
        entry.RuleId.Should().Be("V-11111");
        entry.Type.Should().Be("Command");
    }

    [Fact]
    public async Task BuildIndexAsync_MultipleControls_AllEntriesIncluded()
    {
        WriteEvidencePair(ControlDir("AC-1"), "ev1", MakeMeta("AC-1", "V-1"));
        WriteEvidencePair(ControlDir("SI-2"), "ev2", MakeMeta("SI-2", "V-2"));
        WriteEvidencePair(ControlDir("AC-1"), "ev3", MakeMeta("AC-1", "V-3"));
        var sut = CreateSut();

        var index = await sut.BuildIndexAsync();

        index.TotalEntries.Should().Be(3);
    }

    [Fact]
    public async Task BuildIndexAsync_EntriesSortedByControlKeyThenTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var earlier = MakeMeta("AC-1", "V-1");
        earlier.TimestampUtc = now.AddMinutes(-10).ToString("o");
        var later = MakeMeta("AC-1", "V-2");
        later.TimestampUtc = now.ToString("o");

        WriteEvidencePair(ControlDir("AC-1"), "ev_later", later);
        WriteEvidencePair(ControlDir("AC-1"), "ev_earlier", earlier);

        var sut = CreateSut();
        var index = await sut.BuildIndexAsync();

        string.Compare(index.Entries[0].TimestampUtc, index.Entries[1].TimestampUtc, StringComparison.Ordinal)
              .Should().BeNegative();
    }

    [Fact]
    public async Task BuildIndexAsync_MalformedJsonFile_SkipsEntry()
    {
        var dir = ControlDir("CM-1");
        File.WriteAllText(Path.Combine(dir, "bad_entry.json"), "{ invalid json {{{");
        var sut = CreateSut();

        var index = await sut.BuildIndexAsync();

        index.TotalEntries.Should().Be(0); // skipped
    }

    [Fact]
    public async Task BuildIndexAsync_SummaryFileWithUnderscore_IsIgnored()
    {
        var dir = ControlDir("IR-1");
        // Files starting with '_' are skipped (summary files)
        File.WriteAllText(Path.Combine(dir, "_summary.json"), "{}");
        var sut = CreateSut();

        var index = await sut.BuildIndexAsync();

        index.TotalEntries.Should().Be(0);
    }

    [Fact]
    public async Task BuildIndexAsync_CancellationRequested_Throws()
    {
        ControlDir("CM-7"); // ensure dir exists
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = CreateSut();

        var act = async () => await sut.BuildIndexAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BuildIndexAsync_EvidenceRelativePathIsPopulated()
    {
        var dir = ControlDir("AU-2");
        WriteEvidencePair(dir, "ev_au2", MakeMeta("AU-2", "V-555"));
        var sut = CreateSut();

        var index = await sut.BuildIndexAsync();

        index.Entries.Should().ContainSingle()
             .Which.RelativePath.Should().NotBeNullOrEmpty();
    }

    // ── WriteIndexAsync / ReadIndexAsync ─────────────────────────────────────

    [Fact]
    public async Task WriteIndexAsync_CreatesEvidenceIndexJson()
    {
        var sut = CreateSut();
        var index = new EvidenceIndex { BundleRoot = _temp.Path, TotalEntries = 0 };

        await sut.WriteIndexAsync(index);

        var expectedPath = Path.Combine(_temp.Path, "Evidence", "evidence_index.json");
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public async Task ReadIndexAsync_NoFile_ReturnsNull()
    {
        var sut = CreateSut();

        var index = await sut.ReadIndexAsync();

        index.Should().BeNull();
    }

    [Fact]
    public async Task ReadIndexAsync_AfterWrite_ReturnsMatchingIndex()
    {
        var dir = ControlDir("PE-1");
        WriteEvidencePair(dir, "ev_pe1", MakeMeta("PE-1", "V-777"));

        var sut = CreateSut();
        var built = await sut.BuildIndexAsync();
        await sut.WriteIndexAsync(built);

        var read = await sut.ReadIndexAsync();

        read.Should().NotBeNull();
        read!.TotalEntries.Should().Be(1);
        read.Entries.Should().ContainSingle()
            .Which.ControlKey.Should().Be("PE-1");
    }

    [Fact]
    public async Task WriteIndexAsync_OverwritesExistingFile()
    {
        var sut = CreateSut();
        await sut.WriteIndexAsync(new EvidenceIndex { BundleRoot = _temp.Path, TotalEntries = 0 });

        // Write a second time with different data
        var dir = ControlDir("RA-1");
        WriteEvidencePair(dir, "ev_ra1", MakeMeta("RA-1", "V-999"));
        var rebuilt = await sut.BuildIndexAsync();
        await sut.WriteIndexAsync(rebuilt);

        var read = await sut.ReadIndexAsync();
        read!.TotalEntries.Should().Be(1);
    }

    // ── static query helpers ──────────────────────────────────────────────────

    [Fact]
    public async Task GetEvidenceForControl_ReturnsOnlyMatchingEntries()
    {
        WriteEvidencePair(ControlDir("AC-1"), "ev1", MakeMeta("AC-1", "V-1"));
        WriteEvidencePair(ControlDir("SI-2"), "ev2", MakeMeta("SI-2", "V-2"));
        var sut = CreateSut();
        var index = await sut.BuildIndexAsync();

        var results = EvidenceIndexService.GetEvidenceForControl(index, "AC-1");

        results.Should().ContainSingle().Which.ControlKey.Should().Be("AC-1");
    }

    [Fact]
    public async Task GetEvidenceForControl_CaseInsensitive()
    {
        WriteEvidencePair(ControlDir("AC-1"), "ev1", MakeMeta("AC-1", "V-1"));
        var sut = CreateSut();
        var index = await sut.BuildIndexAsync();

        var results = EvidenceIndexService.GetEvidenceForControl(index, "ac-1");

        results.Should().ContainSingle();
    }

    [Fact]
    public async Task GetEvidenceByType_ReturnsMatchingType()
    {
        WriteEvidencePair(ControlDir("SC-1"), "ev_cmd", MakeMeta("SC-1", "V-1", type: "Command"));
        WriteEvidencePair(ControlDir("SC-1"), "ev_file", MakeMeta("SC-1", "V-1", type: "File"));
        var sut = CreateSut();
        var index = await sut.BuildIndexAsync();

        var results = EvidenceIndexService.GetEvidenceByType(index, "File");

        results.Should().ContainSingle().Which.Type.Should().Be("File");
    }

    [Fact]
    public async Task GetEvidenceByRun_ReturnsMatchingRun()
    {
        WriteEvidencePair(ControlDir("IA-1"), "ev_run1", MakeMeta("IA-1", "V-1", runId: "run-123"));
        WriteEvidencePair(ControlDir("IA-1"), "ev_run2", MakeMeta("IA-1", "V-2", runId: "run-999"));
        var sut = CreateSut();
        var index = await sut.BuildIndexAsync();

        var results = EvidenceIndexService.GetEvidenceByRun(index, "run-123");

        results.Should().ContainSingle().Which.RunId.Should().Be("run-123");
    }

    [Fact]
    public async Task GetEvidenceByRun_NullRunId_ReturnsEmpty()
    {
        WriteEvidencePair(ControlDir("IA-2"), "ev_norun", MakeMeta("IA-2", "V-1")); // no RunId
        var sut = CreateSut();
        var index = await sut.BuildIndexAsync();

        var results = EvidenceIndexService.GetEvidenceByRun(index, "run-xyz");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEvidenceByTag_ReturnsMatchingTag()
    {
        var taggedMeta = MakeMeta("CP-1", "V-1", tags: new Dictionary<string, string> { ["env"] = "prod" });
        var untaggedMeta = MakeMeta("CP-1", "V-2", tags: new Dictionary<string, string> { ["env"] = "dev" });
        WriteEvidencePair(ControlDir("CP-1"), "ev_prod", taggedMeta);
        WriteEvidencePair(ControlDir("CP-1"), "ev_dev", untaggedMeta);
        var sut = CreateSut();
        var index = await sut.BuildIndexAsync();

        var results = EvidenceIndexService.GetEvidenceByTag(index, "env", "prod");

        results.Should().ContainSingle().Which.Tags!["env"].Should().Be("prod");
    }

    // ── GetLineageChain ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetLineageChain_SingleEntry_ReturnsOneItemChain()
    {
        WriteEvidencePair(ControlDir("SA-1"), "ev_root", MakeMeta("SA-1", "V-1"));
        var sut = CreateSut();
        var index = await sut.BuildIndexAsync();

        var chain = EvidenceIndexService.GetLineageChain(index, "ev_root");

        chain.Should().ContainSingle().Which.EvidenceId.Should().Be("ev_root");
    }

    [Fact]
    public async Task GetLineageChain_ThreeGenerations_ReturnsFullChain()
    {
        var gen3 = MakeMeta("AU-1", "V-1", supersedesId: "ev_gen2");
        var gen2 = MakeMeta("AU-1", "V-1", supersedesId: "ev_gen1");
        var gen1 = MakeMeta("AU-1", "V-1");
        var dir = ControlDir("AU-1");
        WriteEvidencePair(dir, "ev_gen3", gen3);
        WriteEvidencePair(dir, "ev_gen2", gen2);
        WriteEvidencePair(dir, "ev_gen1", gen1);
        var sut = CreateSut();
        var index = await sut.BuildIndexAsync();

        var chain = EvidenceIndexService.GetLineageChain(index, "ev_gen3");

        chain.Should().HaveCount(3);
        chain[0].EvidenceId.Should().Be("ev_gen3");
        chain[1].EvidenceId.Should().Be("ev_gen2");
        chain[2].EvidenceId.Should().Be("ev_gen1");
    }

    [Fact]
    public async Task GetLineageChain_CircularReference_DoesNotInfiniteLoop()
    {
        var a = MakeMeta("SC-3", "V-1", supersedesId: "ev_b");
        var b = MakeMeta("SC-3", "V-2", supersedesId: "ev_a");
        var dir = ControlDir("SC-3");
        WriteEvidencePair(dir, "ev_a", a);
        WriteEvidencePair(dir, "ev_b", b);
        var sut = CreateSut();
        var index = await sut.BuildIndexAsync();

        var chain = EvidenceIndexService.GetLineageChain(index, "ev_a");

        chain.Should().HaveCount(2); // a → b, then b→a is visited already
    }

    [Fact]
    public void GetLineageChain_NonExistentId_ReturnsEmpty()
    {
        var index = new EvidenceIndex();

        var chain = EvidenceIndexService.GetLineageChain(index, "no-such-id");

        chain.Should().BeEmpty();
    }
}

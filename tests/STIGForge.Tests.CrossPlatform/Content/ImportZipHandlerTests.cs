using System.IO.Compression;
using System.Text;
using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Content.Models;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class ImportZipHandlerTests : IDisposable
{
    private readonly string _tempDir;

    public ImportZipHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "zip-handler-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string NewZipPath() => Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".zip");

    private string NewDestDir()
    {
        var dir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CreateZipWithEntry(string zipPath, string entryName, string content)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static void CreateZipWithBytes(string zipPath, string entryName, byte[] bytes)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    // ── ExtractZipSafelyAsync tests ──────────────────────────────────────────

    [Fact]
    public async Task ExtractZip_ValidEntries_ExtractsToDestination()
    {
        var zipPath = NewZipPath();
        var dest = NewDestDir();
        CreateZipWithEntry(zipPath, "readme.txt", "hello world");

        await new ImportZipHandler().ExtractZipSafelyAsync(zipPath, dest, CancellationToken.None);

        File.Exists(Path.Combine(dest, "readme.txt")).Should().BeTrue();
        File.ReadAllText(Path.Combine(dest, "readme.txt")).Should().Be("hello world");
    }

    [Fact]
    public async Task ExtractZip_ZipSlipAttempt_ThrowsOrSkipsEntry()
    {
        var zipPath = NewZipPath();
        var dest = NewDestDir();

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var traversal = archive.CreateEntry("../../evil.txt");
            using var writer = new StreamWriter(traversal.Open());
            writer.Write("evil");
        }

        var act = async () =>
            await new ImportZipHandler().ExtractZipSafelyAsync(zipPath, dest, CancellationToken.None);

        await act.Should().ThrowAsync<ParsingException>()
            .WithMessage("*IMPORT-ARCHIVE-002*");
    }

    [Fact]
    public async Task ExtractZip_ExceedsBytesLimit_ThrowsOrTruncates()
    {
        var zipPath = NewZipPath();
        var dest = NewDestDir();

        // Use a small limit (100 bytes) so we can easily trigger it.
        const int limit = 100;
        var largeContent = new string('A', 200);
        CreateZipWithEntry(zipPath, "big.txt", largeContent);

        var act = async () =>
            await new ImportZipHandler(maxExtractedBytes: limit)
                .ExtractZipSafelyAsync(zipPath, dest, CancellationToken.None);

        await act.Should().ThrowAsync<ParsingException>()
            .WithMessage("*IMPORT-ARCHIVE-003*");
    }

    [Fact]
    public async Task ExtractZip_EmptyZip_NoFilesExtracted()
    {
        var zipPath = NewZipPath();
        var dest = NewDestDir();

        using (ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }

        await new ImportZipHandler().ExtractZipSafelyAsync(zipPath, dest, CancellationToken.None);

        Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractZip_NestedDirectories_MaintainsStructure()
    {
        var zipPath = NewZipPath();
        var dest = NewDestDir();

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("subdir/nested/file.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("nested content");
        }

        await new ImportZipHandler().ExtractZipSafelyAsync(zipPath, dest, CancellationToken.None);

        var expectedPath = Path.Combine(dest, "subdir", "nested", "file.txt");
        File.Exists(expectedPath).Should().BeTrue();
        File.ReadAllText(expectedPath).Should().Be("nested content");
    }

    // ── CountingStream tests (internal nested class) ─────────────────────────

    [Fact]
    public void CountingStream_TracksActualBytesWritten()
    {
        var inner = new MemoryStream();
        var counting = new ImportZipHandler.CountingStream(inner);
        var data = new byte[512];
        Random.Shared.NextBytes(data);

        counting.Write(data, 0, data.Length);

        counting.BytesWritten.Should().Be(512);
        inner.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task CountingStream_DeclaredVsActual_ActualIsUsed()
    {
        // Verify that bytes written accumulate based on what is actually passed to Write/WriteAsync,
        // not any pre-declared size.  Write 300 bytes in three chunks.
        var inner = new MemoryStream();
        var counting = new ImportZipHandler.CountingStream(inner);

        var chunk = new byte[100];
        counting.Write(chunk, 0, 100);
        await counting.WriteAsync(chunk, 0, 100, CancellationToken.None);
        await counting.WriteAsync(new ReadOnlyMemory<byte>(chunk), CancellationToken.None);

        counting.BytesWritten.Should().Be(300);
    }

    // ── Entry-count limit ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractZip_ExceedsEntryCountLimit_ThrowsParsingException()
    {
        var zipPath = NewZipPath();
        var dest = NewDestDir();
        const int overLimit = 4097;

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (var i = 0; i < overLimit; i++)
            {
                var entry = archive.CreateEntry($"file_{i:D5}.txt");
                using var w = new StreamWriter(entry.Open());
                w.Write("x");
            }
        }

        var act = async () =>
            await new ImportZipHandler().ExtractZipSafelyAsync(zipPath, dest, CancellationToken.None);

        await act.Should().ThrowAsync<ParsingException>()
            .WithMessage("*IMPORT-ARCHIVE-001*");
    }

    [Fact]
    public async Task ExtractZip_ExactlyAtEntryCountLimit_Succeeds()
    {
        var zipPath = NewZipPath();
        var dest = NewDestDir();
        const int atLimit = 4096;

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (var i = 0; i < atLimit; i++)
            {
                var entry = archive.CreateEntry($"f_{i:D5}.txt");
                using var w = new StreamWriter(entry.Open());
                w.Write("y");
            }
        }

        var act = async () =>
            await new ImportZipHandler().ExtractZipSafelyAsync(zipPath, dest, CancellationToken.None);

        await act.Should().NotThrowAsync("exactly 4096 entries is within the allowed limit");
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractZip_CancelledBeforeStart_ThrowsOperationCanceled()
    {
        var zipPath = NewZipPath();
        var dest = NewDestDir();
        CreateZipWithEntry(zipPath, "file.txt", "content");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
            await new ImportZipHandler().ExtractZipSafelyAsync(zipPath, dest, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Nested zip expansion ──────────────────────────────────────────────────

    [Fact]
    public async Task ExpandNestedZips_SingleNested_ExtractsContent()
    {
        var root = NewDestDir();

        // Create inner.zip containing inner.txt
        var innerZipPath = Path.Combine(root, "inner.zip");
        CreateZipWithEntry(innerZipPath, "inner.txt", "nested content");

        await new ImportZipHandler().ExpandNestedZipArchivesAsync(root, maxPasses: 3, CancellationToken.None);

        var expectedFile = Path.Combine(root, "inner", "inner.txt");
        File.Exists(expectedFile).Should().BeTrue("nested zip must be expanded into a sibling directory");
        File.ReadAllText(expectedFile).Should().Be("nested content");
    }

    [Fact]
    public async Task ExpandNestedZips_NoNestedZips_CompletesWithNoChanges()
    {
        var root = NewDestDir();
        File.WriteAllText(Path.Combine(root, "regular.txt"), "not a zip");

        await new ImportZipHandler().ExpandNestedZipArchivesAsync(root, maxPasses: 3, CancellationToken.None);

        // Only the original file should be present
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Should().ContainSingle(f => f.EndsWith("regular.txt"));
    }

    [Fact]
    public async Task ExpandNestedZips_AlreadyExpandedDir_NotReexpanded()
    {
        var root = NewDestDir();

        // Pre-create the expected extraction directory with content
        var innerZipPath = Path.Combine(root, "pkg.zip");
        CreateZipWithEntry(innerZipPath, "file.txt", "original");

        var preExpandedDir = Path.Combine(root, "pkg");
        Directory.CreateDirectory(preExpandedDir);
        File.WriteAllText(Path.Combine(preExpandedDir, "existing.txt"), "already here");

        await new ImportZipHandler().ExpandNestedZipArchivesAsync(root, maxPasses: 3, CancellationToken.None);

        // The pre-existing directory content must be preserved, not overwritten
        File.Exists(Path.Combine(preExpandedDir, "existing.txt")).Should().BeTrue();
        File.Exists(Path.Combine(preExpandedDir, "file.txt")).Should().BeFalse("already-expanded dir is skipped");
    }

    // ── FindScopedGpoRoots ────────────────────────────────────────────────────

    [Fact]
    public void FindScopedGpoRoots_EmptyDirectory_ReturnsEmpty()
    {
        var root = NewDestDir();

        var result = new ImportZipHandler().FindScopedGpoRoots(root);

        result.Should().BeEmpty();
    }

    [Fact]
    public void FindScopedGpoRoots_LocalPolicy_FindsScope()
    {
        var root = NewDestDir();

        // Create "Support Files/Local Policies/WorkstationScope/GptTmpl.inf"
        var policyDir = Path.Combine(root, "Support Files", "Local Policies", "WorkstationScope");
        Directory.CreateDirectory(policyDir);
        File.WriteAllText(Path.Combine(policyDir, "GptTmpl.inf"), "[System Access]");

        var result = new ImportZipHandler().FindScopedGpoRoots(root);

        result.Should().ContainSingle(r => r.PackName.Contains("WorkstationScope") && r.SourceLabel == "gpo_lgpo_import");
    }

    [Fact]
    public void FindScopedGpoRoots_DotSupportFiles_FindsScope()
    {
        var root = NewDestDir();

        // Alternate spelling with leading dot
        var policyDir = Path.Combine(root, ".Support Files", "Local Policies", "MemberServerScope");
        Directory.CreateDirectory(policyDir);
        File.WriteAllText(Path.Combine(policyDir, "GptTmpl.inf"), "[System Access]");

        var result = new ImportZipHandler().FindScopedGpoRoots(root);

        result.Should().ContainSingle(r => r.PackName.Contains("MemberServerScope"));
    }

    [Fact]
    public void FindScopedGpoRoots_MultipleScopes_ReturnsAll()
    {
        var root = NewDestDir();

        foreach (var scope in new[] { "ScopeA", "ScopeB", "ScopeC" })
        {
            var dir = Path.Combine(root, "Support Files", "Local Policies", scope);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "GptTmpl.inf"), "[System Access]");
        }

        var result = new ImportZipHandler().FindScopedGpoRoots(root);

        result.Should().HaveCount(3);
        result.Select(r => r.PackName).Should().Contain(n => n.Contains("ScopeA"));
        result.Select(r => r.PackName).Should().Contain(n => n.Contains("ScopeB"));
        result.Select(r => r.PackName).Should().Contain(n => n.Contains("ScopeC"));
    }

    // ── Path traversal edge cases ─────────────────────────────────────────────

    [Fact]
    public async Task ExtractZip_AbsolutePathEntry_ThrowsParsingException()
    {
        var zipPath = NewZipPath();
        var dest = NewDestDir();

        // Some zip tools produce entries with absolute paths
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Use a relative path that looks suspicious when combined
            archive.CreateEntry("subdir/../../../outside.txt");
        }

        var act = async () =>
            await new ImportZipHandler().ExtractZipSafelyAsync(zipPath, dest, CancellationToken.None);

        await act.Should().ThrowAsync<ParsingException>()
            .WithMessage("*IMPORT-ARCHIVE-002*");
    }
}

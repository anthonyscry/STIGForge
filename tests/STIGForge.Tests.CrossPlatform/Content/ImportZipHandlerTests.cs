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
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Hashing;
using STIGForge.Infrastructure.Storage;
using Xunit.Abstractions;

namespace STIGForge.UnitTests.Content;

public sealed class ContentPackImporterDirectoryHashTests
{
    private readonly ITestOutputHelper _output;
    private readonly IPathBuilder _paths;
    private readonly IHashingService _hash;
    private readonly IContentPackRepository _packs;
    private readonly IControlRepository _controls;

    public ContentPackImporterDirectoryHashTests(ITestOutputHelper output)
    {
        _output = output;

        var tempDir = Path.Combine(Path.GetTempPath(), "stigforge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        _paths = new TestPathBuilder(tempDir);
        _hash = new Sha256HashingService();

        var dbPath = Path.Combine(tempDir, "test.db");
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        DbBootstrap.EnsureCreated(cs);

        _packs = new SqliteContentPackRepository(cs);
        _controls = new SqliteJsonControlRepository(cs);
    }

    [Fact]
    public async Task ImportDirectoryAsPackAsync_ComputesDeterministicSha256ManifestHash()
    {
        // Arrange: Create a test directory with predictable content
        var testDir = Path.Combine(Path.GetTempPath(), "stigforge-dir-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            // Create test files with deterministic content
            var file1 = Path.Combine(testDir, "test.xml");
            File.WriteAllText(file1, "<Benchmark>test content</Benchmark>");

            var subDir = Path.Combine(testDir, "sub");
            Directory.CreateDirectory(subDir);
            var file2 = Path.Combine(subDir, "other.xml");
            File.WriteAllText(file2, "<Other>content</Other>");

            var importer = new ContentPackImporter(_paths, _hash, _packs, _controls);

            // Act: Import the directory
            var pack = await importer.ImportDirectoryAsPackAsync(testDir, "TestPack", "test", CancellationToken.None);

            // Assert: ManifestSha256 is a 64-char lowercase hex string (SHA-256 format)
            Assert.NotNull(pack.ManifestSha256);
            Assert.Equal(64, pack.ManifestSha256.Length);
            Assert.Matches("^[a-f0-9]{64}$", pack.ManifestSha256);
            Assert.Equal("sha256", pack.HashAlgorithm);
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ImportDirectoryAsPackAsync_SameDirectoryYieldsIdenticalHash()
    {
        // Arrange: Create a test directory
        var testDir1 = Path.Combine(Path.GetTempPath(), "stigforge-dir-consistency-1-" + Guid.NewGuid().ToString("N"));
        var testDir2 = Path.Combine(Path.GetTempPath(), "stigforge-dir-consistency-2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir1);
        Directory.CreateDirectory(testDir2);

        try
        {
            // Create identical content in both directories
            var CreateContent = (string dir) =>
            {
                File.WriteAllText(Path.Combine(dir, "benchmark.xml"), "<Benchmark>V1R1</Benchmark>");
                var sub = Path.Combine(dir, "files");
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(sub, "data.txt"), "test data");
            };

            CreateContent(testDir1);
            CreateContent(testDir2);

            var importer = new ContentPackImporter(_paths, _hash, _packs, _controls);

            // Act: Import both directories
            var pack1 = await importer.ImportDirectoryAsPackAsync(testDir1, "TestPack1", "test", CancellationToken.None);
            var pack2 = await importer.ImportDirectoryAsPackAsync(testDir2, "TestPack2", "test", CancellationToken.None);

            // Assert: Both imports produce the same manifest hash despite different paths and names
            Assert.Equal(pack1.ManifestSha256, pack2.ManifestSha256);
            _output.WriteLine($"Hash 1: {pack1.ManifestSha256}");
            _output.WriteLine($"Hash 2: {pack2.ManifestSha256}");
        }
        finally
        {
            try { Directory.Delete(testDir1, true); } catch { }
            try { Directory.Delete(testDir2, true); } catch { }
        }
    }

    [Fact]
    public async Task ImportDirectoryAsPackAsync_Dedupe_SameContentReturnsExistingPack()
    {
        // Arrange: Create two directories with identical content
        var testDir1 = Path.Combine(Path.GetTempPath(), "stigforge-dedupe-1-" + Guid.NewGuid().ToString("N"));
        var testDir2 = Path.Combine(Path.GetTempPath(), "stigforge-dedupe-2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir1);
        Directory.CreateDirectory(testDir2);

        try
        {
            var CreateContent = (string dir) =>
            {
                File.WriteAllText(Path.Combine(dir, "benchmark.xml"), "<Benchmark id='test'>V1R1</Benchmark>");
            };

            CreateContent(testDir1);
            CreateContent(testDir2);

            var importer = new ContentPackImporter(_paths, _hash, _packs, _controls);

            // Act: Import first directory
            var pack1 = await importer.ImportDirectoryAsPackAsync(testDir1, "TestPack1", "test", CancellationToken.None);

            // Import second directory with identical content
            var pack2 = await importer.ImportDirectoryAsPackAsync(testDir2, "TestPack2", "test", CancellationToken.None);

            // Assert: Second import returns the first pack (deduped)
            Assert.NotNull(pack1);
            Assert.NotNull(pack2);
            Assert.Equal(pack1.PackId, pack2.PackId);
            Assert.Equal(pack1.ManifestSha256, pack2.ManifestSha256);

            // Verify only one pack is persisted
            var allPacks = await _packs.ListAsync(CancellationToken.None);
            var matchingPacks = allPacks.Where(p => p.ManifestSha256 == pack1.ManifestSha256).ToList();
            Assert.Single(matchingPacks);

            _output.WriteLine($"Deduped hash: {pack1.ManifestSha256}");
            _output.WriteLine($"Pack count for hash: {matchingPacks.Count}");
        }
        finally
        {
            try { Directory.Delete(testDir1, true); } catch { }
            try { Directory.Delete(testDir2, true); } catch { }
        }
    }

    [Fact]
    public async Task ImportDirectoryAsPackAsync_DifferentContentCreatesNewPack()
    {
        // Arrange: Create two directories with different content
        var testDir1 = Path.Combine(Path.GetTempPath(), "stigforge-diff-1-" + Guid.NewGuid().ToString("N"));
        var testDir2 = Path.Combine(Path.GetTempPath(), "stigforge-diff-2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir1);
        Directory.CreateDirectory(testDir2);

        try
        {
            File.WriteAllText(Path.Combine(testDir1, "benchmark.xml"), "<Benchmark>V1R1</Benchmark>");
            File.WriteAllText(Path.Combine(testDir2, "benchmark.xml"), "<Benchmark>V2R1</Benchmark>");

            var importer = new ContentPackImporter(_paths, _hash, _packs, _controls);

            // Act: Import both directories
            var pack1 = await importer.ImportDirectoryAsPackAsync(testDir1, "TestPack1", "test", CancellationToken.None);
            var pack2 = await importer.ImportDirectoryAsPackAsync(testDir2, "TestPack2", "test", CancellationToken.None);

            // Assert: Different content yields different packs and hashes
            Assert.NotEqual(pack1.PackId, pack2.PackId);
            Assert.NotEqual(pack1.ManifestSha256, pack2.ManifestSha256);

            // Verify two distinct packs are persisted
            var allPacks = await _packs.ListAsync(CancellationToken.None);
            Assert.Equal(2, allPacks.Count);

            _output.WriteLine($"Hash 1: {pack1.ManifestSha256}");
            _output.WriteLine($"Hash 2: {pack2.ManifestSha256}");
        }
        finally
        {
            try { Directory.Delete(testDir1, true); } catch { }
            try { Directory.Delete(testDir2, true); } catch { }
        }
    }

    [Fact]
    public async Task ImportDirectoryAsPackAsync_HashStableAcrossRepeatedImportsOfUnchangedContent()
    {
        // Arrange: Create a test directory
        var testDir = Path.Combine(Path.GetTempPath(), "stigforge-stable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            File.WriteAllText(Path.Combine(testDir, "benchmark.xml"), "<Benchmark>Stable Content</Benchmark>");
            File.WriteAllText(Path.Combine(testDir, "info.txt"), "Metadata");

            var importer = new ContentPackImporter(_paths, _hash, _packs, _controls);

            // Act: Import the same directory twice
            var pack1 = await importer.ImportDirectoryAsPackAsync(testDir, "TestPack", "test", CancellationToken.None);
            var pack2 = await importer.ImportDirectoryAsPackAsync(testDir, "TestPack", "test", CancellationToken.None);

            // Assert: Hash remains identical (stability)
            Assert.Equal(pack1.ManifestSha256, pack2.ManifestSha256);

            // Only one pack in database (deduped)
            var allPacks = await _packs.ListAsync(CancellationToken.None);
            Assert.Single(allPacks);

            _output.WriteLine($"Stable hash: {pack1.ManifestSha256}");
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    private sealed class TestPathBuilder : IPathBuilder
    {
        private readonly string _root;
        public TestPathBuilder(string root) => _root = root;
        public string GetAppDataRoot() => _root;
        public string GetContentPacksRoot() => Path.Combine(_root, "packs");
        public string GetPackRoot(string packId) => Path.Combine(GetContentPacksRoot(), packId);
        public string GetBundleRoot(string bundleId) => Path.Combine(_root, "bundles", bundleId);
        public string GetLogsRoot() => Path.Combine(_root, "logs");
        public string GetImportRoot() => Path.Combine(_root, "import");
        public string GetImportInboxRoot() => Path.Combine(GetImportRoot(), "inbox");
        public string GetImportIndexPath() => Path.Combine(GetImportRoot(), "index.json");
        public string GetToolsRoot() => Path.Combine(_root, "tools");
        public string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts) =>
            Path.Combine(_root, "emass", $"{systemName}_{os}_{role}_{profileName}_{packName}_{ts:yyyyMMddHHmmss}");
    }
}

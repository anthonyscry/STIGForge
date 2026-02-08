using System.IO.Compression;
using Moq;
using STIGForge.Content.Import;
using STIGForge.Content.Models;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Content;

public sealed class ContentPackImporterTests : IDisposable
{
  private readonly string _tempRoot;

  public ContentPackImporterTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-importer-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
      Directory.Delete(_tempRoot, true);
  }

  [Fact]
  public async Task ImportZipAsync_RejectsPathTraversalEntries()
  {
    var zipPath = Path.Combine(_tempRoot, "unsafe-traversal.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var traversal = archive.CreateEntry("../outside.txt");
      await using (var writer = new StreamWriter(traversal.Open()))
      {
        await writer.WriteAsync("blocked");
      }

      var valid = archive.CreateEntry("valid-xccdf.xml");
      await using (var writer = new StreamWriter(valid.Open()))
      {
        await writer.WriteAsync(CreateMinimalXccdf());
      }
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var act = () => importer.ImportZipAsync(zipPath, "unsafe", "unit-test", CancellationToken.None);

    var ex = await Assert.ThrowsAsync<ParsingException>(act);
    Assert.Contains("IMPORT-ARCHIVE-002", ex.Message, StringComparison.Ordinal);

    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Never);
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ControlRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task ImportZipAsync_RejectsArchiveOverEntryCountLimit()
  {
    var zipPath = Path.Combine(_tempRoot, "too-many-entries.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      for (var i = 0; i < 4097; i++)
      {
        var entry = archive.CreateEntry($"entries/file-{i:D4}.txt");
        await using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync("x");
      }
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var act = () => importer.ImportZipAsync(zipPath, "too-many", "unit-test", CancellationToken.None);

    var ex = await Assert.ThrowsAsync<ParsingException>(act);
    Assert.Contains("IMPORT-ARCHIVE-001", ex.Message, StringComparison.Ordinal);

    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Never);
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ControlRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task ImportZipAsync_ImportsValidXccdfBundle()
  {
    var zipPath = Path.Combine(_tempRoot, "valid-bundle.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var xccdf = archive.CreateEntry("bundle-xccdf.xml");
      await using var writer = new StreamWriter(xccdf.Open());
      await writer.WriteAsync(CreateMinimalXccdf());
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var result = await importer.ImportZipAsync(zipPath, "valid-pack", "unit-test", CancellationToken.None);

    Assert.Equal("valid-pack", result.Name);
    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Once);
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.Is<IReadOnlyList<ControlRecord>>(list => list.Count > 0), It.IsAny<CancellationToken>()), Times.Once);
  }

  private ContentPackImporter CreateImporter(out Mock<IContentPackRepository> packsMock, out Mock<IControlRepository> controlsMock)
  {
    var pathsMock = new Mock<IPathBuilder>();
    pathsMock
      .Setup(p => p.GetPackRoot(It.IsAny<string>()))
      .Returns<string>(packId => Path.Combine(_tempRoot, "packs", packId));

    var hashMock = new Mock<IHashingService>();
    hashMock
      .Setup(h => h.Sha256FileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("fake-sha256");

    packsMock = new Mock<IContentPackRepository>();
    packsMock
      .Setup(p => p.ListAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(Array.Empty<ContentPack>());

    controlsMock = new Mock<IControlRepository>();
    controlsMock
      .Setup(c => c.ListControlsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(Array.Empty<ControlRecord>());

    return new ContentPackImporter(pathsMock.Object, hashMock.Object, packsMock.Object, controlsMock.Object);
  }

  private static string CreateMinimalXccdf()
  {
    return """
<Benchmark xmlns="http://checklists.nist.gov/xccdf/1.2" id="test-benchmark">
  <Rule id="SV-123456r1_rule" severity="medium">
    <title>Test Rule</title>
    <description>Test description.</description>
    <check system="manual">
      <check-content>Manually verify this setting.</check-content>
    </check>
    <fixtext>Apply fix.</fixtext>
  </Rule>
</Benchmark>
""";
  }
}

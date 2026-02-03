using System.IO.Compression;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public sealed class ContentPackImporter
{
  private readonly IPathBuilder _paths;
  private readonly IHashingService _hash;
  private readonly IContentPackRepository _packs;
  private readonly IControlRepository _controls;

  public ContentPackImporter(IPathBuilder paths, IHashingService hash, IContentPackRepository packs, IControlRepository controls)
  {
    _paths = paths;
    _hash = hash;
    _packs = packs;
    _controls = controls;
  }

  public async Task<ContentPack> ImportZipAsync(string zipPath, string packName, string sourceLabel, CancellationToken ct)
  {
    var packId = Guid.NewGuid().ToString("n");
    var packRoot = _paths.GetPackRoot(packId);
    var rawRoot = Path.Combine(packRoot, "raw");
    Directory.CreateDirectory(rawRoot);

    ZipFile.ExtractToDirectory(zipPath, rawRoot);

    var zipHash = await _hash.Sha256FileAsync(zipPath, ct);

    var pack = new ContentPack
    {
      PackId = packId,
      Name = packName,
      ImportedAt = DateTimeOffset.Now,
      SourceLabel = sourceLabel,
      HashAlgorithm = "sha256",
      ManifestSha256 = zipHash
    };

    var xccdfFiles = Directory.GetFiles(rawRoot, "*.xml", SearchOption.AllDirectories)
      .Where(p => Path.GetFileName(p).IndexOf("xccdf", StringComparison.OrdinalIgnoreCase) >= 0)
      .Take(10)
      .ToList();

    var parsed = new List<ControlRecord>();
    foreach (var f in xccdfFiles)
    {
      try
      {
        parsed.AddRange(XccdfParser.Parse(f, packName));
      }
      catch
      {
      }
    }

    await _packs.SaveAsync(pack, ct);
    if (parsed.Count > 0)
      await _controls.SaveControlsAsync(pack.PackId, parsed, ct);

    var note = new
    {
      importedZip = Path.GetFileName(zipPath),
      zipHash,
      parsedControls = parsed.Count,
      timestamp = DateTimeOffset.Now
    };

    var notePath = Path.Combine(packRoot, "import_note.json");
    Directory.CreateDirectory(packRoot);
    File.WriteAllText(notePath, JsonSerializer.Serialize(note, new JsonSerializerOptions { WriteIndented = true }));

    return pack;
  }
}

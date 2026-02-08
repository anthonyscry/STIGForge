using System.IO.Compression;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public static class ScapBundleParser
{
  public static IReadOnlyList<ControlRecord> Parse(string bundleZipPath, string packName)
  {
    if (!File.Exists(bundleZipPath))
      throw new FileNotFoundException("SCAP bundle ZIP not found", bundleZipPath);

    var tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-scap-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);

    try
    {
      ZipFile.ExtractToDirectory(bundleZipPath, tempRoot);

      var xccdfFiles = Directory
        .GetFiles(tempRoot, "*.xml", SearchOption.AllDirectories)
        .Where(p => Path.GetFileName(p).IndexOf("xccdf", StringComparison.OrdinalIgnoreCase) >= 0)
        .ToList();

      var records = new List<ControlRecord>();
      foreach (var xccdf in xccdfFiles)
      {
        records.AddRange(XccdfParser.Parse(xccdf, packName));
      }

      return records;
    }
    finally
    {
      try { Directory.Delete(tempRoot, true); } catch { }
    }
  }
}

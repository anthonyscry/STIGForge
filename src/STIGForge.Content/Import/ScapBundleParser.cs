using System.IO.Compression;
using STIGForge.Content.Models;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public static class ScapBundleParser
{
  private const int MaxArchiveEntryCount = 4096;
  private const long MaxExtractedBytes = 512L * 1024L * 1024L;

  public static IReadOnlyList<ControlRecord> Parse(string bundleZipPath, string packName)
  {
    if (!File.Exists(bundleZipPath))
      throw new FileNotFoundException("SCAP bundle ZIP not found", bundleZipPath);

    var tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-scap-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);

    try
    {
      ExtractZipSafely(bundleZipPath, tempRoot);

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
      try { Directory.Delete(tempRoot, true); }
      catch (Exception ex)
      {
        System.Diagnostics.Trace.TraceWarning("Cleanup failed: " + ex.Message);
      }
    }
  }

  private static void ExtractZipSafely(string zipPath, string destinationRoot)
  {
    var destinationRootFullPath = Path.GetFullPath(destinationRoot);
    var destinationRootPrefix = destinationRootFullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
      ? destinationRootFullPath
      : destinationRootFullPath + Path.DirectorySeparatorChar;

    using var archive = ZipFile.OpenRead(zipPath);

    if (archive.Entries.Count > MaxArchiveEntryCount)
      throw new ParsingException($"[SCAP-ARCHIVE-001] Archive contains {archive.Entries.Count} entries, exceeding the maximum allowed {MaxArchiveEntryCount}.");

    long extractedBytes = 0;

    foreach (var entry in archive.Entries)
    {
      var destinationPath = Path.GetFullPath(Path.Combine(destinationRootFullPath, entry.FullName));
      var isWithinRoot = destinationPath.StartsWith(destinationRootPrefix, StringComparison.OrdinalIgnoreCase)
        || string.Equals(destinationPath, destinationRootFullPath, StringComparison.OrdinalIgnoreCase);
      if (!isWithinRoot)
        throw new ParsingException($"[SCAP-ARCHIVE-002] Archive entry '{entry.FullName}' resolves outside extraction root and was rejected.");

      var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal)
        || entry.FullName.EndsWith("\\", StringComparison.Ordinal);
      if (isDirectory)
      {
        Directory.CreateDirectory(destinationPath);
        continue;
      }

      extractedBytes += entry.Length;
      if (extractedBytes > MaxExtractedBytes)
        throw new ParsingException($"[SCAP-ARCHIVE-003] Archive expanded size exceeds {MaxExtractedBytes} bytes and was rejected.");

      var directory = Path.GetDirectoryName(destinationPath);
      if (!string.IsNullOrWhiteSpace(directory))
        Directory.CreateDirectory(directory);

      using var entryStream = entry.Open();
      using var outputStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
      entryStream.CopyTo(outputStream);
    }
  }
}

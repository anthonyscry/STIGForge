using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace STIGForge.Cli.Commands;

public sealed class SupportBundleRequest
{
  public string OutputDirectory { get; set; } = string.Empty;
  public string AppDataRoot { get; set; } = string.Empty;
  public string? BundleRoot { get; set; }
  public bool IncludeDatabase { get; set; }
  public bool IncludeSensitive { get; set; }
  public string? SensitiveReason { get; set; }
  public int MaxLogFiles { get; set; } = 20;
}

public sealed class SupportBundleResult
{
  public string BundleZipPath { get; init; } = string.Empty;
  public string ManifestPath { get; init; } = string.Empty;
  public int FileCount { get; init; }
  public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class SupportBundleBuilder
{
  private static readonly string[] SensitivePathFragments =
  {
    "secret",
    "credential",
    "password",
    "token",
    "apikey",
    ".env",
    "id_rsa",
    "private"
  };

  private static readonly string[] DiagnosticExtensions =
  {
    ".json", ".csv", ".txt", ".log", ".xml", ".ckl", ".md"
  };

  public SupportBundleResult Create(SupportBundleRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.OutputDirectory)) throw new ArgumentException("OutputDirectory is required.");
    if (string.IsNullOrWhiteSpace(request.AppDataRoot)) throw new ArgumentException("AppDataRoot is required.");

    var outputDirectory = Path.GetFullPath(request.OutputDirectory);
    Directory.CreateDirectory(outputDirectory);

    var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
    var token = Guid.NewGuid().ToString("n").Substring(0, 8);
    var archiveName = $"support-bundle-{stamp}-{token}";
    var stagingRoot = Path.Combine(outputDirectory, archiveName);
    var zipPath = Path.Combine(outputDirectory, archiveName + ".zip");

    var warnings = new List<string>();
    var files = new List<BundleFile>();

    Directory.CreateDirectory(stagingRoot);
    try
    {
      CollectLogs(request, stagingRoot, files, warnings);
      CollectBundleDiagnostics(request, stagingRoot, files, warnings);
      CollectDatabase(request, stagingRoot, files, warnings);
      WriteMetadata(request, stagingRoot, files, warnings);

      var manifestPath = WriteManifest(request, stagingRoot, files, warnings);
      var manifestExportPath = Path.Combine(outputDirectory, archiveName + "-manifest.json");
      File.Copy(manifestPath, manifestExportPath, true);

      if (File.Exists(zipPath)) File.Delete(zipPath);
      ZipFile.CreateFromDirectory(stagingRoot, zipPath, CompressionLevel.Optimal, false);

      return new SupportBundleResult
      {
        BundleZipPath = zipPath,
        ManifestPath = manifestExportPath,
        FileCount = files.Count,
        Warnings = warnings
      };
    }
    finally
    {
      if (Directory.Exists(stagingRoot))
      {
        try { Directory.Delete(stagingRoot, true); }
        catch { }
      }
    }
  }

  private static void CollectLogs(SupportBundleRequest request, string stagingRoot, List<BundleFile> files, List<string> warnings)
  {
    var sourceLogsDir = Path.Combine(request.AppDataRoot, "logs");
    if (!Directory.Exists(sourceLogsDir))
    {
      warnings.Add("Logs directory not found: " + sourceLogsDir);
      return;
    }

    var maxLogs = request.MaxLogFiles < 1 ? 1 : request.MaxLogFiles;
    var logs = new DirectoryInfo(sourceLogsDir)
      .GetFiles("*", SearchOption.TopDirectoryOnly)
      .OrderByDescending(f => f.LastWriteTimeUtc)
      .Take(maxLogs)
      .ToList();

    foreach (var log in logs)
    {
      CopyFile(log.FullName, Path.Combine(stagingRoot, "logs", log.Name), files, warnings);
    }
  }

  private static void CollectBundleDiagnostics(SupportBundleRequest request, string stagingRoot, List<BundleFile> files, List<string> warnings)
  {
    if (string.IsNullOrWhiteSpace(request.BundleRoot)) return;

    var bundleRoot = Path.GetFullPath(request.BundleRoot);
    if (!Directory.Exists(bundleRoot))
    {
      warnings.Add("Bundle directory not found: " + bundleRoot);
      return;
    }

    CopyMatchingFiles(request, Path.Combine(bundleRoot, "Manifest"), Path.Combine(stagingRoot, "bundle", "Manifest"), files, warnings);
    CopyMatchingFiles(request, Path.Combine(bundleRoot, "Reports"), Path.Combine(stagingRoot, "bundle", "Reports"), files, warnings);
    CopyMatchingFiles(request, Path.Combine(bundleRoot, "Apply"), Path.Combine(stagingRoot, "bundle", "Apply"), files, warnings);
    CopyMatchingFiles(request, Path.Combine(bundleRoot, "Verify"), Path.Combine(stagingRoot, "bundle", "Verify"), files, warnings);
    CopyMatchingFiles(request, Path.Combine(bundleRoot, "Manual"), Path.Combine(stagingRoot, "bundle", "Manual"), files, warnings);
  }

  private static void CollectDatabase(SupportBundleRequest request, string stagingRoot, List<BundleFile> files, List<string> warnings)
  {
    if (!request.IncludeDatabase) return;

    if (!request.IncludeSensitive)
    {
      warnings.Add("Database excluded: --include-db requires --include-sensitive and a specific reason.");
      return;
    }

    var dbPath = Path.Combine(request.AppDataRoot, "data", "stigforge.db");
    if (!File.Exists(dbPath))
    {
      warnings.Add("Database file not found: " + dbPath);
      return;
    }

    CopyFile(dbPath, Path.Combine(stagingRoot, "data", "stigforge.db"), files, warnings);
  }

  private static void CopyMatchingFiles(SupportBundleRequest request, string sourceRoot, string destinationRoot, List<BundleFile> files, List<string> warnings)
  {
    if (!Directory.Exists(sourceRoot)) return;

    var sourceRootPath = Path.GetFullPath(sourceRoot);
    foreach (var sourcePath in Directory.GetFiles(sourceRootPath, "*", SearchOption.AllDirectories))
    {
      if (!ShouldInclude(sourcePath)) continue;
      if (!request.IncludeSensitive && IsSensitivePath(sourcePath)) continue;

      var relative = Path.GetRelativePath(sourceRootPath, sourcePath);
      var destinationPath = Path.Combine(destinationRoot, relative);
      CopyFile(sourcePath, destinationPath, files, warnings);
    }
  }

  private static bool ShouldInclude(string sourcePath)
  {
    var extension = Path.GetExtension(sourcePath);
    return DiagnosticExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
  }

  private static bool IsSensitivePath(string sourcePath)
  {
    foreach (var fragment in SensitivePathFragments)
    {
      if (sourcePath.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    return false;
  }

  private static void CopyFile(string sourcePath, string destinationPath, List<BundleFile> files, List<string> warnings)
  {
    try
    {
      var destinationDir = Path.GetDirectoryName(destinationPath);
      if (!string.IsNullOrWhiteSpace(destinationDir)) Directory.CreateDirectory(destinationDir);

      File.Copy(sourcePath, destinationPath, true);
      var info = new FileInfo(destinationPath);
      files.Add(new BundleFile
      {
        ArchivePath = destinationPath,
        OriginalPath = sourcePath,
        SizeBytes = info.Length,
        Sha256 = ComputeSha256(destinationPath)
      });
    }
    catch (Exception ex)
    {
      warnings.Add($"Failed to copy '{sourcePath}': {ex.Message}");
    }
  }

  private static void WriteMetadata(SupportBundleRequest request, string stagingRoot, List<BundleFile> files, List<string> warnings)
  {
    var metadataDir = Path.Combine(stagingRoot, "metadata");
    Directory.CreateDirectory(metadataDir);

    var systemInfoPath = Path.Combine(metadataDir, "system-info.json");
    var metadata = new
    {
      collectedAtUtc = DateTimeOffset.UtcNow,
      appDataRoot = request.IncludeSensitive ? request.AppDataRoot : "[redacted]",
      bundleRoot = request.IncludeSensitive ? request.BundleRoot : "[redacted]",
      includeDatabase = request.IncludeDatabase,
      includeSensitive = request.IncludeSensitive,
      sensitiveReason = request.IncludeSensitive ? request.SensitiveReason : null,
      maxLogFiles = request.MaxLogFiles,
      machineName = request.IncludeSensitive ? Environment.MachineName : "[redacted]",
      userName = request.IncludeSensitive ? Environment.UserName : "[redacted]",
      osDescription = RuntimeInformation.OSDescription,
      osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
      processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
      frameworkDescription = RuntimeInformation.FrameworkDescription,
      commandLine = request.IncludeSensitive ? Environment.CommandLine : "[redacted]"
    };

    File.WriteAllText(systemInfoPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    files.Add(new BundleFile
    {
      ArchivePath = systemInfoPath,
      OriginalPath = systemInfoPath,
      SizeBytes = new FileInfo(systemInfoPath).Length,
      Sha256 = ComputeSha256(systemInfoPath)
    });

    if (warnings.Count > 0)
    {
      var warningsPath = Path.Combine(metadataDir, "warnings.txt");
      File.WriteAllLines(warningsPath, warnings);
      files.Add(new BundleFile
      {
        ArchivePath = warningsPath,
        OriginalPath = warningsPath,
        SizeBytes = new FileInfo(warningsPath).Length,
        Sha256 = ComputeSha256(warningsPath)
      });
    }
  }

  private static string WriteManifest(SupportBundleRequest request, string stagingRoot, List<BundleFile> files, List<string> warnings)
  {
    var manifestPath = Path.Combine(stagingRoot, "metadata", "support-bundle-manifest.json");
    var payload = new
    {
      generatedAtUtc = DateTimeOffset.UtcNow,
      totalFiles = files.Count,
      warnings = warnings,
      files = files.Select(f => new
      {
        path = Path.GetRelativePath(stagingRoot, f.ArchivePath),
        source = request.IncludeSensitive ? f.OriginalPath : "[redacted]",
        sizeBytes = f.SizeBytes,
        sha256 = f.Sha256
      }).OrderBy(f => f.path, StringComparer.OrdinalIgnoreCase).ToList()
    };

    File.WriteAllText(manifestPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    return manifestPath;
  }

  private static string ComputeSha256(string filePath)
  {
    using var stream = File.OpenRead(filePath);
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(stream);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  private sealed class BundleFile
  {
    public string ArchivePath { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
  }
}

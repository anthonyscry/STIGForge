using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.Telemetry;

/// <summary>
/// Creates portable diagnostic bundles for offline support scenarios.
/// Aggregates logs, traces, bundle artifacts, and system information into a ZIP archive.
/// </summary>
public sealed class DebugBundleExporter
{
  private readonly IPathBuilder _paths;

  /// <summary>
  /// Initializes a new instance of the <see cref="DebugBundleExporter"/> class.
  /// </summary>
  /// <param name="paths">The path builder for resolving log and data directories.</param>
  public DebugBundleExporter(IPathBuilder paths)
  {
    _paths = paths ?? throw new ArgumentNullException(nameof(paths));
  }

  /// <summary>
  /// Exports a debug bundle containing diagnostic artifacts to a timestamped ZIP file.
  /// </summary>
  /// <param name="request">The export request specifying bundle root, log days, and reason.</param>
  /// <returns>The result containing output path, file count, and creation timestamp.</returns>
  public DebugBundleResult ExportBundle(DebugBundleRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var logsRoot = _paths.GetLogsRoot();
    var exportsDir = Path.Combine(logsRoot, "exports");
    Directory.CreateDirectory(exportsDir);

    var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
    var token = Guid.NewGuid().ToString("N").Substring(0, 8);
    var archiveName = $"stigforge-debug-{timestamp}-{token}.zip";
    var outputPath = Path.Combine(exportsDir, archiveName);

    var fileCount = 0;

    using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
    {
      AddLogsToArchive(archive, logsRoot, request.IncludeDaysOfLogs, ref fileCount);
      AddBundleLogsToArchive(archive, request.BundleRoot, ref fileCount);
      AddTracesToArchive(archive, logsRoot, ref fileCount);
      AddSystemInfoToArchive(archive, ref fileCount);
      AddManifestToArchive(archive, request, ref fileCount);
    }

    return new DebugBundleResult
    {
      OutputPath = outputPath,
      FileCount = fileCount,
      CreatedAt = DateTimeOffset.UtcNow
    };
  }

  /// <summary>
  /// Adds application logs from the last N days to the archive.
  /// Filters by file modification time.
  /// </summary>
  private void AddLogsToArchive(ZipArchive archive, string logsRoot, int days, ref int fileCount)
  {
    if (!Directory.Exists(logsRoot))
    {
      return;
    }

    var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, days));

    try
    {
      foreach (var file in Directory.GetFiles(logsRoot, "*.log", SearchOption.AllDirectories))
      {
        try
        {
          var info = new FileInfo(file);
          if (info.LastWriteTimeUtc >= cutoff.UtcDateTime)
          {
            var relativePath = Path.GetRelativePath(logsRoot, file);
            var entryName = $"logs/{relativePath.Replace('\\', '/')}";
            archive.CreateEntryFromFile(file, entryName);
            fileCount++;
          }
        }
        catch (Exception)
        {
          // Skip files that cannot be accessed
        }
      }
    }
    catch (Exception)
    {
      // Skip logs directory if inaccessible
    }
  }

  /// <summary>
  /// Adds bundle-specific artifacts (Apply/Logs, Verify/*.json, Apply/apply_run.json) to the archive.
  /// </summary>
  private void AddBundleLogsToArchive(ZipArchive archive, string? bundleRoot, ref int fileCount)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot) || !Directory.Exists(bundleRoot))
    {
      return;
    }

    // Add Apply/Logs
    var applyLogs = Path.Combine(bundleRoot, "Apply", "Logs");
    AddDirectoryToArchive(archive, applyLogs, "bundle/Apply/Logs", ref fileCount);

    // Add Verify/*.json
    var verifyRoot = Path.Combine(bundleRoot, "Verify");
    if (Directory.Exists(verifyRoot))
    {
      try
      {
        foreach (var file in Directory.GetFiles(verifyRoot, "*.json", SearchOption.AllDirectories))
        {
          try
          {
            var relativePath = Path.GetRelativePath(verifyRoot, file);
            var entryName = $"bundle/Verify/{relativePath.Replace('\\', '/')}";
            archive.CreateEntryFromFile(file, entryName);
            fileCount++;
          }
          catch (Exception)
          {
            // Skip files that cannot be accessed
          }
        }
      }
      catch (Exception)
      {
        // Skip Verify directory if inaccessible
      }
    }

    // Add Apply/apply_run.json
    var applyRun = Path.Combine(bundleRoot, "Apply", "apply_run.json");
    if (File.Exists(applyRun))
    {
      try
      {
        archive.CreateEntryFromFile(applyRun, "bundle/Apply/apply_run.json");
        fileCount++;
      }
      catch (Exception)
      {
        // Skip if file cannot be accessed
      }
    }
  }

  /// <summary>
  /// Adds traces.json to the archive if it exists.
  /// </summary>
  private void AddTracesToArchive(ZipArchive archive, string logsRoot, ref int fileCount)
  {
    var tracesPath = Path.Combine(logsRoot, "traces.json");
    if (!File.Exists(tracesPath))
    {
      return;
    }

    try
    {
      archive.CreateEntryFromFile(tracesPath, "traces/traces.json");
      fileCount++;
    }
    catch (Exception)
    {
      // Skip if file cannot be accessed
    }
  }

  /// <summary>
  /// Adds system-info.json with machine name, OS, runtime, and timestamp.
  /// </summary>
  private void AddSystemInfoToArchive(ZipArchive archive, ref int fileCount)
  {
    var entry = archive.CreateEntry("system-info.json");
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream);

    var info = new
    {
      MachineName = Environment.MachineName,
      UserName = Environment.UserName,
      OSDescription = RuntimeInformation.OSDescription,
      OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
      ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
      FrameworkDescription = RuntimeInformation.FrameworkDescription,
      Timestamp = DateTimeOffset.UtcNow.ToString("o"),
      ProcessId = Environment.ProcessId
    };

    writer.Write(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
    fileCount++;
  }

  /// <summary>
  /// Adds manifest.json with export metadata (timestamp, version, reason, included artifacts).
  /// </summary>
  private void AddManifestToArchive(ZipArchive archive, DebugBundleRequest request, ref int fileCount)
  {
    var entry = archive.CreateEntry("manifest.json");
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream);

    var manifest = new
    {
      ExportedAt = DateTimeOffset.UtcNow.ToString("o"),
      BundleRoot = request.BundleRoot,
      IncludeDaysOfLogs = request.IncludeDaysOfLogs,
      Reason = request.ExportReason,
      STIGForgeVersion = typeof(DebugBundleExporter).Assembly.GetName().Version?.ToString() ?? "unknown"
    };

    writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    fileCount++;
  }

  /// <summary>
  /// Recursively adds all files from a directory to the archive.
  /// </summary>
  private static void AddDirectoryToArchive(ZipArchive archive, string directoryPath, string entryPrefix, ref int fileCount)
  {
    if (!Directory.Exists(directoryPath))
    {
      return;
    }

    try
    {
      foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
      {
        try
        {
          var relativePath = Path.GetRelativePath(directoryPath, file);
          var entryName = $"{entryPrefix}/{relativePath.Replace('\\', '/')}";
          archive.CreateEntryFromFile(file, entryName);
          fileCount++;
        }
        catch (Exception)
        {
          // Skip files that cannot be accessed
        }
      }
    }
    catch (Exception)
    {
      // Skip directory if inaccessible
    }
  }
}

/// <summary>
/// Request model for creating a debug export bundle.
/// </summary>
public sealed class DebugBundleRequest
{
  /// <summary>
  /// Gets or sets the optional bundle root path for including bundle-specific artifacts.
  /// </summary>
  public string? BundleRoot { get; set; }

  /// <summary>
  /// Gets or sets the number of days of logs to include. Default is 7.
  /// </summary>
  public int IncludeDaysOfLogs { get; set; } = 7;

  /// <summary>
  /// Gets or sets the optional export reason for the manifest.
  /// </summary>
  public string? ExportReason { get; set; }
}

/// <summary>
/// Result model for debug export bundle creation.
/// </summary>
public sealed class DebugBundleResult
{
  /// <summary>
  /// Gets or sets the full path to the created ZIP archive.
  /// </summary>
  public string OutputPath { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the number of files included in the archive.
  /// </summary>
  public int FileCount { get; set; }

  /// <summary>
  /// Gets or sets the timestamp when the bundle was created.
  /// </summary>
  public DateTimeOffset CreatedAt { get; set; }
}

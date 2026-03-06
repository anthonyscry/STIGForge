using System.IO.Compression;
using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.Workflow;

public sealed class LocalSetupValidator
{
  private readonly IPathBuilder _paths;

  public LocalSetupValidator(IPathBuilder paths)
  {
    _paths = paths;
  }

  public string ValidateRequiredTools()
  {
    return ValidateRequiredTools(null);
  }

  public string ValidateRequiredTools(string? evaluateStigToolRoot)
  {
    return ValidateRequiredTools(evaluateStigToolRoot, null);
  }

  public string ValidateRequiredTools(string? evaluateStigToolRoot, string? importRoot)
  {
    var candidates = ResolveCandidates(evaluateStigToolRoot);

    foreach (var candidate in candidates)
    {
      if (!Directory.Exists(candidate))
        continue;

      var scriptPath = Path.Combine(candidate, "Evaluate-STIG.ps1");
      if (File.Exists(scriptPath))
        return candidate;
    }

    if (TryStageEvaluateStigFromImport(importRoot, candidates, out var stagedPath))
      return stagedPath;

    throw new InvalidOperationException(
      "Required Evaluate-STIG tool path is missing or invalid. " +
      $"Expected Evaluate-STIG.ps1 under {string.Join(" or ", candidates.Select(static c => $"'{c}'"))}.");
  }

  private IReadOnlyList<string> ResolveCandidates(string? evaluateStigToolRoot)
  {
    if (!string.IsNullOrWhiteSpace(evaluateStigToolRoot))
      return new[] { evaluateStigToolRoot };

    var toolsRoot = _paths.GetToolsRoot();
    return new[]
    {
      Path.Combine(toolsRoot, "Evaluate-STIG", "Evaluate-STIG"),
      Path.Combine(toolsRoot, "Evaluate-STIG")
    };
  }

  private bool TryStageEvaluateStigFromImport(string? importRoot, IReadOnlyList<string> candidates, out string stagedPath)
  {
    stagedPath = string.Empty;

    if (string.IsNullOrWhiteSpace(importRoot) || !Directory.Exists(importRoot))
      return false;

    var evaluateStigZips = Directory.EnumerateFiles(importRoot, "*.zip", SearchOption.AllDirectories)
      .Where(z => z.Contains("evaluate-stig", StringComparison.OrdinalIgnoreCase) || 
                  z.Contains("evaluatestig", StringComparison.OrdinalIgnoreCase))
      .OrderBy(z => z, StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (evaluateStigZips.Count == 0)
      return false;

    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    try
    {
      foreach (var zipPath in evaluateStigZips)
      {
        try
        {
          var extractDir = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(zipPath));
          Directory.CreateDirectory(extractDir);
          ExtractZipSafely(zipPath, extractDir);

          var scriptFiles = Directory.EnumerateFiles(extractDir, "Evaluate-STIG.ps1", SearchOption.AllDirectories)
            .OrderBy(f => f.Length)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

          if (scriptFiles.Count == 0)
            continue;

          var scriptPath = scriptFiles[0];
          var scriptDir = Path.GetDirectoryName(scriptPath);
          if (string.IsNullOrWhiteSpace(scriptDir))
            continue;

          var targetCandidate = candidates[0];
          Directory.CreateDirectory(targetCandidate);

          CopyDirectory(scriptDir, targetCandidate);

          var copiedScriptPath = Path.Combine(targetCandidate, "Evaluate-STIG.ps1");
          if (File.Exists(copiedScriptPath))
          {
            stagedPath = targetCandidate;
            return true;
          }
        }
        catch (Exception)
        {
          continue;
        }
      }

      return false;
    }
    finally
    {
      try
      {
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
      catch (Exception)
      {
      }
    }
  }

  private void CopyDirectory(string sourceDir, string targetDir)
  {
    Directory.CreateDirectory(targetDir);

    foreach (var file in Directory.EnumerateFiles(sourceDir))
    {
      var fileName = Path.GetFileName(file);
      var targetPath = Path.Combine(targetDir, fileName);
      File.Copy(file, targetPath, true);
    }

    foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
    {
      var dirName = Path.GetFileName(subDir);
      var targetSubDir = Path.Combine(targetDir, dirName);
      CopyDirectory(subDir, targetSubDir);
    }
  }

  private static void ExtractZipSafely(string zipPath, string destinationRoot)
  {
    using var archive = ZipFile.OpenRead(zipPath);
    var destinationFullRoot = Path.GetFullPath(destinationRoot);
    const int maxEntries = 4096;
    const long maxExtractedBytes = 512L * 1024 * 1024;
    long extractedBytes = 0;
    var count = 0;

    foreach (var entry in archive.Entries)
    {
      count++;
      if (count > maxEntries)
        throw new InvalidDataException($"Archive entry limit exceeded: {zipPath}");

      var destinationPath = Path.GetFullPath(Path.Combine(destinationFullRoot, entry.FullName));
      if (!destinationPath.StartsWith(destinationFullRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException($"Archive entry escapes destination root: {entry.FullName}");

      if (string.IsNullOrEmpty(entry.Name))
      {
        Directory.CreateDirectory(destinationPath);
        continue;
      }

      extractedBytes += entry.Length;
      if (extractedBytes > maxExtractedBytes)
        throw new InvalidDataException($"Extracted archive size exceeds limit: {zipPath}");

      var parentDir = Path.GetDirectoryName(destinationPath);
      if (!string.IsNullOrWhiteSpace(parentDir))
        Directory.CreateDirectory(parentDir);

      entry.ExtractToFile(destinationPath, overwrite: true);
    }
  }
}

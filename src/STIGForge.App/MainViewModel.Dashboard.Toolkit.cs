using System.IO;
using System.IO.Compression;

namespace STIGForge.App;

public partial class MainViewModel
{
  private async Task<bool> TryActivateToolkitAsync(bool userInitiated, CancellationToken ct)
  {
    var sourceRoot = string.IsNullOrWhiteSpace(LocalToolkitRoot)
      ? ResolveDefaultToolkitRoot()
      : LocalToolkitRoot.Trim();
    LocalToolkitRoot = sourceRoot;

    if (!Directory.Exists(sourceRoot))
    {
      ToolkitActivationStatus = "Toolkit root not found: " + sourceRoot;
      if (userInitiated)
        StatusText = ToolkitActivationStatus;
      return false;
    }

    ToolkitActivationStatus = "Activating toolkit from " + sourceRoot + "...";

    ToolkitActivationResult result;
    try
    {
      result = await Task.Run(() => ActivateToolkitFromSource(sourceRoot), ct);
    }
    catch (Exception ex)
    {
      ToolkitActivationStatus = "Toolkit activation failed: " + ex.Message;
      if (userInitiated)
        StatusText = ToolkitActivationStatus;
      return false;
    }

    if (!string.IsNullOrWhiteSpace(result.EvaluateStigRoot))
      EvaluateStigRoot = result.EvaluateStigRoot;

    if (!string.IsNullOrWhiteSpace(result.ScapCommandPath))
      ScapCommandPath = result.ScapCommandPath;

    if (!string.IsNullOrWhiteSpace(result.PowerStigModulePath))
      PowerStigModulePath = result.PowerStigModulePath;

    if (string.IsNullOrWhiteSpace(ScapArgs))
      ScapArgs = string.Empty;

    if (string.IsNullOrWhiteSpace(ScapLabel))
      ScapLabel = "DISA SCAP";

    var scannerReady = !string.IsNullOrWhiteSpace(EvaluateStigRoot) || !string.IsNullOrWhiteSpace(ScapCommandPath);
    var notes = result.Notes.Count == 0
      ? string.Empty
      : " " + string.Join(" | ", result.Notes.Take(3));

    ToolkitActivationStatus = scannerReady
      ? "Toolkit activation complete." + notes
      : "Toolkit activation completed with missing scanners." + notes;

    if (userInitiated || !scannerReady)
      StatusText = ToolkitActivationStatus;

    if (scannerReady)
      GuidedNextAction = "Scanner tooling configured. Continue with Verify or full orchestration.";

    return scannerReady;
  }

  private ToolkitActivationResult ActivateToolkitFromSource(string sourceRoot)
  {
    var result = new ToolkitActivationResult
    {
      SourceRoot = sourceRoot,
      InstallRoot = Path.Combine(_paths.GetAppDataRoot(), "tools")
    };

    Directory.CreateDirectory(result.InstallRoot);

    result.EvaluateStigRoot = ResolveEvaluateStigRoot(sourceRoot, result.InstallRoot, result.Notes);
    result.ScapCommandPath = ResolveScapCommandPath(sourceRoot, result.InstallRoot, result.Notes);
    result.PowerStigModulePath = ResolvePowerStigModulePath(sourceRoot, result.InstallRoot, result.Notes);

    return result;
  }

  private static string ResolveDefaultToolkitRoot()
  {
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
      var dir = new DirectoryInfo(start);
      while (dir != null)
      {
        var candidate = Path.Combine(dir.FullName, "STIG_SCAP");
        if (Directory.Exists(candidate))
          return candidate;

        dir = dir.Parent;
      }
    }

    return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "STIG_SCAP"));
  }

  private static string? ResolveEvaluateStigRoot(string sourceRoot, string installRoot, List<string> notes)
  {
    var scriptPath = FindFirstFileByName(sourceRoot, "Evaluate-STIG.ps1");
    if (!string.IsNullOrWhiteSpace(scriptPath))
    {
      notes.Add("Evaluate-STIG script located in source root.");
      return Path.GetDirectoryName(scriptPath);
    }

    var cachedRoot = Path.Combine(installRoot, "Evaluate-STIG");
    scriptPath = FindFirstFileByName(cachedRoot, "Evaluate-STIG.ps1");
    if (!string.IsNullOrWhiteSpace(scriptPath))
    {
      notes.Add("Evaluate-STIG resolved from managed tools cache.");
      return Path.GetDirectoryName(scriptPath);
    }

    var archive = FindArchiveCandidate(sourceRoot, "evaluate-stig");
    if (archive == null)
    {
      notes.Add("Evaluate-STIG archive not found.");
      return null;
    }

    var extractRoot = Path.Combine(installRoot, "Evaluate-STIG");
    try
    {
      ExtractArchiveWithNestedZips(archive, extractRoot, nestedPasses: 1, notes);
    }
    catch (Exception ex)
    {
      notes.Add("Evaluate-STIG extraction failed: " + ex.Message);
      return null;
    }

    scriptPath = FindFirstFileByName(extractRoot, "Evaluate-STIG.ps1");
    if (!string.IsNullOrWhiteSpace(scriptPath))
    {
      notes.Add("Evaluate-STIG extracted from " + Path.GetFileName(archive) + ".");
      return Path.GetDirectoryName(scriptPath);
    }

    notes.Add("Evaluate-STIG.ps1 not found after extraction.");
    return null;
  }

  private static string? ResolveScapCommandPath(string sourceRoot, string installRoot, List<string> notes)
  {
    var commandPath = FindFirstFileByName(sourceRoot, "cscc.exe")
      ?? FindFirstFileByName(sourceRoot, "scc.exe");
    if (!string.IsNullOrWhiteSpace(commandPath))
    {
      notes.Add("SCC executable located in source root.");
      return commandPath;
    }

    var cachedRoot = Path.Combine(installRoot, "SCC");
    commandPath = FindFirstFileByName(cachedRoot, "cscc.exe")
      ?? FindFirstFileByName(cachedRoot, "scc.exe");
    if (!string.IsNullOrWhiteSpace(commandPath))
    {
      notes.Add("SCC resolved from managed tools cache.");
      return commandPath;
    }

    var archive = FindArchiveCandidate(sourceRoot, "scc");
    if (archive == null)
    {
      notes.Add("SCC archive not found.");
      return null;
    }

    var extractRoot = Path.Combine(installRoot, "SCC");
    try
    {
      ExtractArchiveWithNestedZips(archive, extractRoot, nestedPasses: 2, notes);
    }
    catch (Exception ex)
    {
      notes.Add("SCC extraction failed: " + ex.Message);
      return null;
    }

    commandPath = FindFirstFileByName(extractRoot, "cscc.exe")
      ?? FindFirstFileByName(extractRoot, "scc.exe");
    if (!string.IsNullOrWhiteSpace(commandPath))
    {
      notes.Add("SCC extracted from " + Path.GetFileName(archive) + ".");
      return commandPath;
    }

    notes.Add("cscc.exe/scc.exe not found after extraction.");
    return null;
  }

  private static string? ResolvePowerStigModulePath(string sourceRoot, string installRoot, List<string> notes)
  {
    var modulePath = FindFirstFileByName(sourceRoot, "PowerSTIG.psd1")
      ?? FindFirstFileByName(sourceRoot, "PowerStig.psd1");
    if (!string.IsNullOrWhiteSpace(modulePath))
    {
      notes.Add("PowerSTIG module located in source root.");
      return modulePath;
    }

    var cachedRoot = Path.Combine(installRoot, "PowerSTIG");
    modulePath = FindFirstFileByName(cachedRoot, "PowerSTIG.psd1")
      ?? FindFirstFileByName(cachedRoot, "PowerStig.psd1");
    if (!string.IsNullOrWhiteSpace(modulePath))
    {
      notes.Add("PowerSTIG resolved from managed tools cache.");
      return modulePath;
    }

    var archive = FindArchiveCandidate(sourceRoot, "powerstig");
    if (archive == null)
      return null;

    var extractRoot = Path.Combine(installRoot, "PowerSTIG");
    try
    {
      ExtractArchiveWithNestedZips(archive, extractRoot, nestedPasses: 1, notes);
    }
    catch (Exception ex)
    {
      notes.Add("PowerSTIG extraction failed: " + ex.Message);
      return null;
    }

    modulePath = FindFirstFileByName(extractRoot, "PowerSTIG.psd1")
      ?? FindFirstFileByName(extractRoot, "PowerStig.psd1");
    if (!string.IsNullOrWhiteSpace(modulePath))
    {
      notes.Add("PowerSTIG extracted from " + Path.GetFileName(archive) + ".");
      return modulePath;
    }

    return null;
  }

  private static void ExtractArchiveWithNestedZips(string archivePath, string destinationRoot, int nestedPasses, List<string> notes)
  {
    if (Directory.Exists(destinationRoot))
      Directory.Delete(destinationRoot, recursive: true);

    Directory.CreateDirectory(destinationRoot);
    ExtractZipSafely(archivePath, destinationRoot);

    for (var pass = 0; pass < nestedPasses; pass++)
    {
      var nestedArchives = EnumerateFilesSafe(destinationRoot, "*.zip", SearchOption.AllDirectories).ToList();
      if (nestedArchives.Count == 0)
        break;

      foreach (var nestedArchive in nestedArchives)
      {
        var nestedExtractRoot = Path.Combine(
          Path.GetDirectoryName(nestedArchive) ?? destinationRoot,
          Path.GetFileNameWithoutExtension(nestedArchive));

        try
        {
          if (!Directory.Exists(nestedExtractRoot) || !Directory.EnumerateFileSystemEntries(nestedExtractRoot).Any())
          {
            Directory.CreateDirectory(nestedExtractRoot);
            ExtractZipSafely(nestedArchive, nestedExtractRoot);
          }
        }
        catch (Exception ex)
        {
          notes.Add("Skipped nested archive " + Path.GetFileName(nestedArchive) + ": " + ex.Message);
        }
      }
    }
  }

  private static void ExtractZipSafely(string zipPath, string destinationRoot)
  {
    var destinationFullPath = Path.GetFullPath(destinationRoot);
    var destinationPrefix = destinationFullPath.EndsWith(Path.DirectorySeparatorChar)
      ? destinationFullPath
      : destinationFullPath + Path.DirectorySeparatorChar;

    using var archive = ZipFile.OpenRead(zipPath);
    foreach (var entry in archive.Entries)
    {
      var destinationPath = Path.GetFullPath(Path.Combine(destinationFullPath, entry.FullName));
      if (!destinationPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(destinationPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidDataException("Blocked archive path traversal entry: " + entry.FullName);
      }

      if (string.IsNullOrEmpty(entry.Name))
      {
        Directory.CreateDirectory(destinationPath);
        continue;
      }

      var entryDirectory = Path.GetDirectoryName(destinationPath);
      if (!string.IsNullOrWhiteSpace(entryDirectory))
        Directory.CreateDirectory(entryDirectory);

      entry.ExtractToFile(destinationPath, overwrite: true);
    }
  }

  private static string? FindArchiveCandidate(string sourceRoot, string token)
  {
    var topLevel = EnumerateFilesSafe(sourceRoot, "*.zip", SearchOption.TopDirectoryOnly)
      .Where(path => Path.GetFileName(path).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(topLevel))
      return topLevel;

    return EnumerateFilesSafe(sourceRoot, "*.zip", SearchOption.AllDirectories)
      .Where(path => Path.GetFileName(path).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .FirstOrDefault();
  }

  private static string? FindFirstFileByName(string root, string fileName)
  {
    return EnumerateFilesSafe(root, "*", SearchOption.AllDirectories)
      .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
  }

  private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, SearchOption option)
  {
    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
      yield break;

    var pending = new Stack<string>();
    pending.Push(root);
    while (pending.Count > 0)
    {
      var current = pending.Pop();

      string[] files;
      try
      {
        files = Directory.GetFiles(current, pattern, SearchOption.TopDirectoryOnly);
      }
      catch
      {
        files = Array.Empty<string>();
      }

      foreach (var file in files)
        yield return file;

      if (option != SearchOption.AllDirectories)
        continue;

      string[] children;
      try
      {
        children = Directory.GetDirectories(current);
      }
      catch
      {
        children = Array.Empty<string>();
      }

      foreach (var child in children)
        pending.Push(child);
    }
  }

  private sealed class ToolkitActivationResult
  {
    public string SourceRoot { get; set; } = string.Empty;
    public string InstallRoot { get; set; } = string.Empty;
    public string? EvaluateStigRoot { get; set; }
    public string? ScapCommandPath { get; set; }
    public string? PowerStigModulePath { get; set; }
    public List<string> Notes { get; } = new();
  }
}

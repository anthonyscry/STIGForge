using System.Linq;
using STIGForge.Core.Abstractions;
using PackTypes = STIGForge.Core.Constants.PackTypes;
using ControlStatusStrings = STIGForge.Core.Constants.ControlStatus;

namespace STIGForge.Verify;

public sealed partial class VerificationWorkflowService
{
  private static List<string> DiscoverToolOutputPaths(VerificationWorkflowRequest request, IReadOnlyList<VerificationToolRunResult> toolRuns, DateTimeOffset workflowStartedAt, List<string> diagnostics)
  {
    var discoveredPaths = new List<string>();
    var searchCandidates = new List<string>();

    if (request.EvaluateStig.Enabled && !string.IsNullOrWhiteSpace(request.EvaluateStig.ToolRoot))
    {
      searchCandidates.Add(request.EvaluateStig.ToolRoot!);
      var parentDir = Path.GetDirectoryName(request.EvaluateStig.ToolRoot);
      if (!string.IsNullOrWhiteSpace(parentDir))
        searchCandidates.Add(parentDir);
    }

    if (request.Scap.Enabled)
    {
      if (!string.IsNullOrWhiteSpace(request.Scap.WorkingDirectory))
        searchCandidates.Add(request.Scap.WorkingDirectory!);

      if (!string.IsNullOrWhiteSpace(request.Scap.CommandPath))
      {
        var scapDir = Path.GetDirectoryName(request.Scap.CommandPath);
        if (!string.IsNullOrWhiteSpace(scapDir))
          searchCandidates.Add(scapDir);
      }

      foreach (var defaultSccDir in GetDefaultSccResultsDirectories())
        searchCandidates.Add(defaultSccDir);
    }

    foreach (var toolRun in toolRuns.Where(r => r.Executed))
    {
      if (!string.IsNullOrWhiteSpace(toolRun.Output))
      {
        var pathsFromOutput = ExtractPathsFromToolOutput(toolRun.Output);
        foreach (var path in pathsFromOutput)
        {
          var dir = Path.GetDirectoryName(path);
          if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            searchCandidates.Add(dir);
        }
      }
    }

    foreach (var candidate in searchCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
      if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
        continue;

      try
      {
        var newXmlFiles = Directory.GetFiles(candidate, "*.xml", SearchOption.AllDirectories)
          .Where(f => File.GetCreationTimeUtc(f) >= workflowStartedAt.UtcDateTime || File.GetLastWriteTimeUtc(f) >= workflowStartedAt.UtcDateTime)
          .ToList();

        if (newXmlFiles.Count > 0)
        {
          diagnostics.Add($"[DIAG] Discovered {newXmlFiles.Count} new XML file(s) in: {candidate}");
          discoveredPaths.Add(candidate);
        }
      }
      catch
      {
      }
    }

    return discoveredPaths;
  }

  private static List<string> ExtractPathsFromToolOutput(string output)
  {
    var paths = new List<string>();
    if (string.IsNullOrWhiteSpace(output))
      return paths;

    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (var line in lines)
    {
      var pathStart = line.IndexOf(@":\", StringComparison.OrdinalIgnoreCase);
      if (pathStart < 0)
        pathStart = line.IndexOf(@"\\", StringComparison.OrdinalIgnoreCase);

      if (pathStart >= 0)
      {
        var potentialPath = line.Substring(pathStart).Trim();
        var invalidChars = Path.GetInvalidPathChars();
        var endIndex = potentialPath.IndexOfAny(invalidChars);
        if (endIndex > 0)
          potentialPath = potentialPath.Substring(0, endIndex);

        potentialPath = potentialPath.TrimEnd('.', ' ', '\t');

        if (potentialPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            potentialPath.EndsWith(".xccdf", StringComparison.OrdinalIgnoreCase) ||
            potentialPath.EndsWith(".ckl", StringComparison.OrdinalIgnoreCase))
        {
          if (File.Exists(potentialPath))
            paths.Add(potentialPath);
        }
      }
    }

    return paths;
  }

  private static void LogScapOutputDiagnostics(string toolName, string outputDir, string patchedOutputDirectory, DateTimeOffset scanStartedAt, List<string> diagnostics)
  {
    diagnostics.Add($"[DIAG] {toolName} adapter search patterns:");
    foreach (var pattern in Adapters.ScapResultAdapter.GetSearchPatterns())
      diagnostics.Add($"[DIAG]   - {pattern}");

    diagnostics.Add($"[DIAG] {toolName} patched output directory (from options.xml): {(patchedOutputDirectory ?? string.Empty).Trim()}");

    if (!Directory.Exists(outputDir))
    {
      diagnostics.Add($"[DIAG] {toolName} output directory does not exist after scan: {outputDir}");
      return;
    }

    string[] allFiles;
    try
    {
      allFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
    }
    catch (Exception ex)
    {
      diagnostics.Add($"[DIAG] {toolName} failed to enumerate output directory '{outputDir}': {ex.Message}");
      return;
    }

    diagnostics.Add($"[DIAG] {toolName} output directory contains {allFiles.Length} file(s) recursively:");
    if (allFiles.Length == 0)
    {
      diagnostics.Add($"[DIAG]   - (no files found)");
    }
    else
    {
      foreach (var file in allFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        diagnostics.Add($"[DIAG]   - {file}");
    }

    LogPatternMatches(toolName, outputDir, "*.xml", diagnostics);
    LogPatternMatches(toolName, outputDir, "*.xccdf", diagnostics);
    LogPatternMatches(toolName, outputDir, "*.ckl", diagnostics);

    var sccResultsDirs = Array.Empty<string>();
    try
    {
      sccResultsDirs = Directory.GetDirectories(outputDir, "SCC_Results", SearchOption.AllDirectories);
    }
    catch (Exception ex)
    {
      diagnostics.Add($"[DIAG] {toolName} failed to inspect SCC_Results directories under '{outputDir}': {ex.Message}");
    }

    diagnostics.Add($"[DIAG] {toolName} SCC_Results directories under output: {sccResultsDirs.Length}");
    foreach (var dir in sccResultsDirs.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
      diagnostics.Add($"[DIAG]   - {dir}");

    var outputResultCount = 0;
    try
    {
      outputResultCount = Directory.GetFiles(outputDir, "*.xml", SearchOption.AllDirectories).Length
        + Directory.GetFiles(outputDir, "*.xccdf", SearchOption.AllDirectories).Length
        + Directory.GetFiles(outputDir, "*.ckl", SearchOption.AllDirectories).Length;
    }
    catch (Exception ex)
    {
      diagnostics.Add($"[DIAG] {toolName} failed to count output result files in '{outputDir}': {ex.Message}");
    }

    foreach (var defaultSccDir in GetDefaultSccResultsDirectories())
      LogDefaultSccDirectory(toolName, defaultSccDir, scanStartedAt, diagnostics, outputResultCount == 0);
  }

  private static void LogPatternMatches(string toolName, string root, string pattern, List<string> diagnostics)
  {
    string[] matches;
    try
    {
      matches = Directory.GetFiles(root, pattern, SearchOption.AllDirectories);
    }
    catch (Exception ex)
    {
      diagnostics.Add($"[DIAG] {toolName} failed to search pattern '{pattern}' in '{root}': {ex.Message}");
      return;
    }

    diagnostics.Add($"[DIAG] {toolName} pattern '{pattern}' matched {matches.Length} file(s):");
    if (matches.Length == 0)
    {
      diagnostics.Add($"[DIAG]   - (no matches)");
      return;
    }

    foreach (var file in matches.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
      diagnostics.Add($"[DIAG]   - {file}");
  }

  private static void LogDefaultSccDirectory(string toolName, string defaultSccDir, DateTimeOffset scanStartedAt, List<string> diagnostics, bool outputLooksEmpty)
  {
    if (!Directory.Exists(defaultSccDir))
    {
      diagnostics.Add($"[DIAG] {toolName} default SCC directory not found: {defaultSccDir}");
      return;
    }

    string[] resultCandidates;
    try
    {
      resultCandidates = Directory.GetFiles(defaultSccDir, "*", SearchOption.AllDirectories)
        .Where(f =>
        {
          var ext = Path.GetExtension(f);
          return string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(ext, ".xccdf", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(ext, ".ckl", StringComparison.OrdinalIgnoreCase);
        })
        .ToArray();
    }
    catch (Exception ex)
    {
      diagnostics.Add($"[DIAG] {toolName} failed to inspect default SCC directory '{defaultSccDir}': {ex.Message}");
      return;
    }

    diagnostics.Add($"[DIAG] {toolName} default SCC directory candidate: {defaultSccDir}");
    diagnostics.Add($"[DIAG] {toolName} default SCC files (*.xml/*.xccdf/*.ckl): {resultCandidates.Length}");

    var recentCandidates = resultCandidates
      .Where(f => File.GetLastWriteTimeUtc(f) >= scanStartedAt.UtcDateTime)
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();

    diagnostics.Add($"[DIAG] {toolName} default SCC files written during this run window: {recentCandidates.Count}");
    foreach (var file in recentCandidates)
      diagnostics.Add($"[DIAG]   - {file}");

    if (outputLooksEmpty && recentCandidates.Count > 0)
      diagnostics.Add($"[DIAG] WARNING: {toolName} output directory had no result files, but default SCC directory contains fresh results. SCC likely wrote outside configured output directory.");
  }

  private static IEnumerable<string> GetDefaultSccResultsDirectories()
  {
    var candidates = new List<string>();

    var userProfile = (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(userProfile))
      candidates.Add(Path.Combine(userProfile, "SCC"));

    var envUserProfile = (Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(envUserProfile))
      candidates.Add(Path.Combine(envUserProfile, "SCC"));

    var userName = (Environment.UserName ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(userName))
      candidates.Add(Path.Combine("C:\\Users", userName, "SCC"));

    return candidates
      .Where(p => !string.IsNullOrWhiteSpace(p))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static string MapVerifyStatus(VerifyStatus status)
  {
    return status switch
    {
      VerifyStatus.Pass => ControlStatusStrings.NotAFinding,
      VerifyStatus.Fail => ControlStatusStrings.Open,
      VerifyStatus.NotApplicable => ControlStatusStrings.NotApplicableAlt,
      VerifyStatus.NotReviewed => ControlStatusStrings.NotReviewed,
      _ => status.ToString()
    };
  }

  private static void LogAdapterRejectionReason(string toolName, string xmlFile, List<string> diagnostics)
  {
    var fileName = Path.GetFileName(xmlFile);
    var isLikelyEvaluateStigOutput = fileName.IndexOf("eval", StringComparison.OrdinalIgnoreCase) >= 0;
    var isLikelyScapOutput = fileName.IndexOf("xccdf", StringComparison.OrdinalIgnoreCase) >= 0;

    if (toolName == "Evaluate-STIG" && isLikelyEvaluateStigOutput)
      diagnostics.Add($"[DIAG] {toolName} adapter rejected '{fileName}' (format mismatch - expected STIGChecks/Finding elements)");
    else if (toolName == PackTypes.Scap && isLikelyScapOutput)
      diagnostics.Add($"[DIAG] {toolName} adapter rejected '{fileName}' (format mismatch - expected XCCDF TestResult elements)");
  }

  private static void LogUnhandledPotentialResultFile(string xmlFile, List<string> diagnostics)
  {
    var fileName = Path.GetFileName(xmlFile).ToLowerInvariant();
    var appearsToBeResultFile = fileName.Contains("result") || fileName.Contains("xccdf")
                                || fileName.Contains("eval") || fileName.Contains("check");

    if (appearsToBeResultFile)
      diagnostics.Add($"[DIAG] Potential result file '{Path.GetFileName(xmlFile)}' was not handled by any adapter - check file format");
  }

  private static string TruncateForLog(string text, int maxLength = 500)
  {
    if (string.IsNullOrEmpty(text))
      return string.Empty;

    var truncated = text.Length > maxLength
      ? text.Substring(0, maxLength) + "..."
      : text;

    return truncated.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
  }

  private static void WriteToolProcessLogs(string outputDir, string toolName, string stdout, string stderr, List<string> diagnostics)
  {
    try
    {
      var safeName = SanitizeFileName(toolName);
      var stdoutPath = Path.Combine(outputDir, safeName + "-stdout.log");
      var stderrPath = Path.Combine(outputDir, safeName + "-stderr.log");

      File.WriteAllText(stdoutPath, stdout ?? string.Empty);
      File.WriteAllText(stderrPath, stderr ?? string.Empty);

      diagnostics.Add($"[DIAG] Raw process logs: {stdoutPath}");
      diagnostics.Add($"[DIAG] Raw process logs: {stderrPath}");
    }
    catch (Exception ex)
    {
      diagnostics.Add($"[DIAG] Failed to write raw process logs: {ex.Message}");
    }
  }

  private static string SanitizeFileName(string name)
  {
    if (string.IsNullOrWhiteSpace(name))
      return "tool";

    var sanitized = name;
    foreach (var ch in Path.GetInvalidFileNameChars())
      sanitized = sanitized.Replace(ch, '_');

    return sanitized.Replace(' ', '-');
  }

  private static string GetResultFileStem(string filePath)
  {
    if (string.IsNullOrWhiteSpace(filePath))
      return string.Empty;

    var lower = filePath.ToLowerInvariant();
    if (lower.EndsWith(".xccdf.xml", StringComparison.Ordinal))
      return filePath.Substring(0, filePath.Length - ".xccdf.xml".Length);
    if (lower.EndsWith(".xccdf", StringComparison.Ordinal))
      return filePath.Substring(0, filePath.Length - ".xccdf".Length);
    if (lower.EndsWith(".ckl", StringComparison.Ordinal))
      return filePath.Substring(0, filePath.Length - ".ckl".Length);
    if (lower.EndsWith(".xml", StringComparison.Ordinal))
      return filePath.Substring(0, filePath.Length - ".xml".Length);

    return Path.GetFileNameWithoutExtension(filePath);
  }

  private static string GetResultArtifactKey(string filePath)
  {
    var stem = GetResultFileStem(filePath);
    if (string.IsNullOrWhiteSpace(stem))
      return string.Empty;

    var fileNameStem = Path.GetFileName(stem);
    if (string.IsNullOrWhiteSpace(fileNameStem))
      return stem;

    return System.Text.RegularExpressions.Regex.Replace(
      fileNameStem,
      @"_\d{8}-\d{6}$",
      string.Empty,
      System.Text.RegularExpressions.RegexOptions.CultureInvariant);
  }

  private static bool IsPathUnderRoot(string filePath, string rootPath)
  {
    if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(rootPath))
      return false;

    string fullFile;
    string fullRoot;
    try
    {
      fullFile = Path.GetFullPath(filePath)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      fullRoot = Path.GetFullPath(rootPath)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
    catch
    {
      return false;
    }

    if (string.IsNullOrWhiteSpace(fullFile) || string.IsNullOrWhiteSpace(fullRoot))
      return false;

    var rootedPrefix = fullRoot + Path.DirectorySeparatorChar;
    return fullFile.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase)
           || string.Equals(fullFile, fullRoot, StringComparison.OrdinalIgnoreCase);
  }

  private static void WriteDiagnosticsLog(string outputRoot, List<string> diagnostics, DateTimeOffset startedAt, DateTimeOffset finishedAt)
  {
    try
    {
      var logPath = Path.Combine(outputRoot, "verify_diagnostics.log");
      var sb = new System.Text.StringBuilder();
      sb.AppendLine("=== Verify Diagnostics Log ===");
      sb.AppendLine($"Started: {startedAt:yyyy-MM-dd HH:mm:ss}");
      sb.AppendLine($"Finished: {finishedAt:yyyy-MM-dd HH:mm:ss}");
      sb.AppendLine($"Duration: {(finishedAt - startedAt).TotalSeconds:F2} seconds");
      sb.AppendLine();
      sb.AppendLine("--- Diagnostics ---");
      foreach (var diag in diagnostics)
        sb.AppendLine(diag);
      sb.AppendLine();
      sb.AppendLine("=== End of Log ===");

      File.WriteAllText(logPath, sb.ToString());
    }
    catch
    {
    }
  }
}

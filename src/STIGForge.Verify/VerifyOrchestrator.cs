using System.Text.Json;
using STIGForge.Verify.Adapters;

namespace STIGForge.Verify;

/// <summary>
/// Orchestrates verification result collection from multiple tools.
/// Merges and reconciles results from SCAP, Evaluate-STIG, and manual CKL reviews.
/// </summary>
public sealed class VerifyOrchestrator
{
  private readonly List<IVerifyResultAdapter> _adapters;

  public VerifyOrchestrator()
  {
    _adapters = new List<IVerifyResultAdapter>
    {
      new ScapResultAdapter(),
      new EvaluateStigAdapter(),
      new CklAdapter()
    };
  }

  /// <summary>
  /// Parse a single verification result file using appropriate adapter.
  /// </summary>
  public NormalizedVerifyReport ParseResults(string resultFilePath)
  {
    if (!File.Exists(resultFilePath))
      throw new FileNotFoundException("Verification result file not found", resultFilePath);

    var adapter = FindAdapter(resultFilePath);
    if (adapter == null)
      throw new InvalidOperationException($"No adapter found for file: {resultFilePath}");

    return adapter.ParseResults(resultFilePath);
  }

  /// <summary>
  /// Parse multiple verification result files and merge into consolidated report.
  /// </summary>
  public ConsolidatedVerifyReport ParseAndMergeResults(IEnumerable<string> resultFilePaths)
  {
    var reports = new List<NormalizedVerifyReport>();
    var errors = new List<string>();

    foreach (var path in resultFilePaths)
    {
      try
      {
        var report = ParseResults(path);
        reports.Add(report);
      }
      catch (Exception ex)
      {
        errors.Add($"Failed to parse {path}: {ex.Message}");
      }
    }

    return MergeReports(reports, errors);
  }

  /// <summary>
  /// Merge multiple verification reports into a single consolidated view.
  /// Handles conflicts by applying precedence rules:
  /// 1. Manual CKL review overrides automated tools (human review is authoritative)
  /// 2. Latest timestamp wins if same tool reports different results
  /// 3. Fail status takes precedence over Pass (conservative approach)
  /// </summary>
  public ConsolidatedVerifyReport MergeReports(IReadOnlyList<NormalizedVerifyReport> reports, IReadOnlyList<string> errors)
  {
    var allResults = reports.SelectMany(r => r.Results).ToList();
    var allDiagnostics = reports.SelectMany(r => r.DiagnosticMessages).Concat(errors).ToList();

    // Group results by ControlId
    var grouped = allResults
      .GroupBy(r => r.ControlId, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    var mergedResults = new List<NormalizedVerifyResult>(grouped.Count);
    var conflicts = new List<ResultConflict>();

    foreach (var kvp in grouped)
    {
      var controlId = kvp.Key;
      var controlResults = kvp.Value;

      if (controlResults.Count == 1)
      {
        // Single result - no conflict
        mergedResults.Add(controlResults[0]);
      }
      else
      {
        // Multiple results - apply reconciliation logic
        var reconciled = ReconcileResults(controlId, controlResults, out var conflict);
        mergedResults.Add(reconciled);
        if (conflict != null)
          conflicts.Add(conflict);
      }
    }

    var summary = CalculateSummary(mergedResults);

    return new ConsolidatedVerifyReport
    {
      MergedAt = DateTimeOffset.Now,
      SourceReports = reports.Select(r => new SourceReportInfo
      {
        Tool = r.Tool,
        ToolVersion = r.ToolVersion,
        ResultCount = r.Results.Count,
        SourcePath = r.OutputRoot
      }).ToList(),
      Results = mergedResults,
      Summary = summary,
      Conflicts = conflicts,
      DiagnosticMessages = allDiagnostics
    };
  }

  /// <summary>
  /// Reconcile conflicting results for same control from different tools.
  /// Precedence: Manual CKL > Latest timestamp > Fail > Pass
  /// </summary>
  private NormalizedVerifyResult ReconcileResults(string controlId, List<NormalizedVerifyResult> results, out ResultConflict? conflict)
  {
    conflict = null;

    // Sort by precedence
    var sorted = results.OrderByDescending(r => GetToolPrecedence(r.Tool))
                        .ThenByDescending(r => r.VerifiedAt ?? DateTimeOffset.MinValue)
                        .ThenByDescending(r => GetStatusSeverity(r.Status))
                        .ToList();

    var primary = sorted[0];
    var alternatives = sorted.Skip(1).ToList();

    // Check if there are meaningful conflicts (different statuses)
    var statuses = results.Select(r => r.Status).Distinct().ToList();
    if (statuses.Count > 1 && statuses.Any(s => s == VerifyStatus.Pass) && statuses.Any(s => s == VerifyStatus.Fail))
    {
      conflict = new ResultConflict
      {
        ControlId = controlId,
        ResolvedStatus = primary.Status,
        ConflictingResults = alternatives.Select(r => new ConflictingResult
        {
          Tool = r.Tool,
          Status = r.Status,
          VerifiedAt = r.VerifiedAt ?? DateTimeOffset.MinValue
        }).ToList(),
        ResolutionReason = $"Applied precedence: {primary.Tool} (verified {primary.VerifiedAt:yyyy-MM-dd HH:mm}) overrides {string.Join(", ", alternatives.Select(a => a.Tool))}"
      };
    }

    // Merge metadata from all sources
    var mergedMetadata = new Dictionary<string, string>();
    foreach (var kvp in primary.Metadata)
      mergedMetadata[kvp.Key] = kvp.Value;

    foreach (var alt in alternatives)
    {
      foreach (var kvp in alt.Metadata)
      {
        var key = $"{alt.Tool.ToLowerInvariant().Replace(" ", "_")}_{kvp.Key}";
        if (!mergedMetadata.ContainsKey(key))
          mergedMetadata[key] = kvp.Value;
      }
    }

    // Merge evidence paths
    var evidencePaths = results.SelectMany(r => r.EvidencePaths).Distinct().ToList();

    // Merge comments
    var comments = results.Select(r => r.Comments).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
    var mergedComments = comments.Count > 0 ? string.Join("\n---\n", comments) : primary.Comments;

    return new NormalizedVerifyResult
    {
      ControlId = primary.ControlId,
      VulnId = primary.VulnId ?? alternatives.FirstOrDefault(a => a.VulnId != null)?.VulnId,
      RuleId = primary.RuleId ?? alternatives.FirstOrDefault(a => a.RuleId != null)?.RuleId,
      Title = primary.Title ?? alternatives.FirstOrDefault(a => a.Title != null)?.Title,
      Severity = primary.Severity ?? alternatives.FirstOrDefault(a => a.Severity != null)?.Severity,
      Status = primary.Status,
      FindingDetails = primary.FindingDetails,
      Comments = mergedComments,
      Tool = results.Count > 1 ? "Merged" : primary.Tool,
      SourceFile = primary.SourceFile,
      VerifiedAt = primary.VerifiedAt,
      EvidencePaths = evidencePaths,
      Metadata = mergedMetadata
    };
  }

  private static int GetToolPrecedence(string tool)
  {
    // Manual CKL has highest precedence (human review)
    if (tool.IndexOf("CKL", StringComparison.OrdinalIgnoreCase) >= 0)
      return 3;
    // Evaluate-STIG is PowerShell-based automation (medium precedence)
    if (tool.IndexOf("Evaluate", StringComparison.OrdinalIgnoreCase) >= 0)
      return 2;
    // SCAP is fully automated (lowest precedence)
    return 1;
  }

  private static int GetStatusSeverity(VerifyStatus status)
  {
    // Higher severity = more conservative (Fail > Pass)
    return status switch
    {
      VerifyStatus.Fail => 5,
      VerifyStatus.Error => 4,
      VerifyStatus.Pass => 3,
      VerifyStatus.NotReviewed => 2,
      VerifyStatus.NotApplicable => 1,
      VerifyStatus.Informational => 0,
      _ => -1
    };
  }

  private IVerifyResultAdapter? FindAdapter(string filePath)
  {
    return _adapters.FirstOrDefault(a => a.CanHandle(filePath));
  }

  private static VerifySummary CalculateSummary(IReadOnlyList<NormalizedVerifyResult> results)
  {
    var summary = new VerifySummary
    {
      TotalCount = results.Count
    };

    foreach (var result in results)
    {
      switch (result.Status)
      {
        case VerifyStatus.Pass:
          summary.PassCount++;
          break;
        case VerifyStatus.Fail:
          summary.FailCount++;
          break;
        case VerifyStatus.NotApplicable:
          summary.NotApplicableCount++;
          break;
        case VerifyStatus.NotReviewed:
          summary.NotReviewedCount++;
          break;
        case VerifyStatus.Informational:
          summary.InformationalCount++;
          break;
        case VerifyStatus.Error:
          summary.ErrorCount++;
          break;
      }
    }

    var evaluatedCount = summary.PassCount + summary.FailCount + summary.ErrorCount;
    summary.CompliancePercent = evaluatedCount > 0
      ? (summary.PassCount / (double)evaluatedCount) * 100.0
      : 0.0;

    return summary;
  }

  /// <summary>
  /// Save consolidated verify report to JSON file.
  /// </summary>
  public void SaveReport(ConsolidatedVerifyReport report, string outputPath)
  {
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    File.WriteAllText(outputPath, json);
  }
}

/// <summary>
/// Consolidated verification report from multiple sources.
/// </summary>
public sealed class ConsolidatedVerifyReport
{
  public DateTimeOffset MergedAt { get; set; }
  public IReadOnlyList<SourceReportInfo> SourceReports { get; set; } = Array.Empty<SourceReportInfo>();
  public IReadOnlyList<NormalizedVerifyResult> Results { get; set; } = Array.Empty<NormalizedVerifyResult>();
  public VerifySummary Summary { get; set; } = new VerifySummary();
  public IReadOnlyList<ResultConflict> Conflicts { get; set; } = Array.Empty<ResultConflict>();
  public IReadOnlyList<string> DiagnosticMessages { get; set; } = Array.Empty<string>();
}

public sealed class SourceReportInfo
{
  public string Tool { get; set; } = string.Empty;
  public string ToolVersion { get; set; } = string.Empty;
  public int ResultCount { get; set; }
  public string SourcePath { get; set; } = string.Empty;
}

public sealed class ResultConflict
{
  public string ControlId { get; set; } = string.Empty;
  public VerifyStatus ResolvedStatus { get; set; }
  public IReadOnlyList<ConflictingResult> ConflictingResults { get; set; } = Array.Empty<ConflictingResult>();
  public string ResolutionReason { get; set; } = string.Empty;
}

public sealed class ConflictingResult
{
  public string Tool { get; set; } = string.Empty;
  public VerifyStatus Status { get; set; }
  public DateTimeOffset VerifiedAt { get; set; }
}

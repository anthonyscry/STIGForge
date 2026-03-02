using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class AcasCorrelationService
{
  private readonly NessusImporter _nessusImporter;
  private readonly IControlRepository? _controlRepo;

  public AcasCorrelationService(NessusImporter nessusImporter, IControlRepository? controlRepo = null)
  {
    _nessusImporter = nessusImporter ?? throw new ArgumentNullException(nameof(nessusImporter));
    _controlRepo = controlRepo;
  }

  public AcasCorrelationResult Correlate(string nessusFilePath, string? bundleRoot = null)
  {
    var findings = _nessusImporter.Import(nessusFilePath);
    var correlations = new List<AcasControlCorrelation>();
    var unmatched = new List<NessusFinding>();
    var controls = LoadControls(bundleRoot);

    foreach (var finding in findings)
    {
      var correlation = CorrelateFinding(finding, controls);
      if (correlation != null)
        correlations.Add(correlation);
      else
        unmatched.Add(finding);
    }

    return new AcasCorrelationResult
    {
      TotalFindings = findings.Count,
      CorrelatedCount = correlations.Count,
      UnmatchedCount = unmatched.Count,
      Correlations = correlations.OrderByDescending(c => c.Finding.Severity).ToList(),
      UnmatchedFindings = unmatched.OrderByDescending(f => f.Severity).ToList()
    };
  }

  private IReadOnlyList<ControlRecord> LoadControls(string? bundleRoot)
  {
    if (_controlRepo == null || string.IsNullOrWhiteSpace(bundleRoot))
      return Array.Empty<ControlRecord>();

    return _controlRepo.ListControlsAsync(bundleRoot, CancellationToken.None).GetAwaiter().GetResult();
  }

  private AcasControlCorrelation? CorrelateFinding(NessusFinding finding, IReadOnlyList<ControlRecord> controls)
  {
    ControlRecord? control = null;
    var correlationType = "Unknown";

    if (!string.IsNullOrWhiteSpace(finding.StigRuleId))
    {
      control = FindControlByRuleId(finding.StigRuleId, controls);
      correlationType = "StigRuleId";
    }

    if (control == null && !string.IsNullOrWhiteSpace(finding.PluginName))
    {
      control = FindControlByTitle(finding.PluginName, controls);
      if (control != null)
        correlationType = "TitleMatch";
    }

    if (control == null && finding.CveList.Count > 0)
    {
      control = FindControlByCve(finding.CveList, controls);
      if (control != null)
        correlationType = "CveMatch";
    }

    if (control == null)
      return null;

    return new AcasControlCorrelation
    {
      Finding = finding,
      Control = control,
      CorrelationType = correlationType,
      StigStatus = GetControlStatus(control),
      MismatchType = DetermineMismatch(finding, control)
    };
  }

  private static ControlRecord? FindControlByRuleId(string ruleId, IReadOnlyList<ControlRecord> controls)
  {
    return controls.FirstOrDefault(c =>
      string.Equals(c.ExternalIds?.RuleId, ruleId, StringComparison.OrdinalIgnoreCase) ||
      c.ControlId.Contains(ruleId, StringComparison.OrdinalIgnoreCase));
  }

  private static ControlRecord? FindControlByTitle(string title, IReadOnlyList<ControlRecord> controls)
  {
    var keywords = title.Split(new[] { ' ', '-', '_', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
      .Where(w => w.Length > 3)
      .ToList();

    return controls
      .Select(c => new { Control = c, Score = CalculateTitleMatchScore(c.Title, keywords) })
      .Where(x => x.Score > 0.5)
      .OrderByDescending(x => x.Score)
      .FirstOrDefault()?.Control;
  }

  private static ControlRecord? FindControlByCve(IReadOnlyList<string> cves, IReadOnlyList<ControlRecord> controls)
  {
    if (cves.Count == 0)
      return null;

    return controls.FirstOrDefault(control =>
    {
      var text = string.Join(" ",
        control.Title,
        control.Discussion ?? string.Empty,
        control.CheckText ?? string.Empty,
        control.FixText ?? string.Empty);

      return cves.Any(cve => text.Contains(cve, StringComparison.OrdinalIgnoreCase));
    });
  }

  private static double CalculateTitleMatchScore(string? controlTitle, List<string> keywords)
  {
    if (string.IsNullOrWhiteSpace(controlTitle) || keywords.Count == 0)
      return 0;

    var titleLower = controlTitle.ToLowerInvariant();
    var matches = keywords.Count(k => titleLower.Contains(k.ToLowerInvariant()));
    return (double)matches / keywords.Count;
  }

  private static string GetControlStatus(ControlRecord control)
  {
    return control.IsManual ? "ManualReviewRequired" : "Planned";
  }

  private static string? DetermineMismatch(NessusFinding finding, ControlRecord control)
  {
    if (finding.Severity >= 3 && control.IsManual)
      return "AcasHighNotReviewed";

    if (finding.Severity >= 3 &&
        (string.Equals(control.Severity, "low", StringComparison.OrdinalIgnoreCase)
         || string.Equals(control.Severity, "cat iii", StringComparison.OrdinalIgnoreCase)))
      return "AcasHighSeverityMismatch";

    return null;
  }
}

public sealed class AcasCorrelationResult
{
  public int TotalFindings { get; set; }
  public int CorrelatedCount { get; set; }
  public int UnmatchedCount { get; set; }
  public IReadOnlyList<AcasControlCorrelation> Correlations { get; set; } = Array.Empty<AcasControlCorrelation>();
  public IReadOnlyList<NessusFinding> UnmatchedFindings { get; set; } = Array.Empty<NessusFinding>();

  public int CriticalAndHigh => Correlations.Count(c => c.Finding.Severity >= 3);
  public int Mismatches => Correlations.Count(c => c.MismatchType != null);
}

public sealed class AcasControlCorrelation
{
  public NessusFinding Finding { get; set; } = null!;
  public ControlRecord Control { get; set; } = null!;
  public string CorrelationType { get; set; } = string.Empty;
  public string StigStatus { get; set; } = string.Empty;
  public string? MismatchType { get; set; }
}

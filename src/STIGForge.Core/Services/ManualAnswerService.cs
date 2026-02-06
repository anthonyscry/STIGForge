using System.Text;
using System.Text.Json;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

/// <summary>
/// Service for managing manual answer files and control progression.
/// Handles answer persistence, progress tracking, and wizard navigation.
/// </summary>
public sealed class ManualAnswerService
{
  /// <summary>
  /// Load or create answer file for a bundle.
  /// </summary>
  public AnswerFile LoadAnswerFile(string bundleRoot)
  {
    var path = GetAnswerFilePath(bundleRoot);
    if (!File.Exists(path))
      return CreateEmptyAnswerFile(bundleRoot);

    try
    {
      var json = File.ReadAllText(path);
      var file = JsonSerializer.Deserialize<AnswerFile>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      return file ?? CreateEmptyAnswerFile(bundleRoot);
    }
    catch
    {
      return CreateEmptyAnswerFile(bundleRoot);
    }
  }

  /// <summary>
  /// Save answer file to bundle.
  /// </summary>
  public void SaveAnswerFile(string bundleRoot, AnswerFile answerFile)
  {
    var path = GetAnswerFilePath(bundleRoot);
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir))
      Directory.CreateDirectory(dir);

    var json = JsonSerializer.Serialize(answerFile, new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    File.WriteAllText(path, json, Encoding.UTF8);
  }

  /// <summary>
  /// Save or update a single answer.
  /// </summary>
  public void SaveAnswer(string bundleRoot, ManualAnswer answer)
  {
    var file = LoadAnswerFile(bundleRoot);
    
    // Find existing answer by RuleId or VulnId
    var existing = file.Answers.FirstOrDefault(a =>
      (!string.IsNullOrWhiteSpace(a.RuleId) && string.Equals(a.RuleId, answer.RuleId, StringComparison.OrdinalIgnoreCase)) ||
      (!string.IsNullOrWhiteSpace(a.VulnId) && string.Equals(a.VulnId, answer.VulnId, StringComparison.OrdinalIgnoreCase)));

    if (existing != null)
    {
      // Update existing
      existing.Status = answer.Status;
      existing.Reason = answer.Reason;
      existing.Comment = answer.Comment;
      existing.UpdatedAt = DateTimeOffset.Now;
    }
    else
    {
      // Add new
      answer.UpdatedAt = DateTimeOffset.Now;
      file.Answers.Add(answer);
    }

    SaveAnswerFile(bundleRoot, file);
  }

  /// <summary>
  /// Get answer for a control.
  /// </summary>
  public ManualAnswer? GetAnswer(string bundleRoot, ControlRecord control)
  {
    var file = LoadAnswerFile(bundleRoot);
    return FindAnswer(file, control);
  }

  /// <summary>
  /// Get all unanswered controls from a list.
  /// </summary>
  public List<ControlRecord> GetUnansweredControls(string bundleRoot, IReadOnlyList<ControlRecord> controls)
  {
    var file = LoadAnswerFile(bundleRoot);
    var answered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var answer in file.Answers)
    {
      if (!string.IsNullOrWhiteSpace(answer.RuleId))
        answered.Add("RULE:" + answer.RuleId);
      if (!string.IsNullOrWhiteSpace(answer.VulnId))
        answered.Add("VULN:" + answer.VulnId);
    }

    return controls.Where(c =>
    {
      var ruleKey = !string.IsNullOrWhiteSpace(c.ExternalIds.RuleId) ? "RULE:" + c.ExternalIds.RuleId : null;
      var vulnKey = !string.IsNullOrWhiteSpace(c.ExternalIds.VulnId) ? "VULN:" + c.ExternalIds.VulnId : null;
      
      return !answered.Contains(ruleKey ?? string.Empty) && !answered.Contains(vulnKey ?? string.Empty);
    }).ToList();
  }

  /// <summary>
  /// Calculate progress statistics.
  /// </summary>
  public ManualProgressStats GetProgressStats(string bundleRoot, IReadOnlyList<ControlRecord> manualControls)
  {
    var file = LoadAnswerFile(bundleRoot);
    var total = manualControls.Count;
    var answered = file.Answers.Count;
    var pass = file.Answers.Count(a => a.Status.IndexOf("Pass", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                        a.Status.IndexOf("NotAFinding", StringComparison.OrdinalIgnoreCase) >= 0);
    var fail = file.Answers.Count(a => a.Status.IndexOf("Fail", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                        a.Status.IndexOf("Open", StringComparison.OrdinalIgnoreCase) >= 0);
    var na = file.Answers.Count(a => a.Status.IndexOf("NotApplicable", StringComparison.OrdinalIgnoreCase) >= 0);

    return new ManualProgressStats
    {
      TotalControls = total,
      AnsweredControls = answered,
      UnansweredControls = total - answered,
      PassCount = pass,
      FailCount = fail,
      NotApplicableCount = na,
      PercentComplete = total > 0 ? (answered * 100.0 / total) : 0.0
    };
  }

  private static ManualAnswer? FindAnswer(AnswerFile file, ControlRecord control)
  {
    return file.Answers.FirstOrDefault(a =>
      (!string.IsNullOrWhiteSpace(a.RuleId) && string.Equals(a.RuleId, control.ExternalIds.RuleId, StringComparison.OrdinalIgnoreCase)) ||
      (!string.IsNullOrWhiteSpace(a.VulnId) && string.Equals(a.VulnId, control.ExternalIds.VulnId, StringComparison.OrdinalIgnoreCase)));
  }

  private static AnswerFile CreateEmptyAnswerFile(string bundleRoot)
  {
    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
    string? profileId = null;
    string? packId = null;

    if (File.Exists(manifestPath))
    {
      try
      {
        var manifestJson = File.ReadAllText(manifestPath);
        using var doc = JsonDocument.Parse(manifestJson);
        profileId = doc.RootElement.GetProperty("run").GetProperty("profileName").GetString();
        packId = doc.RootElement.GetProperty("run").GetProperty("packName").GetString();
      }
      catch
      {
        // Ignore parse errors
      }
    }

    return new AnswerFile
    {
      ProfileId = profileId,
      PackId = packId,
      CreatedAt = DateTimeOffset.Now,
      Answers = new List<ManualAnswer>()
    };
  }

  private static string GetAnswerFilePath(string bundleRoot)
  {
    return Path.Combine(bundleRoot, "Manual", "answers.json");
  }
}

/// <summary>
/// Progress statistics for manual control review.
/// </summary>
public sealed class ManualProgressStats
{
  public int TotalControls { get; set; }
  public int AnsweredControls { get; set; }
  public int UnansweredControls { get; set; }
  public int PassCount { get; set; }
  public int FailCount { get; set; }
  public int NotApplicableCount { get; set; }
  public double PercentComplete { get; set; }
}

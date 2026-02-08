using System.Text;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

/// <summary>
/// Service for managing manual answer files and control progression.
/// Handles answer persistence, progress tracking, and wizard navigation.
/// </summary>
public sealed class ManualAnswerService
{
  private readonly IAuditTrailService? _audit;

  public ManualAnswerService(IAuditTrailService? audit = null)
  {
    _audit = audit;
  }

  public string NormalizeStatus(string? status)
  {
    var token = NormalizeToken(status);
    if (token.Length == 0)
      return "Open";

    if (token == "pass" || token == "notafinding" || token == "compliant" || token == "closed")
      return "Pass";

    if (token == "fail" || token == "noncompliant")
      return "Fail";

    if (token == "notapplicable" || token == "na")
      return "NotApplicable";

    if (token == "open" || token == "notreviewed" || token == "notchecked" || token == "unknown" || token == "informational" || token == "error")
      return "Open";

    return "Open";
  }

  public bool RequiresReason(string? status)
  {
    var normalized = NormalizeStatus(status);
    return string.Equals(normalized, "Fail", StringComparison.Ordinal)
      || string.Equals(normalized, "NotApplicable", StringComparison.Ordinal);
  }

  public void ValidateReasonRequirement(string? status, string? reason)
  {
    if (!RequiresReason(status))
      return;

    if (string.IsNullOrWhiteSpace(reason))
      throw new ArgumentException("Reason is required for Fail and NotApplicable manual decisions.", nameof(reason));
  }

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

      if (file == null)
        return CreateEmptyAnswerFile(bundleRoot);

      NormalizeAnswerFile(file);
      return file;
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
    NormalizeAnswerFile(answerFile);

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
  public void SaveAnswer(string bundleRoot, ManualAnswer answer, bool requireReasonForDecision = false, string? profileId = null, string? packId = null)
  {
    if (answer == null)
      throw new ArgumentNullException(nameof(answer));

    if (string.IsNullOrWhiteSpace(answer.RuleId) && string.IsNullOrWhiteSpace(answer.VulnId))
      throw new ArgumentException("Answer must include RuleId or VulnId.", nameof(answer));

    var normalizedStatus = NormalizeStatus(answer.Status);
    if (requireReasonForDecision)
      ValidateReasonRequirement(normalizedStatus, answer.Reason);

    var file = LoadAnswerFile(bundleRoot);

    if (!string.IsNullOrWhiteSpace(profileId))
      file.ProfileId = profileId.Trim();

    if (!string.IsNullOrWhiteSpace(packId))
      file.PackId = packId.Trim();

    var ruleId = string.IsNullOrWhiteSpace(answer.RuleId) ? null : answer.RuleId.Trim();
    var vulnId = string.IsNullOrWhiteSpace(answer.VulnId) ? null : answer.VulnId.Trim();
    var reason = string.IsNullOrWhiteSpace(answer.Reason) ? null : answer.Reason.Trim();
    var comment = string.IsNullOrWhiteSpace(answer.Comment) ? null : answer.Comment.Trim();
    
    // Find existing answer by RuleId or VulnId
    var existing = file.Answers.FirstOrDefault(a =>
      (!string.IsNullOrWhiteSpace(a.RuleId) && !string.IsNullOrWhiteSpace(ruleId) && string.Equals(a.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)) ||
      (!string.IsNullOrWhiteSpace(a.VulnId) && !string.IsNullOrWhiteSpace(vulnId) && string.Equals(a.VulnId, vulnId, StringComparison.OrdinalIgnoreCase)));

    if (existing != null)
    {
      // Update existing
      existing.RuleId = ruleId ?? existing.RuleId;
      existing.VulnId = vulnId ?? existing.VulnId;
      existing.Status = normalizedStatus;
      existing.Reason = reason;
      existing.Comment = comment;
      existing.UpdatedAt = DateTimeOffset.Now;
    }
    else
    {
      // Add new
      file.Answers.Add(new ManualAnswer
      {
        RuleId = ruleId,
        VulnId = vulnId,
        Status = normalizedStatus,
        Reason = reason,
        Comment = comment,
        UpdatedAt = DateTimeOffset.Now
      });
    }

    SaveAnswerFile(bundleRoot, file);

    if (_audit != null)
    {
      _ = Task.Run(async () =>
      {
        try
        {
          await _audit.RecordAsync(new AuditEntry
          {
            Action = "manual-answer",
            Target = ruleId ?? vulnId ?? "unknown",
            Result = normalizedStatus,
            Detail = reason ?? string.Empty,
            User = Environment.UserName,
            Machine = Environment.MachineName,
            Timestamp = DateTimeOffset.Now
          }, CancellationToken.None).ConfigureAwait(false);
        }
        catch { /* fire-and-forget audit */ }
      });
    }
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
      var status = NormalizeStatus(answer.Status);
      if (string.Equals(status, "Open", StringComparison.Ordinal))
        continue;

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
    var pass = 0;
    var fail = 0;
    var na = 0;
    var open = 0;

    foreach (var control in manualControls)
    {
      var answer = FindAnswer(file, control);
      if (answer == null)
      {
        open++;
        continue;
      }

      var status = NormalizeStatus(answer.Status);
      if (string.Equals(status, "Pass", StringComparison.Ordinal))
      {
        pass++;
      }
      else if (string.Equals(status, "Fail", StringComparison.Ordinal))
      {
        fail++;
      }
      else if (string.Equals(status, "NotApplicable", StringComparison.Ordinal))
      {
        na++;
      }
      else
      {
        open++;
      }
    }

    var answered = pass + fail + na;

    return new ManualProgressStats
    {
      TotalControls = total,
      AnsweredControls = answered,
      UnansweredControls = open,
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

  private void NormalizeAnswerFile(AnswerFile answerFile)
  {
    if (answerFile.Answers == null)
    {
      answerFile.Answers = new List<ManualAnswer>();
      return;
    }

    foreach (var answer in answerFile.Answers)
    {
      answer.RuleId = string.IsNullOrWhiteSpace(answer.RuleId) ? null : answer.RuleId.Trim();
      answer.VulnId = string.IsNullOrWhiteSpace(answer.VulnId) ? null : answer.VulnId.Trim();
      answer.Status = NormalizeStatus(answer.Status);
      answer.Reason = string.IsNullOrWhiteSpace(answer.Reason) ? null : answer.Reason.Trim();
      answer.Comment = string.IsNullOrWhiteSpace(answer.Comment) ? null : answer.Comment.Trim();
    }
  }

  private static string NormalizeToken(string? status)
  {
    if (string.IsNullOrWhiteSpace(status))
      return string.Empty;

    var source = status.Trim().ToLowerInvariant();
    var sb = new StringBuilder(source.Length);
    foreach (var ch in source)
    {
      if (char.IsLetterOrDigit(ch))
        sb.Append(ch);
    }

    return sb.ToString();
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

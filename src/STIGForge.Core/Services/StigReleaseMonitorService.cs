using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

/// <summary>
/// Monitors for new STIG releases by comparing local packs, generates release notes,
/// and estimates compliance impact. Offline-safe: compares only locally imported packs.
/// </summary>
public sealed class StigReleaseMonitorService
{
  private readonly IContentPackRepository _packRepo;
  private readonly BaselineDiffService _diffService;
  private readonly IReleaseCheckRepository _releaseCheckRepo;
  private readonly ComplianceTrendService? _complianceTrend;
  private readonly IClock _clock;

  public StigReleaseMonitorService(
    IContentPackRepository packRepo,
    BaselineDiffService diffService,
    IReleaseCheckRepository releaseCheckRepo,
    ComplianceTrendService? complianceTrend = null,
    IClock? clock = null)
  {
    _packRepo = packRepo ?? throw new ArgumentNullException(nameof(packRepo));
    _diffService = diffService ?? throw new ArgumentNullException(nameof(diffService));
    _releaseCheckRepo = releaseCheckRepo ?? throw new ArgumentNullException(nameof(releaseCheckRepo));
    _complianceTrend = complianceTrend;
    _clock = clock ?? new SystemClock();
  }

  /// <summary>
  /// Checks all local packs for newer versions compared to the baseline pack.
  /// Returns a ReleaseCheck with status indicating whether a new release was found.
  /// </summary>
  public async Task<ReleaseCheck> CheckForNewReleasesAsync(string currentPackId, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(currentPackId)) throw new ArgumentException("Value cannot be null or empty.", nameof(currentPackId));

    var currentPack = await _packRepo.GetAsync(currentPackId, ct).ConfigureAwait(false);
    if (currentPack == null)
      throw new ArgumentException($"Pack not found: {currentPackId}", nameof(currentPackId));

    var allPacks = await _packRepo.ListAsync(ct).ConfigureAwait(false);

    var newerPacks = allPacks
      .Where(p => p.PackId != currentPackId)
      .Where(p => IsNewerPack(currentPack, p))
      .OrderByDescending(p => p.ImportedAt)
      .ToList();

    var check = new ReleaseCheck
    {
      CheckId = Guid.NewGuid().ToString("N"),
      CheckedAt = _clock.Now,
      BaselinePackId = currentPackId
    };

    if (newerPacks.Count == 0)
    {
      check.Status = "NoNewRelease";
    }
    else
    {
      check.Status = "NewReleaseFound";
      check.TargetPackId = newerPacks[0].PackId;
      check.SummaryJson = JsonSerializer.Serialize(new
      {
        NewerPackCount = newerPacks.Count,
        LatestPackId = newerPacks[0].PackId,
        LatestPackName = newerPacks[0].Name
      });
    }

    await _releaseCheckRepo.SaveAsync(check, ct).ConfigureAwait(false);
    return check;
  }

  /// <summary>
  /// Generates detailed release notes by diffing baseline and target packs.
  /// Includes highlighted changes (severity escalations, new CAT I rules, etc.)
  /// and optional compliance impact estimate.
  /// </summary>
  public async Task<ReleaseNotes> GenerateReleaseNotesAsync(string baselinePackId, string targetPackId, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(baselinePackId)) throw new ArgumentException("Value cannot be null or empty.", nameof(baselinePackId));
    if (string.IsNullOrEmpty(targetPackId)) throw new ArgumentException("Value cannot be null or empty.", nameof(targetPackId));

    var diff = await _diffService.ComparePacksAsync(baselinePackId, targetPackId, ct).ConfigureAwait(false);

    var highlights = new List<string>();
    var severityChanged = 0;
    var severityEscalations = 0;
    var severityDeescalations = 0;

    foreach (var mod in diff.ModifiedControls)
    {
      foreach (var change in mod.Changes)
      {
        if (change.FieldName.Equals("Severity", StringComparison.OrdinalIgnoreCase))
        {
          severityChanged++;
          var oldSev = NormalizeSeverityRank(change.OldValue);
          var newSev = NormalizeSeverityRank(change.NewValue);
          if (newSev > oldSev)
          {
            severityEscalations++;
            highlights.Add($"ESCALATION: {mod.ControlKey} severity {change.OldValue} -> {change.NewValue}");
          }
          else if (newSev < oldSev)
          {
            severityDeescalations++;
          }
        }
      }
    }

    foreach (var added in diff.AddedControls)
    {
      if (added.NewControl?.Severity?.Equals("high", StringComparison.OrdinalIgnoreCase) == true)
      {
        highlights.Add($"NEW CAT I: {added.ControlKey} - {added.NewControl?.Title}");
      }
    }

    if (diff.RemovedControls.Count > 0)
    {
      highlights.Add($"{diff.RemovedControls.Count} controls removed from STIG");
    }

    var notes = new ReleaseNotes
    {
      BaselinePackId = baselinePackId,
      TargetPackId = targetPackId,
      GeneratedAt = _clock.Now,
      AddedCount = diff.AddedControls.Count,
      RemovedCount = diff.RemovedControls.Count,
      ModifiedCount = diff.ModifiedControls.Count,
      SeverityChangedCount = severityChanged,
      HighlightedChanges = highlights,
      ComplianceImpact = new ComplianceImpactEstimate
      {
        NewControlsRequiringReview = diff.AddedControls.Count,
        RemovedControlsAffectingScore = diff.RemovedControls.Count,
        SeverityEscalations = severityEscalations,
        SeverityDeescalations = severityDeescalations
      }
    };

    return notes;
  }

  /// <summary>
  /// Imports a new pack diff and records a release check with DiffGenerated status.
  /// </summary>
  public async Task<ReleaseCheck> ImportAndDiffAsync(string currentPackId, string newPackId, CancellationToken ct)
  {
    var notes = await GenerateReleaseNotesAsync(currentPackId, newPackId, ct).ConfigureAwait(false);
    var notesJson = JsonSerializer.Serialize(notes, JsonOptions.Indented);

    var check = new ReleaseCheck
    {
      CheckId = Guid.NewGuid().ToString("N"),
      CheckedAt = _clock.Now,
      BaselinePackId = currentPackId,
      TargetPackId = newPackId,
      Status = "DiffGenerated",
      SummaryJson = notesJson
    };

    await _releaseCheckRepo.SaveAsync(check, ct).ConfigureAwait(false);
    return check;
  }

  private static bool IsNewerPack(ContentPack baseline, ContentPack candidate)
  {
    if (candidate.ImportedAt > baseline.ImportedAt)
      return true;

    return false;
  }

  private static int NormalizeSeverityRank(string? severity)
  {
    if (string.IsNullOrWhiteSpace(severity)) return 0;
    return severity.Trim().ToLowerInvariant() switch
    {
      "high" => 3,
      "medium" => 2,
      "low" => 1,
      _ => 0
    };
  }
}

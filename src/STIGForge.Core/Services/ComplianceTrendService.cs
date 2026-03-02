using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

/// <summary>
/// Records compliance snapshots and analyzes trends with regression detection.
/// Formula: CompliancePercent = Pass / (Pass + Fail + Error) * 100
/// </summary>
public sealed class ComplianceTrendService
{
  private readonly IComplianceTrendRepository _repo;
  private readonly IClock _clock;

  public ComplianceTrendService(IComplianceTrendRepository repo, IClock? clock = null)
  {
    _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    _clock = clock ?? new DefaultClock();
  }

  public async Task RecordSnapshotAsync(
    int passCount, int failCount, int errorCount, int notApplicableCount, int notReviewedCount,
    string bundleRoot, string? runId, string? packId, string tool,
    CancellationToken ct)
  {
    var totalCount = passCount + failCount + errorCount + notApplicableCount + notReviewedCount;
    var evaluatedCount = passCount + failCount + errorCount;
    var compliancePercent = evaluatedCount > 0
      ? (passCount / (double)evaluatedCount) * 100.0
      : 0.0;

    var snapshot = new ComplianceSnapshot
    {
      SnapshotId = Guid.NewGuid().ToString("N"),
      BundleRoot = bundleRoot,
      RunId = runId,
      PackId = packId,
      CapturedAt = _clock.Now,
      PassCount = passCount,
      FailCount = failCount,
      ErrorCount = errorCount,
      NotApplicableCount = notApplicableCount,
      NotReviewedCount = notReviewedCount,
      TotalCount = totalCount,
      CompliancePercent = Math.Round(compliancePercent, 2),
      Tool = tool
    };

    await _repo.SaveSnapshotAsync(snapshot, ct).ConfigureAwait(false);
  }

  public async Task<ComplianceTrend> GetTrendAsync(string bundleRoot, int limit, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));
    if (limit < 1) limit = 10;

    var snapshots = await _repo.GetSnapshotsAsync(bundleRoot, limit, ct).ConfigureAwait(false);
    if (snapshots.Count == 0)
    {
      return new ComplianceTrend
      {
        Snapshots = snapshots,
        CurrentPercent = 0,
        PreviousPercent = 0,
        Delta = 0,
        IsRegression = false
      };
    }

    var current = snapshots[0].CompliancePercent;
    var previous = snapshots.Count > 1 ? snapshots[1].CompliancePercent : current;
    var delta = current - previous;

    return new ComplianceTrend
    {
      Snapshots = snapshots,
      CurrentPercent = current,
      PreviousPercent = previous,
      Delta = Math.Round(delta, 2),
      IsRegression = delta < -0.01, // any decrease is a regression signal
      RegressionSummary = delta < -0.01
        ? $"Compliance dropped from {previous:F1}% to {current:F1}% (Δ{delta:F1}%)"
        : null
    };
  }

  public async Task<bool> DetectRegressionAsync(string bundleRoot, double thresholdPercent, CancellationToken ct)
  {
    var trend = await GetTrendAsync(bundleRoot, 2, ct).ConfigureAwait(false);
    return trend.Delta < -thresholdPercent;
  }

  private sealed class DefaultClock : IClock
  {
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
  }
}

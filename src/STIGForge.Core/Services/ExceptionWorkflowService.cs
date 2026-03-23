using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class ExceptionWorkflowService
{
  private readonly IExceptionRepository _repo;
  private readonly IClock _clock;

  public ExceptionWorkflowService(IExceptionRepository repo, IClock? clock = null)
  {
    _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    _clock = clock ?? new SystemClock();
  }

  public async Task<ControlException> CreateExceptionAsync(CreateExceptionRequest req, CancellationToken ct)
  {
    if (req is null) throw new ArgumentNullException(nameof(req));
    if (string.IsNullOrEmpty(req.RuleId)) throw new ArgumentException("Value cannot be null or empty.", nameof(req.RuleId));
    if (string.IsNullOrEmpty(req.ApprovedBy)) throw new ArgumentException("Value cannot be null or empty.", nameof(req.ApprovedBy));
    if (string.IsNullOrEmpty(req.ExceptionType)) throw new ArgumentException("Value cannot be null or empty.", nameof(req.ExceptionType));
    if (string.IsNullOrEmpty(req.RiskLevel)) throw new ArgumentException("Value cannot be null or empty.", nameof(req.RiskLevel));

    var exception = new ControlException
    {
      ExceptionId = Guid.NewGuid().ToString("N"),
      BundleRoot = req.BundleRoot,
      RuleId = req.RuleId,
      VulnId = req.VulnId,
      ExceptionType = req.ExceptionType,
      Status = "Active",      RiskLevel = req.RiskLevel,
      ApprovedBy = req.ApprovedBy,
      Justification = req.Justification,
      JustificationDoc = req.JustificationDoc,
      CreatedAt = _clock.Now,
      ExpiresAt = req.ExpiresAt
    };

    await _repo.SaveAsync(exception, ct).ConfigureAwait(false);
    return exception;
  }

  public async Task RevokeExceptionAsync(string exceptionId, string revokedBy, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(exceptionId)) throw new ArgumentException("Value cannot be null or empty.", nameof(exceptionId));
    if (string.IsNullOrEmpty(revokedBy)) throw new ArgumentException("Value cannot be null or empty.", nameof(revokedBy));
    await _repo.RevokeAsync(exceptionId, revokedBy, _clock.Now, ct).ConfigureAwait(false);
  }

  public async Task<IReadOnlyList<ControlException>> GetExpiredExceptionsAsync(string bundleRoot, CancellationToken ct)
  {
    var all = await _repo.ListByBundleAsync(bundleRoot, ct).ConfigureAwait(false);
    var now = _clock.Now;
    return all.Where(e => e.StatusValue == ExceptionStatus.Active && e.ExpiresAt != null && e.ExpiresAt <= now).ToList();
  }

  public async Task<IReadOnlyList<ControlException>> GetActiveExceptionsAsync(string bundleRoot, CancellationToken ct)
  {
    var all = await _repo.ListByBundleAsync(bundleRoot, ct).ConfigureAwait(false);
    var now = _clock.Now;
    return all.Where(e => e.StatusValue == ExceptionStatus.Active && (e.ExpiresAt == null || e.ExpiresAt > now)).ToList();
  }

  public async Task<bool> IsRuleCoveredByActiveExceptionAsync(string bundleRoot, string ruleId, CancellationToken ct)
  {
    var active = await _repo.ListActiveByRuleAsync(bundleRoot, ruleId, ct).ConfigureAwait(false);
    return active.Count > 0;
  }

  public async Task<ExceptionAuditReport> AuditExceptionsAsync(string bundleRoot, CancellationToken ct)
  {
    var all = await _repo.ListByBundleAsync(bundleRoot, ct).ConfigureAwait(false);
    var now = _clock.Now;
    var thirtyDaysOut = now.AddDays(30);

    var active = all.Where(e => e.StatusValue == ExceptionStatus.Active && (e.ExpiresAt == null || e.ExpiresAt > now)).ToList();
    var expired = all.Where(e => e.StatusValue == ExceptionStatus.Active && e.ExpiresAt != null && e.ExpiresAt <= now).ToList();
    var revoked = all.Where(e => e.StatusValue == ExceptionStatus.Revoked).ToList();
    var expiringSoon = active.Where(e => e.ExpiresAt != null && e.ExpiresAt <= thirtyDaysOut).ToList();
    var highRisk = active.Where(e => e.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase)).ToList();

    return new ExceptionAuditReport
    {
      BundleRoot = bundleRoot,
      GeneratedAt = now,
      ActiveCount = active.Count,
      ExpiredCount = expired.Count,
      RevokedCount = revoked.Count,
      ExpiringWithin30Days = expiringSoon.Count,
      HighRiskActiveCount = highRisk.Count,
      ExpiringExceptions = expiringSoon
    };
  }
}

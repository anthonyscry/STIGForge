namespace STIGForge.Core.Models;

/// <summary>
/// Time-bounded exception/waiver for a control rule, with approver tracking and risk level.
/// Exceptions can be Active, Expired (past ExpiresAt), or Revoked (manually revoked).
/// </summary>
public sealed class ControlException
{
  public string ExceptionId { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;
  public string RuleId { get; set; } = string.Empty;
  public string? VulnId { get; set; }
  public string ExceptionType { get; set; } = string.Empty;  // Waiver, RiskAcceptance, TechnicalException
  public string Status { get; set; } = "Active";              // Active, Expired, Revoked
  public string RiskLevel { get; set; } = string.Empty;       // High, Medium, Low
  public string ApprovedBy { get; set; } = string.Empty;
  public string? Justification { get; set; }
  public string? JustificationDoc { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ExpiresAt { get; set; }
  public DateTimeOffset? RevokedAt { get; set; }
  public string? RevokedBy { get; set; }
}

/// <summary>Request to create a new control exception.</summary>
public sealed class CreateExceptionRequest
{
  public string BundleRoot { get; set; } = string.Empty;
  public string RuleId { get; set; } = string.Empty;
  public string? VulnId { get; set; }
  public string ExceptionType { get; set; } = string.Empty;
  public string RiskLevel { get; set; } = string.Empty;
  public string ApprovedBy { get; set; } = string.Empty;
  public string? Justification { get; set; }
  public string? JustificationDoc { get; set; }
  public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>Audit report for exception health across a bundle.</summary>
public sealed class ExceptionAuditReport
{
  public string BundleRoot { get; set; } = string.Empty;
  public DateTimeOffset GeneratedAt { get; set; }
  public int ActiveCount { get; set; }
  public int ExpiredCount { get; set; }
  public int RevokedCount { get; set; }
  public int ExpiringWithin30Days { get; set; }
  public int HighRiskActiveCount { get; set; }
  public IReadOnlyList<ControlException> ExpiringExceptions { get; set; } = Array.Empty<ControlException>();
}

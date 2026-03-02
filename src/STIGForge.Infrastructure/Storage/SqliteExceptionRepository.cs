using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Infrastructure.Storage;

public sealed class SqliteExceptionRepository : IExceptionRepository
{
  private readonly string _cs;

  public SqliteExceptionRepository(string connectionString)
  {
    if (string.IsNullOrEmpty(connectionString)) throw new ArgumentException("Value cannot be null or empty.", nameof(connectionString));
    _cs = connectionString;
  }

  public async Task SaveAsync(ControlException exception, CancellationToken ct)
  {
    if (exception is null) throw new ArgumentNullException(nameof(exception));
    const string sql = @"
INSERT INTO control_exceptions(exception_id, bundle_root, rule_id, vuln_id, exception_type,
  status, risk_level, approved_by, justification, justification_doc, created_at, expires_at, revoked_at, revoked_by)
VALUES(@ExceptionId, @BundleRoot, @RuleId, @VulnId, @ExceptionType,
  @Status, @RiskLevel, @ApprovedBy, @Justification, @JustificationDoc, @CreatedAt, @ExpiresAt, @RevokedAt, @RevokedBy)
ON CONFLICT(exception_id) DO UPDATE SET
  status=excluded.status, revoked_at=excluded.revoked_at, revoked_by=excluded.revoked_by;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    await conn.ExecuteAsync(new CommandDefinition(sql, new
    {
      exception.ExceptionId,
      exception.BundleRoot,
      exception.RuleId,
      exception.VulnId,
      exception.ExceptionType,
      exception.Status,
      exception.RiskLevel,
      exception.ApprovedBy,
      exception.Justification,
      exception.JustificationDoc,
      CreatedAt = exception.CreatedAt.ToString("o"),
      ExpiresAt = exception.ExpiresAt?.ToString("o"),
      RevokedAt = exception.RevokedAt?.ToString("o"),
      exception.RevokedBy
    }, cancellationToken: ct)).ConfigureAwait(false);
  }

  public async Task<ControlException?> GetAsync(string exceptionId, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(exceptionId)) throw new ArgumentException("Value cannot be null or empty.", nameof(exceptionId));

    const string sql = @"
SELECT exception_id ExceptionId, bundle_root BundleRoot, rule_id RuleId, vuln_id VulnId,
  exception_type ExceptionType, status Status, risk_level RiskLevel, approved_by ApprovedBy,
  justification Justification, justification_doc JustificationDoc, created_at CreatedAt,
  expires_at ExpiresAt, revoked_at RevokedAt, revoked_by RevokedBy
FROM control_exceptions
WHERE exception_id=@exceptionId;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var row = await conn.QuerySingleOrDefaultAsync<ExceptionRow>(
      new CommandDefinition(sql, new { exceptionId }, cancellationToken: ct)).ConfigureAwait(false);
    return row is null ? null : MapException(row);
  }

  public async Task<IReadOnlyList<ControlException>> ListByBundleAsync(string bundleRoot, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));

    const string sql = @"
SELECT exception_id ExceptionId, bundle_root BundleRoot, rule_id RuleId, vuln_id VulnId,
  exception_type ExceptionType, status Status, risk_level RiskLevel, approved_by ApprovedBy,
  justification Justification, justification_doc JustificationDoc, created_at CreatedAt,
  expires_at ExpiresAt, revoked_at RevokedAt, revoked_by RevokedBy
FROM control_exceptions
WHERE bundle_root=@bundleRoot
ORDER BY created_at DESC;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var rows = await conn.QueryAsync<ExceptionRow>(
      new CommandDefinition(sql, new { bundleRoot }, cancellationToken: ct)).ConfigureAwait(false);
    return rows.Select(MapException).ToList();
  }

  public async Task<IReadOnlyList<ControlException>> ListActiveByRuleAsync(string bundleRoot, string ruleId, CancellationToken ct)
  {
    ArgumentException.ThrowIfNullOrEmpty(bundleRoot);
    if (string.IsNullOrEmpty(ruleId)) throw new ArgumentException("Value cannot be null or empty.", nameof(ruleId));

    const string sql = @"
SELECT exception_id ExceptionId, bundle_root BundleRoot, rule_id RuleId, vuln_id VulnId,
  exception_type ExceptionType, status Status, risk_level RiskLevel, approved_by ApprovedBy,
  justification Justification, justification_doc JustificationDoc, created_at CreatedAt,
  expires_at ExpiresAt, revoked_at RevokedAt, revoked_by RevokedBy
FROM control_exceptions
WHERE bundle_root=@bundleRoot AND rule_id=@ruleId AND status='Active' AND (expires_at IS NULL OR expires_at > @now)
ORDER BY created_at DESC;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var rows = await conn.QueryAsync<ExceptionRow>(
      new CommandDefinition(sql, new
      {
        bundleRoot,
        ruleId,
        now = DateTimeOffset.UtcNow.ToString("o")
      }, cancellationToken: ct)).ConfigureAwait(false);
    return rows.Select(MapException).ToList();
  }

  public async Task RevokeAsync(string exceptionId, string revokedBy, DateTimeOffset revokedAt, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(exceptionId)) throw new ArgumentException("Value cannot be null or empty.", nameof(exceptionId));
    if (string.IsNullOrEmpty(revokedBy)) throw new ArgumentException("Value cannot be null or empty.", nameof(revokedBy));

    const string sql = @"
UPDATE control_exceptions
SET status='Revoked', revoked_at=@revokedAt, revoked_by=@revokedBy
WHERE exception_id=@exceptionId;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    await conn.ExecuteAsync(new CommandDefinition(sql, new
    {
      exceptionId,
      revokedBy,
      revokedAt = revokedAt.ToString("o")
    }, cancellationToken: ct)).ConfigureAwait(false);
  }

  private static readonly global::System.Globalization.DateTimeStyles RoundtripStyle =
    global::System.Globalization.DateTimeStyles.RoundtripKind;

  private static ControlException MapException(ExceptionRow row) => new()
  {
    ExceptionId = row.ExceptionId,
    BundleRoot = row.BundleRoot,
    RuleId = row.RuleId,
    VulnId = row.VulnId,
    ExceptionType = row.ExceptionType,
    Status = row.Status,
    RiskLevel = row.RiskLevel,
    ApprovedBy = row.ApprovedBy,
    Justification = row.Justification,
    JustificationDoc = row.JustificationDoc,
    CreatedAt = DateTimeOffset.Parse(row.CreatedAt, null, RoundtripStyle),
    ExpiresAt = row.ExpiresAt is null ? null : DateTimeOffset.Parse(row.ExpiresAt, null, RoundtripStyle),
    RevokedAt = row.RevokedAt is null ? null : DateTimeOffset.Parse(row.RevokedAt, null, RoundtripStyle),
    RevokedBy = row.RevokedBy
  };

  private sealed class ExceptionRow
  {
    public string ExceptionId { get; set; } = string.Empty;
    public string BundleRoot { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string? VulnId { get; set; }
    public string ExceptionType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public string? Justification { get; set; }
    public string? JustificationDoc { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
    public string? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
  }
}

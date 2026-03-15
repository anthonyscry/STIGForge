using System.Diagnostics;

namespace STIGForge.Core.Abstractions;

/// <summary>
/// Extension methods for <see cref="IAuditTrailService"/> that provide
/// fire-and-forget audit recording with consistent error handling.
/// </summary>
public static class AuditTrailExtensions
{
  /// <summary>
  /// Records an audit entry, swallowing failures with a trace warning.
  /// Use this for non-blocking audit writes where the caller must not fail
  /// due to audit infrastructure issues.
  /// </summary>
  public static async Task SafeRecordAsync(this IAuditTrailService? audit, AuditEntry entry, CancellationToken ct)
  {
    if (audit == null) return;

    try
    {
      await audit.RecordAsync(entry, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      Trace.TraceWarning("Audit write failed (non-blocking): " + ex.Message);
    }
  }
}

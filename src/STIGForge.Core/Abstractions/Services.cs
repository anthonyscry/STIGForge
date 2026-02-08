using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

public interface IClock
{
  DateTimeOffset Now { get; }
}

/// <summary>
/// Tamper-evident audit trail for compliance-relevant actions.
/// Each entry is chained to the previous via SHA-256 hash for integrity verification.
/// </summary>
public interface IAuditTrailService
{
  Task RecordAsync(AuditEntry entry, CancellationToken ct);
  Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct);
  Task<bool> VerifyIntegrityAsync(CancellationToken ct);
}

public sealed class AuditEntry
{
  public long Id { get; set; }
  public DateTimeOffset Timestamp { get; set; }
  public string User { get; set; } = string.Empty;
  public string Machine { get; set; } = string.Empty;
  public string Action { get; set; } = string.Empty;
  public string Target { get; set; } = string.Empty;
  public string Result { get; set; } = string.Empty;
  public string Detail { get; set; } = string.Empty;
  public string PreviousHash { get; set; } = string.Empty;
  public string EntryHash { get; set; } = string.Empty;
}

public sealed class AuditQuery
{
  public string? Action { get; set; }
  public string? Target { get; set; }
  public DateTimeOffset? From { get; set; }
  public DateTimeOffset? To { get; set; }
  public int Limit { get; set; } = 100;
}

public interface IHashingService
{
  Task<string> Sha256FileAsync(string path, CancellationToken ct);
  Task<string> Sha256TextAsync(string content, CancellationToken ct);
}

public interface IPathBuilder
{
  string GetAppDataRoot();
  string GetContentPacksRoot();
  string GetPackRoot(string packId);
  string GetBundleRoot(string bundleId);
  string GetLogsRoot();
  string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts);
}

public interface IClassificationScopeService
{
  CompiledControls Compile(Profile profile, IReadOnlyList<ControlRecord> controls);
}

public sealed class CompiledControl
{
  public CompiledControl(ControlRecord control, ControlStatus status, string? comment, bool needsReview, string? reviewReason)
  {
    Control = control;
    Status = status;
    Comment = comment;
    NeedsReview = needsReview;
    ReviewReason = reviewReason;
  }

  public ControlRecord Control { get; }
  public ControlStatus Status { get; }
  public string? Comment { get; }
  public bool NeedsReview { get; }
  public string? ReviewReason { get; }
}

public sealed class CompiledControls
{
  public CompiledControls(IReadOnlyList<CompiledControl> controls, IReadOnlyList<CompiledControl> reviewQueue)
  {
    Controls = controls;
    ReviewQueue = reviewQueue;
  }

  public IReadOnlyList<CompiledControl> Controls { get; }
  public IReadOnlyList<CompiledControl> ReviewQueue { get; }
}

/// <summary>
/// Secure credential storage for fleet operations using DPAPI (Windows Data Protection).
/// Credentials are encrypted per-user and stored locally.
/// </summary>
public interface ICredentialStore
{
  void Save(string targetHost, string username, string password);
  (string Username, string Password)? Load(string targetHost);
  bool Remove(string targetHost);
  IReadOnlyList<string> ListHosts();
}

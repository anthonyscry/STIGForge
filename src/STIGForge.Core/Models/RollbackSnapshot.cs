namespace STIGForge.Core.Models;

public sealed class RollbackSnapshot
{
  public string SnapshotId { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
  public IReadOnlyList<RollbackRegistryKeyState> RegistryKeys { get; set; } = Array.Empty<RollbackRegistryKeyState>();
  public IReadOnlyList<RollbackFilePathState> FilePaths { get; set; } = Array.Empty<RollbackFilePathState>();
  public IReadOnlyList<RollbackServiceState> ServiceStates { get; set; } = Array.Empty<RollbackServiceState>();
  public IReadOnlyList<RollbackGpoSettingState> GpoSettings { get; set; } = Array.Empty<RollbackGpoSettingState>();
  public string RollbackScriptPath { get; set; } = string.Empty;
}

public sealed class RollbackRegistryKeyState
{
  public string Path { get; set; } = string.Empty;
  public string ValueName { get; set; } = string.Empty;
  public string? Value { get; set; }
  public string ValueType { get; set; } = string.Empty;
  public bool Exists { get; set; }
}

public sealed class RollbackFilePathState
{
  public string Path { get; set; } = string.Empty;
  public bool Exists { get; set; }
  public string? Sha256 { get; set; }
}

public sealed class RollbackServiceState
{
  public string ServiceName { get; set; } = string.Empty;
  public string StartupType { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
}

public sealed class RollbackGpoSettingState
{
  public string SettingPath { get; set; } = string.Empty;
  public string? Value { get; set; }
  public string? GpoName { get; set; }
}

public sealed class RollbackApplyResult
{
  public string SnapshotId { get; set; } = string.Empty;
  public bool Success { get; set; }
  public int ExitCode { get; set; }
  public string Output { get; set; } = string.Empty;
  public string Error { get; set; } = string.Empty;
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset FinishedAt { get; set; }
}

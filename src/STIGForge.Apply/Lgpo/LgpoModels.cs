namespace STIGForge.Apply.Lgpo;

public enum LgpoScope
{
  Machine,
  User
}

public sealed class LgpoApplyRequest
{
  public string PolFilePath { get; set; } = string.Empty;
  public LgpoScope Scope { get; set; } = LgpoScope.Machine;
  public string? LgpoExePath { get; set; }
}

public sealed class LgpoApplyResult
{
  public bool Success { get; set; }
  public int ExitCode { get; set; }
  public string StdOut { get; set; } = string.Empty;
  public string StdErr { get; set; } = string.Empty;
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset FinishedAt { get; set; }
}

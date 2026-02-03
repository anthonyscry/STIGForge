namespace STIGForge.Apply.Snapshot;

public sealed class SnapshotResult
{
    public string SnapshotId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string SecurityPolicyPath { get; set; } = string.Empty;
    public string AuditPolicyPath { get; set; } = string.Empty;
    public string? LgpoStatePath { get; set; }
    public string RollbackScriptPath { get; set; } = string.Empty;
}

public sealed class SnapshotException : Exception
{
    public SnapshotException(string message) : base(message) { }
    
    public SnapshotException(string message, Exception innerException) : base(message, innerException) { }
}

using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

/// <summary>
/// Atomic remediation handler for a single STIG rule. Each handler knows how to
/// test compliance and apply the fix for one specific control.
/// </summary>
public interface IRemediationHandler
{
    /// <summary>Rule ID this handler remediates (e.g., "SV-12345").</summary>
    string RuleId { get; }

    /// <summary>Handler category: Registry, Service, AuditPolicy, FilePermission, SecurityOption.</summary>
    string Category { get; }

    /// <summary>Human-readable description of the remediation.</summary>
    string Description { get; }

    /// <summary>Tests current compliance state without making changes.</summary>
    Task<RemediationResult> TestAsync(RemediationContext context, CancellationToken ct);

    /// <summary>Applies the remediation fix.</summary>
    Task<RemediationResult> ApplyAsync(RemediationContext context, CancellationToken ct);
}

public sealed class RemediationContext
{
    public string BundleRoot { get; set; } = string.Empty;
    public HardeningMode Mode { get; set; }
    public bool DryRun { get; set; }
    public ControlRecord Control { get; set; } = new();
}

public sealed class RemediationResult
{
    public string RuleId { get; set; } = string.Empty;
    public string HandlerCategory { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool Changed { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Detail { get; set; }
}

public sealed class RemediationRunResult
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public int TotalHandled { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int ChangedCount { get; set; }
    public int SkippedCount { get; set; }
    public IReadOnlyList<RemediationResult> Results { get; set; } = Array.Empty<RemediationResult>();
}

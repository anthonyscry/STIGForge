namespace STIGForge.Apply;

/// <summary>
/// Structured report produced by a dry-run apply, detailing every change that
/// WOULD be made without actually modifying the system.
/// </summary>
public sealed class DryRunReport
{
    public string BundleRoot { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public int TotalChanges { get; set; }
    public IReadOnlyList<DryRunChange> Changes { get; set; } = Array.Empty<DryRunChange>();
    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();
}

/// <summary>
/// A single proposed change identified during dry-run simulation.
/// </summary>
public sealed class DryRunChange
{
    /// <summary>Apply step that would make this change (e.g., "PowerStig", "DSC", "LGPO", "Script").</summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>Human-readable description of the change.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Current value on the system (null if not yet set).</summary>
    public string? CurrentValue { get; set; }

    /// <summary>Value that would be applied.</summary>
    public string? ProposedValue { get; set; }

    /// <summary>Rule ID associated with this change, if applicable.</summary>
    public string? RuleId { get; set; }

    /// <summary>Resource type (e.g., "Registry", "Service", "AuditPolicy", "GroupPolicy").</summary>
    public string? ResourceType { get; set; }

    /// <summary>Resource path or identifier (e.g., registry key path, service name).</summary>
    public string? ResourcePath { get; set; }
}

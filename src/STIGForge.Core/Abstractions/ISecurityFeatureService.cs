using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

/// <summary>
/// Interface for advanced Windows security feature services (WDAC, BitLocker, Firewall).
/// Each implementation manages a specific security subsystem.
/// </summary>
public interface ISecurityFeatureService
{
    string FeatureName { get; }
    Task<SecurityFeatureStatus> GetStatusAsync(CancellationToken ct);
    Task<SecurityFeatureResult> ApplyAsync(SecurityFeatureRequest request, CancellationToken ct);
    Task<SecurityFeatureResult> TestAsync(SecurityFeatureRequest request, CancellationToken ct);
}

public sealed class SecurityFeatureRequest
{
    public string BundleRoot { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public HardeningMode Mode { get; set; }
    public string? ConfigPath { get; set; }
}

public sealed class SecurityFeatureStatus
{
    public string FeatureName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}

public sealed class SecurityFeatureResult
{
    public string FeatureName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool Changed { get; set; }
    public string? PreviousState { get; set; }
    public string? NewState { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Detail { get; set; }
    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();
}

public sealed class SecurityRunResult
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public int TotalFeatures { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int ChangedCount { get; set; }
    public IReadOnlyList<SecurityFeatureResult> Results { get; set; } = Array.Empty<SecurityFeatureResult>();
}

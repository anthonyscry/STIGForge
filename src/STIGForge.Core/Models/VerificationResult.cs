namespace STIGForge.Core.Models;

/// <summary>
/// Canonical verification result contract. Defines the cross-module schema for
/// verification outcomes. Implementation modules (STIGForge.Verify) retain
/// their own richer types; this type represents the published contract.
/// </summary>
public sealed class VerificationResult
{
    /// <summary>Control identifier (VulnId preferred, fallback to RuleId)</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>VulnId if available</summary>
    public string? VulnId { get; set; }

    /// <summary>RuleId if available</summary>
    public string? RuleId { get; set; }

    /// <summary>Verification status: Pass, Fail, NotApplicable, NotReviewed, Error</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Tool that generated this result</summary>
    public string Tool { get; set; } = string.Empty;

    /// <summary>When this verification was performed</summary>
    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>SCAP benchmark ID this result maps to</summary>
    public string? BenchmarkId { get; set; }

    /// <summary>Schema version for contract compatibility</summary>
    public string SchemaVersion { get; set; } = CanonicalContract.Version;
}

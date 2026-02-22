namespace STIGForge.Core.Models;

/// <summary>
/// Canonical evidence record contract. Defines the cross-module schema for
/// evidence artifacts. Implementation modules (STIGForge.Evidence) retain
/// their own richer types; this type represents the published contract.
/// </summary>
public sealed class EvidenceRecord
{
    /// <summary>Control identifier this evidence supports</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>RuleId if available</summary>
    public string? RuleId { get; set; }

    /// <summary>Evidence artifact type (Command, File, Registry, PolicyExport, Screenshot, Other)</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the evidence artifact</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>UTC timestamp when evidence was collected</summary>
    public string TimestampUtc { get; set; } = string.Empty;

    /// <summary>Run ID for apply-run provenance linkage</summary>
    public string? RunId { get; set; }

    /// <summary>Schema version for contract compatibility</summary>
    public string SchemaVersion { get; set; } = CanonicalContract.Version;
}

namespace STIGForge.Core.Models;

/// <summary>
/// Canonical export index entry contract. Represents a single file entry
/// in an export package index (e.g., eMASS package manifest).
/// </summary>
public sealed class ExportIndexEntry
{
    /// <summary>Relative file path within the export package</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Artifact type classification (e.g., CKL, POA&M, Attestation, Evidence, Manifest)</summary>
    public string ArtifactType { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the file</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the file was written</summary>
    public string TimestampUtc { get; set; } = string.Empty;

    /// <summary>Schema version for contract compatibility</summary>
    public string SchemaVersion { get; set; } = CanonicalContract.Version;
}

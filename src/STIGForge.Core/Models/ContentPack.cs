namespace STIGForge.Core.Models;

public sealed class ContentPack
{
  public string PackId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public DateTimeOffset ImportedAt { get; set; }
  public DateTimeOffset? ReleaseDate { get; set; }
  public string SourceLabel { get; set; } = string.Empty;
  public string HashAlgorithm { get; set; } = "sha256";
  public string ManifestSha256 { get; set; } = string.Empty;

  /// <summary>
  /// List of SCAP benchmark IDs this pack maps to (e.g., ["Windows_10_STIG", "Windows_Server_2022_STIG"]).
  /// Populated at import time from parsed XCCDF/OVAL metadata.
  /// </summary>
  public IReadOnlyList<string> BenchmarkIds { get; set; } = Array.Empty<string>();

  /// <summary>
  /// Flat string tags for downstream filtering and applicability matching
  /// (e.g., ["Win10", "Server2022", "MemberServer", "DomainController"]).
  /// </summary>
  public IReadOnlyList<string> ApplicabilityTags { get; set; } = Array.Empty<string>();

  /// <summary>
  /// Pack-level benchmark version string (e.g., "V2R7").
  /// </summary>
  public string Version { get; set; } = string.Empty;

  /// <summary>
  /// Pack-level benchmark release string (e.g., "1").
  /// </summary>
  public string Release { get; set; } = string.Empty;

  public string SchemaVersion { get; set; } = CanonicalContract.Version;
}

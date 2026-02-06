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
  public string SchemaVersion { get; set; } = CanonicalContract.Version;
}

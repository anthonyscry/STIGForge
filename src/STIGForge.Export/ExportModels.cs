namespace STIGForge.Export;

public sealed class ExportRequest
{
  public string BundleRoot { get; set; } = string.Empty;
  public string? OutputRoot { get; set; }
}

public sealed class ExportResult
{
  public string OutputRoot { get; set; } = string.Empty;
  public string ManifestPath { get; set; } = string.Empty;
  public string IndexPath { get; set; } = string.Empty;
  public ValidationResult? ValidationResult { get; set; }
}

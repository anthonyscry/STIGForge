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
  public string ValidationReportPath { get; set; } = string.Empty;
  public string ValidationReportJsonPath { get; set; } = string.Empty;
  public ValidationResult? ValidationResult { get; set; }
}

public sealed class ValidationResult
{
  public bool IsValid { get; set; }
  public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
  public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
  public string PackageRoot { get; set; } = string.Empty;
  public DateTimeOffset ValidatedAt { get; set; }
  public ValidationMetrics Metrics { get; set; } = new();

  public static ValidationResult Failure(string error)
  {
    return new ValidationResult
    {
      IsValid = false,
      Errors = new[] { error },
      ValidatedAt = DateTimeOffset.Now
    };
  }
}

public sealed class ValidationMetrics
{
  public int RequiredDirectoriesChecked { get; set; }
  public int MissingRequiredDirectoryCount { get; set; }

  public int RequiredFilesChecked { get; set; }
  public int MissingRequiredFileCount { get; set; }

  public int HashManifestEntryCount { get; set; }
  public int HashedFileCount { get; set; }
  public int HashMismatchCount { get; set; }

  public int IndexedControlCount { get; set; }
  public int PoamItemCount { get; set; }
  public int AttestationCount { get; set; }

  public int CrossArtifactMismatchCount { get; set; }
}

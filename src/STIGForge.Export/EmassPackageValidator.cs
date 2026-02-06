using System.Security.Cryptography;
using System.Text;

namespace STIGForge.Export;

/// <summary>
/// Validates eMASS export packages for completeness and integrity.
/// Verifies required files, validates file hashes, checks package structure.
/// </summary>
public sealed class EmassPackageValidator
{
  /// <summary>
  /// Validate an eMASS export package.
  /// </summary>
  public ValidationResult ValidatePackage(string packageRoot)
  {
    if (!Directory.Exists(packageRoot))
      return ValidationResult.Failure("Package root directory not found: " + packageRoot);

    var errors = new List<string>();
    var warnings = new List<string>();

    // Check directory structure
    ValidateDirectoryStructure(packageRoot, errors, warnings);

    // Check required files
    ValidateRequiredFiles(packageRoot, errors, warnings);

    // Validate file hashes
    ValidateFileHashes(packageRoot, errors, warnings);

    // Validate content integrity
    ValidateContentIntegrity(packageRoot, errors, warnings);

    var isValid = errors.Count == 0;

    return new ValidationResult
    {
      IsValid = isValid,
      Errors = errors,
      Warnings = warnings,
      PackageRoot = packageRoot,
      ValidatedAt = DateTimeOffset.Now
    };
  }

  private static void ValidateDirectoryStructure(string root, List<string> errors, List<string> warnings)
  {
    var requiredDirs = new[]
    {
      "00_Manifest",
      "01_Scans",
      "02_Checklists",
      "03_POAM",
      "04_Evidence",
      "05_Attestations",
      "06_Index"
    };

    foreach (var dir in requiredDirs)
    {
      var path = Path.Combine(root, dir);
      if (!Directory.Exists(path))
        errors.Add($"Required directory missing: {dir}");
    }
  }

  private static void ValidateRequiredFiles(string root, List<string> errors, List<string> warnings)
  {
    var requiredFiles = new Dictionary<string, string>
    {
      ["00_Manifest/manifest.json"] = "Bundle manifest",
      ["00_Manifest/file_hashes.sha256"] = "File hash manifest",
      ["03_POAM/poam.json"] = "POA&M data",
      ["03_POAM/poam.csv"] = "POA&M CSV export",
      ["05_Attestations/attestations.json"] = "Attestation records",
      ["06_Index/control_evidence_index.csv"] = "Control evidence index",
      ["README_Submission.txt"] = "Submission readme"
    };

    foreach (var kvp in requiredFiles)
    {
      var path = Path.Combine(root, kvp.Key);
      if (!File.Exists(path))
        errors.Add($"Required file missing: {kvp.Key} ({kvp.Value})");
    }

    // Check for scan outputs
    var scansDir = Path.Combine(root, "01_Scans");
    if (Directory.Exists(scansDir))
    {
      var scanFiles = Directory.GetFiles(scansDir, "*", SearchOption.AllDirectories);
      if (scanFiles.Length == 0)
        warnings.Add("No scan output files found in 01_Scans/");
    }

    // Check for evidence files
    var evidenceDir = Path.Combine(root, "04_Evidence");
    if (Directory.Exists(evidenceDir))
    {
      var evidenceFiles = Directory.GetFiles(evidenceDir, "*", SearchOption.AllDirectories);
      if (evidenceFiles.Length == 0)
        warnings.Add("No evidence files found in 04_Evidence/ - ensure evidence is collected");
    }
  }

  private static void ValidateFileHashes(string root, List<string> errors, List<string> warnings)
  {
    var hashManifestPath = Path.Combine(root, "00_Manifest", "file_hashes.sha256");
    if (!File.Exists(hashManifestPath))
    {
      errors.Add("Hash manifest missing - cannot validate file integrity");
      return;
    }

    var manifestHashes = ParseHashManifest(hashManifestPath);
    var actualFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
      .Where(f => !string.Equals(f, hashManifestPath, StringComparison.OrdinalIgnoreCase))
      .ToList();

    // Check for files in manifest but missing from package
    foreach (var manifestEntry in manifestHashes)
    {
      var fullPath = Path.Combine(root, manifestEntry.Key);
      if (!File.Exists(fullPath))
        errors.Add($"File in hash manifest but missing from package: {manifestEntry.Key}");
    }

    // Check for files in package but missing from manifest
    foreach (var file in actualFiles)
    {
      var relativePath = GetRelativePath(root, file);
      if (!manifestHashes.ContainsKey(relativePath))
        warnings.Add($"File in package but not in hash manifest: {relativePath}");
    }

    // Validate hashes for existing files
    var hashMismatches = 0;
    foreach (var manifestEntry in manifestHashes)
    {
      var fullPath = Path.Combine(root, manifestEntry.Key);
      if (!File.Exists(fullPath))
        continue;

      var actualHash = ComputeSha256(fullPath);
      if (!string.Equals(actualHash, manifestEntry.Value, StringComparison.OrdinalIgnoreCase))
      {
        errors.Add($"Hash mismatch for {manifestEntry.Key}: expected {manifestEntry.Value}, got {actualHash}");
        hashMismatches++;
      }
    }

    if (hashMismatches > 0)
      errors.Add($"Total files with hash mismatches: {hashMismatches}");
  }

  private static void ValidateContentIntegrity(string root, List<string> errors, List<string> warnings)
  {
    // Validate manifest.json is valid JSON
    var manifestPath = Path.Combine(root, "00_Manifest", "manifest.json");
    if (File.Exists(manifestPath))
    {
      try
      {
        var json = File.ReadAllText(manifestPath);
        System.Text.Json.JsonDocument.Parse(json);
      }
      catch (Exception ex)
      {
        errors.Add($"Invalid manifest.json: {ex.Message}");
      }
    }

    // Validate POA&M JSON
    var poamPath = Path.Combine(root, "03_POAM", "poam.json");
    if (File.Exists(poamPath))
    {
      try
      {
        var json = File.ReadAllText(poamPath);
        System.Text.Json.JsonDocument.Parse(json);
      }
      catch (Exception ex)
      {
        errors.Add($"Invalid poam.json: {ex.Message}");
      }
    }

    // Validate control evidence index has content
    var indexPath = Path.Combine(root, "06_Index", "control_evidence_index.csv");
    if (File.Exists(indexPath))
    {
      var lines = File.ReadAllLines(indexPath);
      if (lines.Length < 2)
        warnings.Add("control_evidence_index.csv appears empty (only header or no content)");
    }
  }

  private static Dictionary<string, string> ParseHashManifest(string path)
  {
    var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var lines = File.ReadAllLines(path);

    foreach (var line in lines)
    {
      if (string.IsNullOrWhiteSpace(line))
        continue;

      // Format: <hash>  <filepath>
      var parts = line.Split(new[] { "  " }, 2, StringSplitOptions.None);
      if (parts.Length == 2)
      {
        var hash = parts[0].Trim();
        var filePath = parts[1].Trim();
        hashes[filePath] = hash;
      }
    }

    return hashes;
  }

  private static string ComputeSha256(string filePath)
  {
    using var stream = File.OpenRead(filePath);
    using var sha256 = SHA256.Create();
    var hashBytes = sha256.ComputeHash(stream);
    return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
  }

  private static string GetRelativePath(string root, string path)
  {
    var rootUri = new Uri(AppendDirSeparator(root));
    var pathUri = new Uri(path);
    var relativeUri = rootUri.MakeRelativeUri(pathUri);
    return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
  }

  private static string AppendDirSeparator(string path)
  {
    if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
      return path + Path.DirectorySeparatorChar;
    return path;
  }

  /// <summary>
  /// Write validation report to file.
  /// </summary>
  public void WriteValidationReport(ValidationResult result, string outputPath)
  {
    var sb = new StringBuilder(2048);
    sb.AppendLine("eMASS Package Validation Report");
    sb.AppendLine("================================");
    sb.AppendLine();
    sb.AppendLine($"Package: {result.PackageRoot}");
    sb.AppendLine($"Validated: {result.ValidatedAt:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine($"Status: {(result.IsValid ? "VALID" : "INVALID")}");
    sb.AppendLine();

    if (result.Errors.Count > 0)
    {
      sb.AppendLine($"ERRORS ({result.Errors.Count}):");
      foreach (var error in result.Errors)
        sb.AppendLine($"  - {error}");
      sb.AppendLine();
    }

    if (result.Warnings.Count > 0)
    {
      sb.AppendLine($"WARNINGS ({result.Warnings.Count}):");
      foreach (var warning in result.Warnings)
        sb.AppendLine($"  - {warning}");
      sb.AppendLine();
    }

    if (result.IsValid && result.Warnings.Count == 0)
    {
      sb.AppendLine("Package passed all validation checks.");
      sb.AppendLine("Ready for eMASS submission.");
    }
    else if (result.IsValid && result.Warnings.Count > 0)
    {
      sb.AppendLine("Package is valid but has warnings.");
      sb.AppendLine("Review warnings before submission.");
    }
    else
    {
      sb.AppendLine("Package failed validation.");
      sb.AppendLine("Address all errors before submission.");
    }

    File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
  }
}

/// <summary>
/// Result of package validation.
/// </summary>
public sealed class ValidationResult
{
  public bool IsValid { get; set; }
  public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
  public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
  public string PackageRoot { get; set; } = string.Empty;
  public DateTimeOffset ValidatedAt { get; set; }

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

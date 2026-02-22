using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    var metrics = new ValidationMetrics();

    // Check directory structure
    ValidateDirectoryStructure(packageRoot, errors, warnings, metrics);

    // Check required files
    ValidateRequiredFiles(packageRoot, errors, warnings, metrics);

    // Validate file hashes
    ValidateFileHashes(packageRoot, errors, warnings, metrics);

    // Validate content integrity
    ValidateContentIntegrity(packageRoot, errors, warnings, metrics);

    // Validate package-level hash
    ValidatePackageHash(packageRoot, errors, warnings, metrics);

    // Validate submission readiness
    ValidateSubmissionReadiness(packageRoot, errors, warnings, metrics);

    var isValid = errors.Count == 0;

    return new ValidationResult
    {
      IsValid = isValid,
      Errors = errors,
      Warnings = warnings,
      PackageRoot = packageRoot,
      ValidatedAt = DateTimeOffset.Now,
      Metrics = metrics
    };
  }

  private static void ValidateDirectoryStructure(string root, List<string> errors, List<string> warnings, ValidationMetrics metrics)
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

    metrics.RequiredDirectoriesChecked = requiredDirs.Length;

    foreach (var dir in requiredDirs)
    {
      var path = Path.Combine(root, dir);
      if (!Directory.Exists(path))
      {
        errors.Add($"Required directory missing: {dir}");
        metrics.MissingRequiredDirectoryCount++;
      }
    }
  }

  private static void ValidateRequiredFiles(string root, List<string> errors, List<string> warnings, ValidationMetrics metrics)
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

    metrics.RequiredFilesChecked = requiredFiles.Count;

    foreach (var kvp in requiredFiles)
    {
      var path = Path.Combine(root, kvp.Key);
      if (!File.Exists(path))
      {
        errors.Add($"Required file missing: {kvp.Key} ({kvp.Value})");
        metrics.MissingRequiredFileCount++;
      }
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

  private static void ValidateFileHashes(string root, List<string> errors, List<string> warnings, ValidationMetrics metrics)
  {
    var hashManifestPath = Path.Combine(root, "00_Manifest", "file_hashes.sha256");
    if (!File.Exists(hashManifestPath))
    {
      errors.Add("Hash manifest missing - cannot validate file integrity");
      return;
    }

    var manifestHashes = ParseHashManifest(hashManifestPath);
    metrics.HashManifestEntryCount = manifestHashes.Count;
    var actualFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
      .Where(f => !string.Equals(f, hashManifestPath, StringComparison.OrdinalIgnoreCase))
      .Where(f => !IsValidationReportRelativePath(GetRelativePath(root, f)))
      .ToList();
    metrics.HashedFileCount = actualFiles.Count;

    // Check for files in manifest but missing from package
    foreach (var manifestEntry in manifestHashes)
    {
      if (IsValidationReportRelativePath(manifestEntry.Key))
        continue;

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
      if (IsValidationReportRelativePath(manifestEntry.Key))
        continue;

      var fullPath = Path.Combine(root, manifestEntry.Key);
      if (!File.Exists(fullPath))
        continue;

      var actualHash = ComputeSha256(fullPath);
      if (!string.Equals(actualHash, manifestEntry.Value, StringComparison.OrdinalIgnoreCase))
      {
        errors.Add($"Hash mismatch for {manifestEntry.Key}: expected {manifestEntry.Value}, got {actualHash}");
        hashMismatches++;
        metrics.HashMismatchCount++;
      }
    }

    if (hashMismatches > 0)
      errors.Add($"Total files with hash mismatches: {hashMismatches}");
  }

  private static void ValidateContentIntegrity(string root, List<string> errors, List<string> warnings, ValidationMetrics metrics)
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

    ValidateCrossArtifactConsistency(root, errors, warnings, metrics);
  }

  private static void ValidateCrossArtifactConsistency(string root, List<string> errors, List<string> warnings, ValidationMetrics metrics)
  {
    var indexPath = Path.Combine(root, "06_Index", "control_evidence_index.csv");
    var poamPath = Path.Combine(root, "03_POAM", "poam.json");
    var attPath = Path.Combine(root, "05_Attestations", "attestations.json");

    var indexRows = ParseIndexRows(indexPath);
    metrics.IndexedControlCount = indexRows.Count;

    var poamItems = LoadPoamItems(poamPath, errors);
    metrics.PoamItemCount = poamItems.Count;

    var attestations = LoadAttestations(attPath, errors);
    metrics.AttestationCount = attestations.Count;

    var indexRuleIds = new HashSet<string>(indexRows.Where(r => !string.IsNullOrWhiteSpace(r.RuleId)).Select(r => r.RuleId!), StringComparer.OrdinalIgnoreCase);
    var indexVulnIds = new HashSet<string>(indexRows.Where(r => !string.IsNullOrWhiteSpace(r.VulnId)).Select(r => r.VulnId!), StringComparer.OrdinalIgnoreCase);

    var poamMatchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var poam in poamItems)
    {
      var matched = false;
      if (!string.IsNullOrWhiteSpace(poam.RuleId) && indexRuleIds.Contains(poam.RuleId!))
      {
        matched = true;
        poamMatchedKeys.Add("RULE:" + poam.RuleId);
      }

      if (!matched && !string.IsNullOrWhiteSpace(poam.VulnId) && indexVulnIds.Contains(poam.VulnId!))
      {
        matched = true;
        poamMatchedKeys.Add("VULN:" + poam.VulnId);
      }

      if (!matched)
      {
        var controlId = string.IsNullOrWhiteSpace(poam.ControlId) ? "(missing control id)" : poam.ControlId;
        errors.Add("POA&M item does not map to an indexed control: " + controlId);
        metrics.CrossArtifactMismatchCount++;
      }
    }

    foreach (var row in indexRows)
    {
      var key = !string.IsNullOrWhiteSpace(row.RuleId) ? "RULE:" + row.RuleId : "VULN:" + row.VulnId;
      if (string.Equals(row.Status, "Fail", StringComparison.OrdinalIgnoreCase) && !poamMatchedKeys.Contains(key))
      {
        errors.Add("Failed control missing POA&M entry: " + key);
        metrics.CrossArtifactMismatchCount++;
      }
    }

    var validAttestationStatuses = new HashSet<string>(new[] { "Pending", "Compliant", "NonCompliant", "PartiallyCompliant" }, StringComparer.OrdinalIgnoreCase);
    foreach (var att in attestations)
    {
      if (string.IsNullOrWhiteSpace(att.ControlId))
      {
        warnings.Add("Attestation record contains empty ControlId.");
        continue;
      }

      var inIndex = indexRuleIds.Contains(att.ControlId) || indexVulnIds.Contains(att.ControlId);
      if (!inIndex)
      {
        warnings.Add("Attestation references non-indexed control: " + att.ControlId);
      }

      if (!validAttestationStatuses.Contains(att.ComplianceStatus ?? string.Empty))
      {
        warnings.Add("Attestation has unsupported compliance status for control " + att.ControlId + ": " + att.ComplianceStatus);
      }
    }
  }

  private static List<IndexRow> ParseIndexRows(string indexPath)
  {
    var rows = new List<IndexRow>();
    if (!File.Exists(indexPath))
      return rows;

    foreach (var line in File.ReadAllLines(indexPath).Skip(1))
    {
      if (string.IsNullOrWhiteSpace(line))
        continue;

      var parts = ParseCsvLine(line);
      if (parts.Length < 5)
        continue;

      rows.Add(new IndexRow
      {
        VulnId = NormalizeEmpty(parts[0]),
        RuleId = NormalizeEmpty(parts[1]),
        Status = NormalizeEmpty(parts[4]) ?? string.Empty
      });
    }

    return rows;
  }

  private static IReadOnlyList<PoamItem> LoadPoamItems(string poamPath, List<string> errors)
  {
    if (!File.Exists(poamPath))
      return Array.Empty<PoamItem>();

    try
    {
      var json = File.ReadAllText(poamPath);
      var package = JsonSerializer.Deserialize<PoamPackage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
      return package?.Items ?? Array.Empty<PoamItem>();
    }
    catch (Exception ex)
    {
      errors.Add("Unable to parse poam.json for cross-artifact validation: " + ex.Message);
      return Array.Empty<PoamItem>();
    }
  }

  private static IReadOnlyList<AttestationRecord> LoadAttestations(string attestationPath, List<string> errors)
  {
    if (!File.Exists(attestationPath))
      return Array.Empty<AttestationRecord>();

    try
    {
      var json = File.ReadAllText(attestationPath);
      var package = JsonSerializer.Deserialize<AttestationPackage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
      return package?.Attestations ?? Array.Empty<AttestationRecord>();
    }
    catch (Exception ex)
    {
      errors.Add("Unable to parse attestations.json for cross-artifact validation: " + ex.Message);
      return Array.Empty<AttestationRecord>();
    }
  }

  private static string? NormalizeEmpty(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return null;

    return value!.Trim();
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
    sb.AppendLine("SUMMARY:");
    sb.AppendLine($"  Required directories checked: {result.Metrics.RequiredDirectoriesChecked}");
    sb.AppendLine($"  Missing required directories: {result.Metrics.MissingRequiredDirectoryCount}");
    sb.AppendLine($"  Required files checked: {result.Metrics.RequiredFilesChecked}");
    sb.AppendLine($"  Missing required files: {result.Metrics.MissingRequiredFileCount}");
    sb.AppendLine($"  Hash manifest entries: {result.Metrics.HashManifestEntryCount}");
    sb.AppendLine($"  Hashed files: {result.Metrics.HashedFileCount}");
    sb.AppendLine($"  Hash mismatches: {result.Metrics.HashMismatchCount}");
    sb.AppendLine($"  Indexed controls: {result.Metrics.IndexedControlCount}");
    sb.AppendLine($"  POA&M items: {result.Metrics.PoamItemCount}");
    sb.AppendLine($"  Attestations: {result.Metrics.AttestationCount}");
    sb.AppendLine($"  Cross-artifact mismatches: {result.Metrics.CrossArtifactMismatchCount}");
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

    // Package Integrity section
    sb.AppendLine("PACKAGE INTEGRITY:");
    if (result.Metrics.PackageHashExpected != null)
    {
      sb.AppendLine($"  Package hash: {(result.Metrics.PackageHashValid ? "VALID" : "INVALID")} ({(result.Metrics.PackageHashValid ? "matches" : "DOES NOT match")} file_hashes.sha256)");
    }
    else
    {
      sb.AppendLine("  Package hash: NOT PRESENT (no packageHash in manifest)");
    }
    sb.AppendLine($"  Hash manifest entries: {result.Metrics.HashManifestEntryCount}");
    sb.AppendLine($"  File hash mismatches: {result.Metrics.HashMismatchCount}");
    sb.AppendLine();

    // Submission Readiness section
    if (result.Metrics.SubmissionReadiness != null)
    {
      var sr = result.Metrics.SubmissionReadiness;
      sb.AppendLine("SUBMISSION READINESS:");
      sb.AppendLine($"  [{(sr.AllControlsCovered ? "PASS" : "FAIL")}] All controls covered");
      sb.AppendLine($"  [{(sr.EvidencePresent ? "PASS" : "FAIL")}] Evidence present");
      sb.AppendLine($"  [{(sr.PoamComplete ? "PASS" : "FAIL")}] POA&M complete");
      sb.AppendLine($"  [{(sr.AttestationsComplete ? "PASS" : "FAIL")}] Attestations complete");
      sb.AppendLine($"  Verdict: {(sr.IsReady ? "READY" : "NOT READY")}");
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

  private static void ValidatePackageHash(string root, List<string> errors, List<string> warnings, ValidationMetrics metrics)
  {
    var manifestPath = Path.Combine(root, "00_Manifest", "manifest.json");
    var hashManifestPath = Path.Combine(root, "00_Manifest", "file_hashes.sha256");

    if (!File.Exists(manifestPath) || !File.Exists(hashManifestPath))
      return;

    try
    {
      using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
      if (!doc.RootElement.TryGetProperty("packageHash", out var packageHashElement) ||
          packageHashElement.ValueKind != System.Text.Json.JsonValueKind.String)
      {
        warnings.Add("No packageHash in manifest - package integrity cannot be verified at package level");
        return;
      }

      var expectedHash = packageHashElement.GetString() ?? string.Empty;
      var actualHash = ComputeSha256(hashManifestPath);

      metrics.PackageHashExpected = expectedHash;
      metrics.PackageHashActual = actualHash;

      if (string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
      {
        metrics.PackageHashValid = true;
      }
      else
      {
        errors.Add("Package hash mismatch: hash manifest has been modified after package creation");
        metrics.PackageHashValid = false;
      }
    }
    catch (Exception ex)
    {
      warnings.Add("Unable to verify package hash: " + ex.Message);
    }
  }

  private static void ValidateSubmissionReadiness(string root, List<string> errors, List<string> warnings, ValidationMetrics metrics)
  {
    var manifestPath = Path.Combine(root, "00_Manifest", "manifest.json");
    if (!File.Exists(manifestPath))
      return;

    try
    {
      using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
      if (!doc.RootElement.TryGetProperty("submissionReadiness", out var readiness))
        return;

      var result = new SubmissionReadinessResult();

      if (readiness.TryGetProperty("allControlsCovered", out var acc))
        result.AllControlsCovered = acc.GetBoolean();
      if (readiness.TryGetProperty("evidencePresent", out var ep))
        result.EvidencePresent = ep.GetBoolean();
      if (readiness.TryGetProperty("poamComplete", out var pc))
        result.PoamComplete = pc.GetBoolean();
      if (readiness.TryGetProperty("attestationsComplete", out var atc))
        result.AttestationsComplete = atc.GetBoolean();

      metrics.SubmissionReadiness = result;

      if (!result.AllControlsCovered)
        warnings.Add("Not all controls have been reviewed (some remain NotReviewed)");
      if (!result.EvidencePresent)
        warnings.Add("No evidence files found for controls");
      if (!result.PoamComplete)
        warnings.Add("POA&M may be incomplete");
      if (!result.AttestationsComplete)
        warnings.Add("Some attestations are still Pending");
    }
    catch (Exception ex)
    {
      warnings.Add("Unable to read submission readiness: " + ex.Message);
    }
  }

  private static string[] ParseCsvLine(string line)
  {
    var list = new List<string>();
    var sb = new StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < line.Length; i++)
    {
      var ch = line[i];
      if (ch == '"')
      {
        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
        {
          sb.Append('"');
          i++;
        }
        else
        {
          inQuotes = !inQuotes;
        }
      }
      else if (ch == ',' && !inQuotes)
      {
        list.Add(sb.ToString());
        sb.Clear();
      }
      else
      {
        sb.Append(ch);
      }
    }

    list.Add(sb.ToString());
    return list.ToArray();
  }

  private static bool IsValidationReportRelativePath(string relativePath)
  {
    var normalized = relativePath.Replace('\\', '/');
    return string.Equals(normalized, "00_Manifest/validation_report.txt", StringComparison.OrdinalIgnoreCase)
      || string.Equals(normalized, "00_Manifest/validation_report.json", StringComparison.OrdinalIgnoreCase);
  }

  private sealed class IndexRow
  {
    public string? VulnId { get; set; }

    public string? RuleId { get; set; }

    public string Status { get; set; } = string.Empty;
  }
}

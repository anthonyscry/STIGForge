using System.Text.Json;
using STIGForge.Export;
using Xunit;

namespace STIGForge.UnitTests.Export;

public class ExportDeterminismTests
{
  [Fact]
  public void SubmissionReadiness_IsReady_WhenAllTrue()
  {
    var readiness = new SubmissionReadiness
    {
      AllControlsCovered = true,
      EvidencePresent = true,
      PoamComplete = true,
      AttestationsComplete = true
    };

    Assert.True(readiness.IsReady);
  }

  [Fact]
  public void SubmissionReadiness_NotReady_WhenAnyFalse()
  {
    var readiness = new SubmissionReadiness
    {
      AllControlsCovered = true,
      EvidencePresent = true,
      PoamComplete = true,
      AttestationsComplete = false
    };

    Assert.False(readiness.IsReady);
  }

  [Fact]
  public void SubmissionReadinessResult_IsReady_WhenAllTrue()
  {
    var result = new SubmissionReadinessResult
    {
      AllControlsCovered = true,
      EvidencePresent = true,
      PoamComplete = true,
      AttestationsComplete = true
    };

    Assert.True(result.IsReady);
  }

  [Fact]
  public void SubmissionReadinessResult_NotReady_WhenControlsNotCovered()
  {
    var result = new SubmissionReadinessResult
    {
      AllControlsCovered = false,
      EvidencePresent = true,
      PoamComplete = true,
      AttestationsComplete = true
    };

    Assert.False(result.IsReady);
  }

  [Fact]
  public void ValidationMetrics_PackageHash_DefaultsFalse()
  {
    var metrics = new ValidationMetrics();
    Assert.False(metrics.PackageHashValid);
    Assert.Null(metrics.PackageHashExpected);
    Assert.Null(metrics.PackageHashActual);
  }

  [Fact]
  public void ValidationMetrics_SubmissionReadiness_DefaultsNull()
  {
    var metrics = new ValidationMetrics();
    Assert.Null(metrics.SubmissionReadiness);
  }

  [Fact]
  public void EmassPackageValidator_MissingPackageHash_WarnsButDoesNotFail()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "export_det_test_" + Guid.NewGuid().ToString("N"));
    try
    {
      // Create minimal valid package structure
      CreateMinimalValidPackage(tempDir, includePackageHash: false);

      var validator = new EmassPackageValidator();
      var result = validator.ValidatePackage(tempDir);

      // Should warn about missing packageHash but not error
      Assert.True(result.Warnings.Any(w => w.Contains("packageHash")));
    }
    finally
    {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void EmassPackageValidator_ValidPackageHash_PassesValidation()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "export_det_test_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateMinimalValidPackage(tempDir, includePackageHash: true);

      var validator = new EmassPackageValidator();
      var result = validator.ValidatePackage(tempDir);

      Assert.True(result.Metrics.PackageHashValid);
    }
    finally
    {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void EmassPackageValidator_TamperedHash_FailsValidation()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "export_det_test_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateMinimalValidPackage(tempDir, includePackageHash: true);

      // Tamper with file_hashes.sha256
      var hashPath = Path.Combine(tempDir, "00_Manifest", "file_hashes.sha256");
      File.AppendAllText(hashPath, "extra_line  extra_file.txt\n");

      var validator = new EmassPackageValidator();
      var result = validator.ValidatePackage(tempDir);

      Assert.False(result.Metrics.PackageHashValid);
      Assert.True(result.Errors.Any(e => e.Contains("Package hash mismatch")));
    }
    finally
    {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void EmassPackageValidator_SubmissionReadiness_IncludedInReport()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "export_det_test_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateMinimalValidPackage(tempDir, includePackageHash: true, includeSubmissionReadiness: true);

      var validator = new EmassPackageValidator();
      var result = validator.ValidatePackage(tempDir);

      Assert.NotNull(result.Metrics.SubmissionReadiness);

      // Write report and check it contains readiness section
      var reportPath = Path.Combine(tempDir, "validation_report.txt");
      validator.WriteValidationReport(result, reportPath);
      var reportText = File.ReadAllText(reportPath);

      Assert.Contains("SUBMISSION READINESS:", reportText);
      Assert.Contains("PACKAGE INTEGRITY:", reportText);
    }
    finally
    {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }

  private static void CreateMinimalValidPackage(string root, bool includePackageHash = false, bool includeSubmissionReadiness = false)
  {
    var dirs = new[] { "00_Manifest", "01_Scans", "02_Checklists", "03_POAM", "04_Evidence", "05_Attestations", "06_Index" };
    foreach (var d in dirs)
      Directory.CreateDirectory(Path.Combine(root, d));

    // Create required files
    var poamPackage = new { items = new[] { new { controlId = "V-1234", vulnId = "V-1234", ruleId = "SV-1234r1_rule", status = "Ongoing" } }, summary = new { totalFindings = 1 } };
    File.WriteAllText(Path.Combine(root, "03_POAM", "poam.json"), JsonSerializer.Serialize(poamPackage));
    File.WriteAllText(Path.Combine(root, "03_POAM", "poam.csv"), "header\nrow1");

    var attestPackage = new { attestations = new[] { new { controlId = "V-1234", complianceStatus = "Compliant" } }, generatedAt = DateTimeOffset.Now, systemName = "Test" };
    File.WriteAllText(Path.Combine(root, "05_Attestations", "attestations.json"), JsonSerializer.Serialize(attestPackage));

    File.WriteAllText(Path.Combine(root, "06_Index", "control_evidence_index.csv"), "VulnId,RuleId,Title,Severity,Status\nV-1234,SV-1234r1_rule,Test,medium,Pass");
    File.WriteAllText(Path.Combine(root, "README_Submission.txt"), "test");

    // Write file hashes
    var hashContent = "abc123  03_POAM/poam.json\ndef456  03_POAM/poam.csv\n";
    File.WriteAllText(Path.Combine(root, "00_Manifest", "file_hashes.sha256"), hashContent);

    // Compute actual hash of hash manifest
    string packageHash;
    using (var sha = System.Security.Cryptography.SHA256.Create())
    {
      var bytes = sha.ComputeHash(File.ReadAllBytes(Path.Combine(root, "00_Manifest", "file_hashes.sha256")));
      packageHash = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    // Create manifest
    var manifest = new Dictionary<string, object>
    {
      ["exportId"] = "test",
      ["createdAt"] = DateTimeOffset.Now.ToString("o"),
      ["totalControls"] = 1,
      ["fileCount"] = 2
    };
    if (includePackageHash)
      manifest["packageHash"] = packageHash;
    if (includeSubmissionReadiness)
    {
      manifest["submissionReadiness"] = new Dictionary<string, object>
      {
        ["allControlsCovered"] = true,
        ["evidencePresent"] = false,
        ["poamComplete"] = true,
        ["attestationsComplete"] = true,
        ["isReady"] = false
      };
    }

    File.WriteAllText(Path.Combine(root, "00_Manifest", "manifest.json"),
      JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(Path.Combine(root, "00_Manifest", "export_log.txt"), "test");

    // Add scan files
    Directory.CreateDirectory(Path.Combine(root, "01_Scans", "raw"));
    File.WriteAllText(Path.Combine(root, "01_Scans", "raw", "scan.json"), "{}");
  }
}

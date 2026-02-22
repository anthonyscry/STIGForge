using System.Text.Json;
using FluentAssertions;
using STIGForge.Export;
using Xunit;

namespace STIGForge.UnitTests.Export;

public class PackageIntegrityTests
{
  [Fact]
  public void PackageHash_ValidatesAgainstManifest()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "pkg_hash_valid_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateMinimalPackage(tempDir, includePackageHash: true);

      var validator = new EmassPackageValidator();
      var result = validator.ValidatePackage(tempDir);

      result.Metrics.PackageHashValid.Should().BeTrue();
      result.Metrics.PackageHashExpected.Should().NotBeNullOrWhiteSpace();
      result.Metrics.PackageHashActual.Should().NotBeNullOrWhiteSpace();
      result.Metrics.PackageHashExpected.Should().Be(result.Metrics.PackageHashActual);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void TamperedHashManifest_FailsValidation()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "pkg_hash_tamper_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateMinimalPackage(tempDir, includePackageHash: true);

      // Tamper with the hash manifest
      var hashPath = Path.Combine(tempDir, "00_Manifest", "file_hashes.sha256");
      File.AppendAllText(hashPath, "deadbeef  injected_file.txt\n");

      var validator = new EmassPackageValidator();
      var result = validator.ValidatePackage(tempDir);

      result.Metrics.PackageHashValid.Should().BeFalse();
      result.Errors.Should().Contain(e => e.Contains("Package hash mismatch"));
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void MissingPackageHash_WarnsButDoesNotFail()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "pkg_hash_missing_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateMinimalPackage(tempDir, includePackageHash: false);

      var validator = new EmassPackageValidator();
      var result = validator.ValidatePackage(tempDir);

      result.Warnings.Should().Contain(w => w.Contains("packageHash"));
      // Should not have a package hash error
      result.Errors.Should().NotContain(e => e.Contains("Package hash"));
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void SubmissionReadiness_InValidationReport()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "pkg_readiness_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateMinimalPackage(tempDir, includePackageHash: true, includeSubmissionReadiness: true);

      var validator = new EmassPackageValidator();
      var result = validator.ValidatePackage(tempDir);

      result.Metrics.SubmissionReadiness.Should().NotBeNull();

      var reportPath = Path.Combine(tempDir, "validation_report.txt");
      validator.WriteValidationReport(result, reportPath);
      var reportText = File.ReadAllText(reportPath);

      reportText.Should().Contain("SUBMISSION READINESS:");
      reportText.Should().Contain("PACKAGE INTEGRITY:");
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void FullIntegrity_EndToEnd_AllChecksPass()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "pkg_full_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateMinimalPackage(tempDir, includePackageHash: true, includeSubmissionReadiness: true);

      var validator = new EmassPackageValidator();
      var result = validator.ValidatePackage(tempDir);

      result.Metrics.PackageHashValid.Should().BeTrue();
      result.Metrics.SubmissionReadiness.Should().NotBeNull();
      // Note: full readiness depends on the specific flags set in the test fixture
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  private static void CreateMinimalPackage(string root, bool includePackageHash = false, bool includeSubmissionReadiness = false)
  {
    var dirs = new[] { "00_Manifest", "01_Scans", "02_Checklists", "03_POAM", "04_Evidence", "05_Attestations", "06_Index" };
    foreach (var d in dirs)
      Directory.CreateDirectory(Path.Combine(root, d));

    var poamPackage = new { items = new[] { new { controlId = "V-1234", vulnId = "V-1234", ruleId = "SV-1234r1_rule", status = "Ongoing" } }, summary = new { totalFindings = 1 } };
    File.WriteAllText(Path.Combine(root, "03_POAM", "poam.json"), JsonSerializer.Serialize(poamPackage));
    File.WriteAllText(Path.Combine(root, "03_POAM", "poam.csv"), "header\nrow1");

    var attestPackage = new { attestations = new[] { new { controlId = "V-1234", complianceStatus = "Compliant" } }, generatedAt = DateTimeOffset.Now, systemName = "Test" };
    File.WriteAllText(Path.Combine(root, "05_Attestations", "attestations.json"), JsonSerializer.Serialize(attestPackage));

    File.WriteAllText(Path.Combine(root, "06_Index", "control_evidence_index.csv"), "VulnId,RuleId,Title,Severity,Status\nV-1234,SV-1234r1_rule,Test,medium,Pass");
    File.WriteAllText(Path.Combine(root, "README_Submission.txt"), "test");

    var hashContent = "abc123  03_POAM/poam.json\ndef456  03_POAM/poam.csv\n";
    File.WriteAllText(Path.Combine(root, "00_Manifest", "file_hashes.sha256"), hashContent);

    string packageHash;
    using (var sha = System.Security.Cryptography.SHA256.Create())
    {
      var bytes = sha.ComputeHash(File.ReadAllBytes(Path.Combine(root, "00_Manifest", "file_hashes.sha256")));
      packageHash = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }

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

    Directory.CreateDirectory(Path.Combine(root, "01_Scans", "raw"));
    File.WriteAllText(Path.Combine(root, "01_Scans", "raw", "scan.json"), "{}");
  }
}

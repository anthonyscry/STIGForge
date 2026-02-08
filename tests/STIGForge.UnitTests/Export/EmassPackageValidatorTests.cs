using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Export;

namespace STIGForge.UnitTests.Export;

public sealed class EmassPackageValidatorTests : IDisposable
{
  private readonly string _tempDir;

  public EmassPackageValidatorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-emass-validator-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public void ValidatePackage_WithValidFixture_ReturnsValidAndMetrics()
  {
    var packageRoot = CreatePackageFixture("valid");
    var validator = new EmassPackageValidator();

    var result = validator.ValidatePackage(packageRoot);

    result.IsValid.Should().BeTrue();
    result.Errors.Should().BeEmpty();
    result.Metrics.RequiredDirectoriesChecked.Should().Be(7);
    result.Metrics.MissingRequiredDirectoryCount.Should().Be(0);
    result.Metrics.RequiredFilesChecked.Should().Be(7);
    result.Metrics.MissingRequiredFileCount.Should().Be(0);
    result.Metrics.IndexedControlCount.Should().Be(2);
    result.Metrics.PoamItemCount.Should().Be(1);
    result.Metrics.AttestationCount.Should().Be(1);
    result.Metrics.CrossArtifactMismatchCount.Should().Be(0);
  }

  [Fact]
  public void ValidatePackage_WhenPoamItemDoesNotMatchIndex_ReturnsError()
  {
    var packageRoot = CreatePackageFixture("poam-mismatch");

    var poamPath = Path.Combine(packageRoot, "03_POAM", "poam.json");
    var package = JsonSerializer.Deserialize<PoamPackage>(File.ReadAllText(poamPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    package.Items = new[]
    {
      new PoamItem
      {
        ControlId = "SV-UNKNOWN",
        RuleId = "SV-UNKNOWN",
        VulnId = "V-UNKNOWN",
        Status = "Ongoing"
      }
    };
    File.WriteAllText(poamPath, JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    RewriteHashManifest(packageRoot);

    var validator = new EmassPackageValidator();
    var result = validator.ValidatePackage(packageRoot);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("POA&M item does not map", StringComparison.Ordinal));
    result.Metrics.CrossArtifactMismatchCount.Should().BeGreaterThan(0);
  }

  [Fact]
  public void ValidatePackage_WhenHashMismatchDetected_ReturnsInvalid()
  {
    var packageRoot = CreatePackageFixture("hash-mismatch");

    File.AppendAllText(Path.Combine(packageRoot, "03_POAM", "poam.csv"), Environment.NewLine + "tampered", Encoding.UTF8);

    var validator = new EmassPackageValidator();
    var result = validator.ValidatePackage(packageRoot);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("Hash mismatch", StringComparison.Ordinal));
    result.Metrics.HashMismatchCount.Should().BeGreaterThan(0);
  }

  [Fact]
  public void ValidatePackage_WhenRequiredFileMissing_ReturnsInvalid()
  {
    var packageRoot = CreatePackageFixture("missing-file");
    File.Delete(Path.Combine(packageRoot, "03_POAM", "poam.csv"));
    RewriteHashManifest(packageRoot);

    var validator = new EmassPackageValidator();
    var result = validator.ValidatePackage(packageRoot);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("Required file missing", StringComparison.Ordinal));
    result.Metrics.MissingRequiredFileCount.Should().BeGreaterThan(0);
  }

  private string CreatePackageFixture(string name)
  {
    var root = Path.Combine(_tempDir, name);
    Directory.CreateDirectory(root);

    var manifestDir = Path.Combine(root, "00_Manifest");
    var scansDir = Path.Combine(root, "01_Scans");
    var checklistsDir = Path.Combine(root, "02_Checklists");
    var poamDir = Path.Combine(root, "03_POAM");
    var evidenceDir = Path.Combine(root, "04_Evidence");
    var attDir = Path.Combine(root, "05_Attestations");
    var indexDir = Path.Combine(root, "06_Index");

    Directory.CreateDirectory(manifestDir);
    Directory.CreateDirectory(scansDir);
    Directory.CreateDirectory(checklistsDir);
    Directory.CreateDirectory(poamDir);
    Directory.CreateDirectory(evidenceDir);
    Directory.CreateDirectory(attDir);
    Directory.CreateDirectory(indexDir);

    File.WriteAllText(Path.Combine(root, "README_Submission.txt"), "submission readme", Encoding.UTF8);
    File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), "{\"bundleId\":\"bundle-1\"}", Encoding.UTF8);
    File.WriteAllText(Path.Combine(scansDir, "scan.txt"), "scan", Encoding.UTF8);
    File.WriteAllText(Path.Combine(checklistsDir, "checklist.ckl"), "<CHECKLIST />", Encoding.UTF8);
    File.WriteAllText(Path.Combine(evidenceDir, "evidence.txt"), "evidence", Encoding.UTF8);

    var poamPackage = new PoamPackage
    {
      Items = new[]
      {
        new PoamItem
        {
          ControlId = "SV-1",
          RuleId = "SV-1",
          VulnId = "V-1",
          Status = "Ongoing"
        }
      }
    };
    File.WriteAllText(Path.Combine(poamDir, "poam.json"), JsonSerializer.Serialize(poamPackage, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }), Encoding.UTF8);
    File.WriteAllText(Path.Combine(poamDir, "poam.csv"), "header,value", Encoding.UTF8);

    var attest = new AttestationPackage
    {
      Attestations = new[]
      {
        new AttestationRecord
        {
          ControlId = "SV-2",
          ComplianceStatus = "Pending"
        }
      }
    };
    File.WriteAllText(Path.Combine(attDir, "attestations.json"), JsonSerializer.Serialize(attest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }), Encoding.UTF8);

    File.WriteAllText(Path.Combine(indexDir, "control_evidence_index.csv"),
      "VulnId,RuleId,Title,Severity,Status,NaReason,NaOrigin,EvidencePaths,ScanSources,LastVerified" + Environment.NewLine +
      "V-1,SV-1,Control One,high,Fail,,,,,2026-02-08T00:00:00Z" + Environment.NewLine +
      "V-2,SV-2,Control Two,medium,Pass,,,,,2026-02-08T00:00:00Z",
      Encoding.UTF8);

    RewriteHashManifest(root);
    return root;
  }

  private static void RewriteHashManifest(string root)
  {
    var hashManifestPath = Path.Combine(root, "00_Manifest", "file_hashes.sha256");
    var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
      .Where(f => !string.Equals(f, hashManifestPath, StringComparison.OrdinalIgnoreCase))
      .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var sb = new StringBuilder();
    foreach (var file in files)
    {
      sb.AppendLine(ComputeSha256(file) + "  " + Path.GetRelativePath(root, file));
    }

    File.WriteAllText(hashManifestPath, sb.ToString(), Encoding.UTF8);
  }

  private static string ComputeSha256(string path)
  {
    using var stream = File.OpenRead(path);
    using var sha = SHA256.Create();
    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
  }
}

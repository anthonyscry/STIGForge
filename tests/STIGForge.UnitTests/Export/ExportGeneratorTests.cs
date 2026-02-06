using FluentAssertions;
using STIGForge.Export;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Export;

public class ExportGeneratorTests : IDisposable
{
  private readonly string _tempDir;

  public ExportGeneratorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-export-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  // ── PoamGenerator ────────────────────────────────────────────────────

  [Fact]
  public void GeneratePoam_NoFailures_ReturnsEmptyPackage()
  {
    var results = new List<NormalizedVerifyResult>
    {
      new() { ControlId = "V-1", Status = VerifyStatus.Pass, Tool = "SCAP" }
    };

    var package = PoamGenerator.GeneratePoam(results, "TestSystem", "bundle-1");

    package.Items.Should().BeEmpty();
    package.Summary.TotalFindings.Should().Be(0);
  }

  [Fact]
  public void GeneratePoam_WithFailures_CreatesPoamItems()
  {
    var results = new List<NormalizedVerifyResult>
    {
      new() { ControlId = "V-1", VulnId = "V-1", Severity = "high", Status = VerifyStatus.Fail, Title = "Failed Check", Tool = "SCAP" },
      new() { ControlId = "V-2", VulnId = "V-2", Severity = "medium", Status = VerifyStatus.Error, Title = "Error Check", Tool = "SCAP" },
      new() { ControlId = "V-3", VulnId = "V-3", Severity = "low", Status = VerifyStatus.Pass, Title = "Pass Check", Tool = "SCAP" }
    };

    var package = PoamGenerator.GeneratePoam(results, "TestSystem", "bundle-1");

    package.Items.Should().HaveCount(2); // Only Fail + Error
    package.Summary.TotalFindings.Should().Be(2);
    package.Items.Should().Contain(i => i.VulnId == "V-1");
    package.Items.Should().Contain(i => i.VulnId == "V-2");
    package.Items.Should().NotContain(i => i.VulnId == "V-3");
  }

  [Fact]
  public void WritePoamFiles_CreatesOutputFiles()
  {
    var results = new List<NormalizedVerifyResult>
    {
      new() { ControlId = "V-1", VulnId = "V-1", Severity = "high", Status = VerifyStatus.Fail, Title = "Test", Tool = "SCAP" }
    };
    var package = PoamGenerator.GeneratePoam(results, "Test", "b1");

    PoamGenerator.WritePoamFiles(package, _tempDir);

    Directory.GetFiles(_tempDir).Length.Should().BeGreaterThan(0);
  }

  // ── AttestationGenerator ─────────────────────────────────────────────

  [Fact]
  public void GenerateAttestations_CreatesRecordsForAllControls()
  {
    var controlIds = new List<string> { "V-1", "V-2", "V-3" };

    var package = AttestationGenerator.GenerateAttestations(controlIds, "TestSystem", "bundle-1");

    package.Attestations.Should().HaveCount(3);
    package.SystemName.Should().Be("TestSystem");
    package.Attestations.Should().OnlyContain(a => a.ComplianceStatus == "Pending");
  }

  [Fact]
  public void GenerateAttestations_EmptyList_ReturnsEmptyPackage()
  {
    var package = AttestationGenerator.GenerateAttestations(Array.Empty<string>(), "Test", "b1");

    package.Attestations.Should().BeEmpty();
  }

  [Fact]
  public void WriteAttestationFiles_CreatesOutputFiles()
  {
    var package = AttestationGenerator.GenerateAttestations(new[] { "V-1" }, "Test", "b1");

    AttestationGenerator.WriteAttestationFiles(package, _tempDir);

    Directory.GetFiles(_tempDir).Length.Should().BeGreaterThan(0);
  }

  // ── EmassPackageValidator ────────────────────────────────────────────

  [Fact]
  public void ValidatePackage_NonExistentDir_ReturnsInvalid()
  {
    var validator = new EmassPackageValidator();
    var result = validator.ValidatePackage(Path.Combine(_tempDir, "nonexistent"));

    result.IsValid.Should().BeFalse();
    result.Errors.Should().NotBeEmpty();
  }

  [Fact]
  public void ValidatePackage_EmptyDir_ReturnsInvalid()
  {
    var emptyDir = Path.Combine(_tempDir, "empty-package");
    Directory.CreateDirectory(emptyDir);

    var validator = new EmassPackageValidator();
    var result = validator.ValidatePackage(emptyDir);

    result.IsValid.Should().BeFalse();
  }
}

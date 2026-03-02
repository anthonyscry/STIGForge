using System.Text.Json;
using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public sealed class EmassPackageGeneratorTests : IDisposable
{
  private readonly string _root;

  public EmassPackageGeneratorTests()
  {
    _root = Path.Combine(Path.GetTempPath(), "stigforge-emass-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose()
  {
    try { Directory.Delete(_root, true); } catch { }
  }

  [Fact]
  public async Task SavePackageAsync_WritesChecksumManifest()
  {
    var generator = new EmassPackageGenerator();
    var package = new EmassPackage
    {
      PackageId = "PKG001",
      GeneratedAt = DateTimeOffset.UtcNow,
      SystemName = "Test System",
      SystemAcronym = "TS",
      BundleRoot = _root,
      ControlCorrelationMatrix = new ControlCorrelationMatrix
      {
        Controls =
        {
          new CcmControlEntry { ControlId = "AC-1", ControlName = "Access Control" }
        }
      },
      Poam = new PlanOfAction
      {
        Entries =
        {
          new PoamEntry { PoamId = "POAM-1", ControlId = "AC-1", VulnerabilityDescription = "desc" }
        }
      }
    };

    var output = Path.Combine(_root, "out");
    await generator.SavePackageAsync(package, output, CancellationToken.None);

    var packageDir = Directory.EnumerateDirectories(output).Single();
    var checksumsPath = Path.Combine(packageDir, "sha256-checksums.txt");
    File.Exists(checksumsPath).Should().BeTrue();
    var text = await File.ReadAllTextAsync(checksumsPath);
    text.Should().Contain("package-manifest.json");
    text.Should().Contain("poam.json");
  }

  [Fact]
  public async Task GeneratePackageAsync_WithPreviousPackage_PopulatesChangeLogCounts()
  {
    var bundle = Path.Combine(_root, "bundle");
    Directory.CreateDirectory(Path.Combine(bundle, "Manifest"));
    await File.WriteAllTextAsync(Path.Combine(bundle, "Manifest", "manifest.json"), "{}");

    var controls = new List<ControlRecord>
    {
      MakeControl("AC-1", "Control One"),
      MakeControl("AC-2", "Control Two")
    };
    await File.WriteAllTextAsync(Path.Combine(bundle, "Manifest", "pack_controls.json"), JsonSerializer.Serialize(controls));

    var previousDir = Path.Combine(_root, "previous");
    Directory.CreateDirectory(previousDir);
    var previousPackage = new EmassPackage
    {
      PackageId = "OLD",
      SystemName = "Test System",
      SystemAcronym = "TS",
      ControlCorrelationMatrix = new ControlCorrelationMatrix
      {
        Controls =
        {
          new CcmControlEntry
          {
            ControlId = "AC-1",
            ControlName = "Control One",
            ImplementationStatus = "Planned",
            Applicability = "Applicable"
          }
        }
      },
      Poam = new PlanOfAction { Entries = new List<PoamEntry>() }
    };
    await File.WriteAllTextAsync(Path.Combine(previousDir, "package-manifest.json"), JsonSerializer.Serialize(previousPackage));

    var generator = new EmassPackageGenerator();
    var generated = await generator.GeneratePackageAsync(bundle, "Test System", "TS", previousDir, CancellationToken.None);

    generated.ChangeLog.Should().NotBeNullOrWhiteSpace();
    generated.ChangeLog.Should().Contain("Added controls: 1");
  }

  private static ControlRecord MakeControl(string id, string title)
  {
    return new ControlRecord
    {
      ControlId = id,
      Title = title,
      Severity = "medium",
      IsManual = false,
      ExternalIds = new ExternalIds { RuleId = $"SV-{id}", VulnId = $"V-{id}" },
      Applicability = new Applicability { ClassificationScope = ScopeTag.Both, Confidence = Confidence.High },
      Revision = new RevisionInfo { PackName = "Pack" }
    };
  }
}

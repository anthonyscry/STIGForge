using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.Paths;
using static STIGForge.UnitTests.TestCategories;

namespace STIGForge.UnitTests;

public class SmokeTests
{
  [Fact]
  [Trait("Category", Unit)]
  public void PathBuilder_Should_Create_Deterministic_ExportRoot()
  {
    var pb = new PathBuilder();
    var ts = new DateTimeOffset(2026, 2, 2, 21, 30, 0, TimeSpan.Zero);

    var a = pb.GetEmassExportRoot("SYS1", "Win11", "Workstation", "Prof A", "Q1_2026", ts);
    var b = pb.GetEmassExportRoot("SYS1", "Win11", "Workstation", "Prof A", "Q1_2026", ts);

    a.Should().Be(b);
    Path.GetFileName(a).Should().Be("EMASS_SYS1Win11WorkstationProfAQ1_2026_20260202-2130");
  }

  [Fact]
  [Trait("Category", Unit)]
  public void PathBuilder_Should_Resolve_Project_Import_Root()
  {
    var pb = new PathBuilder();
    var projectRoot = Directory.GetParent(pb.GetAppDataRoot())?.FullName;

    projectRoot.Should().NotBeNullOrWhiteSpace();
    pb.GetImportRoot().Should().Be(Path.Combine(projectRoot!, "import"));
    pb.GetImportInboxRoot().Should().Be(Path.Combine(projectRoot!, "import", "inbox"));
  }

  [Fact]
  [Trait("Category", Unit)]
  public void PathBuilder_Should_Prefer_AppBase_Over_CurrentDirectory()
  {
    var expectedRoot = Path.Combine(Path.GetTempPath(), "stigforge-pathbuilder-expected-" + Guid.NewGuid().ToString("n"));
    var fallbackRoot = Path.Combine(Path.GetTempPath(), "stigforge-pathbuilder-fallback-" + Guid.NewGuid().ToString("n"));
    var appBase = Path.Combine(expectedRoot, "src", "STIGForge.App", "bin", "Debug", "net8.0-windows");
    var currentDirectory = Path.Combine(fallbackRoot, "legacy");

    Directory.CreateDirectory(Path.Combine(expectedRoot, ".git"));
    Directory.CreateDirectory(Path.Combine(fallbackRoot, ".git"));
    Directory.CreateDirectory(appBase);
    Directory.CreateDirectory(currentDirectory);

    try
    {
      var pb = new PathBuilder(appBase, currentDirectory);

      pb.GetImportRoot().Should().Be(Path.Combine(expectedRoot, "import"));
      pb.GetImportInboxRoot().Should().Be(Path.Combine(expectedRoot, "import", "inbox"));
    }
    finally
    {
      if (Directory.Exists(expectedRoot))
        Directory.Delete(expectedRoot, true);
      if (Directory.Exists(fallbackRoot))
        Directory.Delete(fallbackRoot, true);
    }
  }

  [Fact]
  [Trait("Category", Unit)]
  public void ClassifiedProfile_Should_AutoNa_UnclassifiedOnly_When_Confident()
  {
    var svc = new ClassificationScopeService();

    var profile = new Profile
    {
      ProfileId = "p1",
      Name = "Classified WS",
      OsTarget = OsTarget.Win11,
      RoleTemplate = RoleTemplate.Workstation,
      HardeningMode = HardeningMode.Safe,
      ClassificationMode = ClassificationMode.Classified,
      NaPolicy = new NaPolicy
      {
        AutoNaOutOfScope = true,
        ConfidenceThreshold = Confidence.High,
        DefaultNaCommentTemplate = "Not applicable: unclassified-only control; system is classified."
      },
      OverlayIds = Array.Empty<string>()
    };

    var c = new ControlRecord
    {
      ControlId = "c1",
      ExternalIds = new ExternalIds { VulnId = "V-12345", RuleId = "SV-1", SrgId = null, BenchmarkId = "WIN11" },
      Title = "Example",
      Severity = "medium",
      Discussion = null,
      CheckText = null,
      FixText = null,
      IsManual = false,
      WizardPrompt = null,
      Applicability = new Applicability
      {
        OsTarget = OsTarget.Win11,
        RoleTags = Array.Empty<RoleTemplate>(),
        ClassificationScope = ScopeTag.UnclassifiedOnly,
        Confidence = Confidence.High
      },
      Revision = new RevisionInfo { PackName = "Q1_2026" }
    };

    var compiled = svc.Compile(profile, new[] { c });
    compiled.Controls.Single().Status.Should().Be(ControlStatus.NotApplicable);
  }
}

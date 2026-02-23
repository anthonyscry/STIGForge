using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Workflow;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class LocalSetupValidatorTests : IDisposable
{
  private readonly string _tempRoot;

  public LocalSetupValidatorTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-local-setup-validator-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempRoot, true); } catch { }
  }

  [Fact]
  public void ValidateRequiredTools_Throws_WhenEvaluateStigScriptIsMissing()
  {
    var paths = new TestPathBuilder(_tempRoot);
    Directory.CreateDirectory(Path.Combine(paths.GetToolsRoot(), "Evaluate-STIG"));
    var validator = new LocalSetupValidator(paths);

    var act = () => validator.ValidateRequiredTools();

    act.Should().Throw<InvalidOperationException>()
      .WithMessage("*Evaluate-STIG*")
      .WithMessage("*Evaluate-STIG.ps1*");
  }

  [Fact]
  public void ValidateRequiredTools_Throws_WhenEvaluateStigPathIsAFile()
  {
    var paths = new TestPathBuilder(_tempRoot);
    var invalidPath = Path.Combine(paths.GetToolsRoot(), "Evaluate-STIG", "Evaluate-STIG");
    Directory.CreateDirectory(Path.GetDirectoryName(invalidPath)!);
    File.WriteAllText(invalidPath, "not-a-directory");

    var validator = new LocalSetupValidator(paths);

    var act = () => validator.ValidateRequiredTools();

    act.Should().Throw<InvalidOperationException>()
      .WithMessage("*Evaluate-STIG*");
  }

  [Fact]
  public void ValidateRequiredTools_Succeeds_WhenEvaluateStigScriptExistsInDefaultLocation()
  {
    var paths = new TestPathBuilder(_tempRoot);
    var toolRoot = Path.Combine(paths.GetToolsRoot(), "Evaluate-STIG", "Evaluate-STIG");
    Directory.CreateDirectory(toolRoot);
    File.WriteAllText(Path.Combine(toolRoot, "Evaluate-STIG.ps1"), "# script");

    var validator = new LocalSetupValidator(paths);

    var result = validator.ValidateRequiredTools();

    result.Should().Be(toolRoot);
  }

  private sealed class TestPathBuilder : IPathBuilder
  {
    private readonly string _root;

    public TestPathBuilder(string root)
    {
      _root = root;
    }

    public string GetAppDataRoot() => _root;
    public string GetContentPacksRoot() => Path.Combine(_root, "contentpacks");
    public string GetPackRoot(string packId) => Path.Combine(GetContentPacksRoot(), packId);
    public string GetBundleRoot(string bundleId) => Path.Combine(_root, "bundles", bundleId);
    public string GetLogsRoot() => Path.Combine(_root, "logs");
    public string GetImportRoot() => Path.Combine(_root, "import");
    public string GetImportInboxRoot() => Path.Combine(GetImportRoot(), "inbox");
    public string GetImportIndexPath() => Path.Combine(GetImportRoot(), "inbox_index.json");
    public string GetToolsRoot() => Path.Combine(_root, "tools");
    public string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts)
      => Path.Combine(_root, "exports", "EMASS_TEST_" + ts.ToString("yyyyMMdd-HHmm"));
  }
}

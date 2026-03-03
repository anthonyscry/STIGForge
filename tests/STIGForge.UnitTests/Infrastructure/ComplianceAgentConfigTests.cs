using FluentAssertions;
using STIGForge.Infrastructure.System;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class ComplianceAgentConfigTests : IDisposable
{
  private readonly string _tempDir;

  public ComplianceAgentConfigTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-agent-config-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_tempDir))
        Directory.Delete(_tempDir, true);
    }
    catch
    {
    }
  }

  [Fact]
  public async Task SaveAndLoad_RoundTripsConfiguration()
  {
    var path = Path.Combine(_tempDir, "agent-config.json");
    var config = new ComplianceAgentConfig
    {
      BundleRoot = "C:/Bundles/Primary",
      CheckIntervalMinutes = 60,
      AutoRemediate = true,
      EnableAuditForwarding = false,
      MaxDriftEventsToForward = 5
    };

    await ComplianceAgentConfig.SaveToFileAsync(config, path);
    var loaded = await ComplianceAgentConfig.LoadFromFileAsync(path);

    loaded.BundleRoot.Should().Be(config.BundleRoot);
    loaded.CheckIntervalMinutes.Should().Be(60);
    loaded.AutoRemediate.Should().BeTrue();
    loaded.EnableAuditForwarding.Should().BeFalse();
    loaded.MaxDriftEventsToForward.Should().Be(5);
  }

  [Fact]
  public async Task LoadFromFileAsync_MissingFile_Throws()
  {
    var path = Path.Combine(_tempDir, "missing.json");

    var act = async () => await ComplianceAgentConfig.LoadFromFileAsync(path);

    await act.Should().ThrowAsync<FileNotFoundException>();
  }

  [Fact]
  public async Task LoadFromFileAsync_MissingBundleRoot_Throws()
  {
    var path = Path.Combine(_tempDir, "invalid.json");
    await File.WriteAllTextAsync(path, "{\"checkIntervalMinutes\":60}");

    var act = async () => await ComplianceAgentConfig.LoadFromFileAsync(path);

    await act.Should().ThrowAsync<InvalidOperationException>();
  }

  [Fact]
  public async Task SaveToFileAsync_InvalidInterval_Throws()
  {
    var path = Path.Combine(_tempDir, "invalid-save.json");
    var config = new ComplianceAgentConfig
    {
      BundleRoot = "bundle",
      CheckIntervalMinutes = 0
    };

    var act = async () => await ComplianceAgentConfig.SaveToFileAsync(config, path);

    await act.Should().ThrowAsync<InvalidOperationException>();
  }

  [Fact]
  public async Task SaveToFileAsync_CreatesParentDirectory()
  {
    var path = Path.Combine(_tempDir, "nested", "agent-config.json");
    var config = new ComplianceAgentConfig
    {
      BundleRoot = "bundle"
    };

    await ComplianceAgentConfig.SaveToFileAsync(config, path);

    File.Exists(path).Should().BeTrue();
  }
}

using Microsoft.Extensions.DependencyInjection;
using STIGForge.Cli;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.Cli;

public sealed class CliHostFactoryTests : IDisposable
{
  private readonly string _tempRoot;

  public CliHostFactoryTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-cli-host-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
      Directory.Delete(_tempRoot, true);
  }

  [Fact]
  public async Task ConfigureServices_ResolvesConnectionStringAndImporterWithoutCircularDependency()
  {
    var services = new ServiceCollection();
    services.AddLogging();
    CliHostFactory.ConfigureServices(services, () => new TestPathBuilder(_tempRoot));

    await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
    {
      ValidateScopes = true,
      ValidateOnBuild = true
    });

    var resolveTask = Task.Run(() =>
    {
      var connectionString = provider.GetRequiredService<string>();
      var importer = provider.GetRequiredService<ContentPackImporter>();
      return (connectionString, importer);
    });

    var completed = await Task.WhenAny(resolveTask, Task.Delay(TimeSpan.FromSeconds(10)));
    Assert.Same(resolveTask, completed);

    var resolved = await resolveTask;
    Assert.NotNull(resolved.importer);
    Assert.Contains(Path.Combine(_tempRoot, "data", "stigforge.db"), resolved.connectionString, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory()
  {
    using var host = CliHostFactory.BuildHost(() => new TestPathBuilder(_tempRoot));

    await host.StartAsync();
    await host.StopAsync();

    var logsRoot = Path.Combine(_tempRoot, "logs");
    Assert.True(Directory.Exists(logsRoot));
    Assert.NotEmpty(Directory.GetFiles(logsRoot, "stigforge-cli*.log", SearchOption.TopDirectoryOnly));
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

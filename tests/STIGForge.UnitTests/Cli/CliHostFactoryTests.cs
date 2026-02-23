using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using STIGForge.Cli;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using Xunit;

namespace STIGForge.UnitTests.Cli;

public sealed class CliHostFactoryTests : IAsyncLifetime
{
  private readonly string _tempRoot;
  private IHost? _host;

  public CliHostFactoryTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-cli-host-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    if (_host is not null)
    {
      await _host.StopAsync();
      _host.Dispose();
      // Allow file handles to release before deleting directory
      await Task.Delay(50);
    }

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
    _host = CliHostFactory.BuildHost(() => new TestPathBuilder(_tempRoot));

    await _host.StartAsync();
    await _host.StopAsync();

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

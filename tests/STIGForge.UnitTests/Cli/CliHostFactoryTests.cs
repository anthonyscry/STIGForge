using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Cli;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using System.Diagnostics;
using Xunit;
using static STIGForge.UnitTests.TestCategories;

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
      _host = null;
    }

    await DeleteDirectoryWithRetriesAsync(_tempRoot);
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

  [Fact]
  [Trait("Category", Integration)]
  public async Task BuildHost_LogContainsCorrelationId()
  {
    const string testMessage = "Test log message with correlation";
    _host = CliHostFactory.BuildHost(() => new TestPathBuilder(_tempRoot));

    await _host.StartAsync();

    // Create a scope that logs something
    var logger = _host.Services.GetRequiredService<ILogger<CliHostFactoryTests>>();
    using (var activity = new Activity("cli-host-test-correlation"))
    {
      activity.Start();
      logger.LogInformation(testMessage);
    }

    await _host.StopAsync();
    _host.Dispose();
    _host = null;

    var logsRoot = Path.Combine(_tempRoot, "logs");
    var logContent = await WaitForLogContentAsync(logsRoot, "stigforge-cli*.log", testMessage, TimeSpan.FromSeconds(3));
    logContent.Should().NotBeNullOrWhiteSpace();
    // Log should contain either TraceId (if Activity started) or CorrelationId
    // Both are 32-char hex strings (GUID without dashes)
    logContent.Should().MatchRegex(@"\[[a-f0-9]{32}\]", "log should contain correlation ID");
  }

  private static async Task<string> ReadAllTextSharedAsync(string path)
  {
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(stream);
    return await reader.ReadToEndAsync();
  }

  private static async Task<string?> WaitForLogContentAsync(string root, string pattern, string marker, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      if (Directory.Exists(root))
      {
        var files = Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
          var content = await ReadAllTextSharedAsync(file);
          if (content.Contains(marker, StringComparison.Ordinal))
            return content;
        }
      }

      await Task.Delay(50);
    }

    return null;
  }

  private static async Task DeleteDirectoryWithRetriesAsync(string path)
  {
    if (!Directory.Exists(path))
      return;

    for (var attempt = 0; attempt < 10; attempt++)
    {
      try
      {
        Directory.Delete(path, true);
        return;
      }
      catch (IOException) when (attempt < 9)
      {
        await Task.Delay(100);
      }
      catch (UnauthorizedAccessException) when (attempt < 9)
      {
        await Task.Delay(100);
      }
      catch (IOException)
      {
        return;
      }
      catch (UnauthorizedAccessException)
      {
        return;
      }
    }
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

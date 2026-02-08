using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Cli.Commands;

namespace STIGForge.UnitTests.Cli;

public sealed class SupportBundleBuilderTests : IDisposable
{
  private readonly string _root;

  public SupportBundleBuilderTests()
  {
    _root = Path.Combine(Path.GetTempPath(), "stigforge-support-bundle-tests-" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose()
  {
    if (!Directory.Exists(_root)) return;
    try { Directory.Delete(_root, true); }
    catch { }
  }

  [Fact]
  public void Create_IncludesLogsMetadataAndBundleDiagnostics()
  {
    var appDataRoot = Path.Combine(_root, ".stigforge");
    var logsRoot = Path.Combine(appDataRoot, "logs");
    var bundleRoot = Path.Combine(_root, "bundle-a");
    var outputRoot = Path.Combine(_root, "out");

    Directory.CreateDirectory(logsRoot);
    Directory.CreateDirectory(Path.Combine(bundleRoot, "Manifest"));
    Directory.CreateDirectory(Path.Combine(bundleRoot, "Verify", "run-1"));
    File.WriteAllText(Path.Combine(logsRoot, "stigforge-cli.log"), "cli log");
    File.WriteAllText(Path.Combine(logsRoot, "stigforge-app.log"), "app log");
    File.WriteAllText(Path.Combine(bundleRoot, "Manifest", "manifest.json"), "{}");
    File.WriteAllText(Path.Combine(bundleRoot, "Verify", "run-1", "consolidated-results.json"), "{\"results\":[]}");

    var builder = new SupportBundleBuilder();
    var result = builder.Create(new SupportBundleRequest
    {
      OutputDirectory = outputRoot,
      AppDataRoot = appDataRoot,
      BundleRoot = bundleRoot,
      MaxLogFiles = 5,
      IncludeDatabase = false
    });

    File.Exists(result.BundleZipPath).Should().BeTrue();
    File.Exists(result.ManifestPath).Should().BeTrue();
    result.FileCount.Should().BeGreaterThan(0);

    var extractRoot = Path.Combine(_root, "extract-one");
    ZipFile.ExtractToDirectory(result.BundleZipPath, extractRoot);

    File.Exists(Path.Combine(extractRoot, "metadata", "system-info.json")).Should().BeTrue();
    File.Exists(Path.Combine(extractRoot, "logs", "stigforge-cli.log")).Should().BeTrue();
    File.Exists(Path.Combine(extractRoot, "bundle", "Manifest", "manifest.json")).Should().BeTrue();
    File.Exists(Path.Combine(extractRoot, "bundle", "Verify", "run-1", "consolidated-results.json")).Should().BeTrue();
  }

  [Fact]
  public void Create_RespectsIncludeDatabaseFlag()
  {
    var appDataRoot = Path.Combine(_root, ".stigforge-db");
    var outputRoot = Path.Combine(_root, "out-db");
    Directory.CreateDirectory(Path.Combine(appDataRoot, "logs"));
    Directory.CreateDirectory(Path.Combine(appDataRoot, "data"));
    File.WriteAllText(Path.Combine(appDataRoot, "logs", "stigforge.log"), "log");
    File.WriteAllText(Path.Combine(appDataRoot, "data", "stigforge.db"), "not-real-db");

    var builder = new SupportBundleBuilder();
    var withoutDb = builder.Create(new SupportBundleRequest
    {
      OutputDirectory = outputRoot,
      AppDataRoot = appDataRoot,
      IncludeDatabase = false
    });

    var extractWithoutDb = Path.Combine(_root, "extract-without-db");
    ZipFile.ExtractToDirectory(withoutDb.BundleZipPath, extractWithoutDb);
    File.Exists(Path.Combine(extractWithoutDb, "data", "stigforge.db")).Should().BeFalse();

    var withDb = builder.Create(new SupportBundleRequest
    {
      OutputDirectory = outputRoot,
      AppDataRoot = appDataRoot,
      IncludeDatabase = true,
      IncludeSensitive = true,
      SensitiveReason = "Incident triage requires DB diagnostics"
    });

    var extractWithDb = Path.Combine(_root, "extract-with-db");
    ZipFile.ExtractToDirectory(withDb.BundleZipPath, extractWithDb);
    File.Exists(Path.Combine(extractWithDb, "data", "stigforge.db")).Should().BeTrue();
  }

  [Fact]
  public void Create_DefaultModeRedactsSensitiveMetadataAndExcludesSecretNamedFiles()
  {
    var appDataRoot = Path.Combine(_root, ".stigforge-sensitive-default");
    var bundleRoot = Path.Combine(_root, "bundle-sensitive");
    var outputRoot = Path.Combine(_root, "out-sensitive-default");
    Directory.CreateDirectory(Path.Combine(appDataRoot, "logs"));
    Directory.CreateDirectory(Path.Combine(bundleRoot, "Manifest"));
    File.WriteAllText(Path.Combine(appDataRoot, "logs", "session.log"), "log");
    File.WriteAllText(Path.Combine(bundleRoot, "Manifest", "credentials.json"), "{\"secret\":\"value\"}");
    File.WriteAllText(Path.Combine(bundleRoot, "Manifest", "manifest.json"), "{}");

    var builder = new SupportBundleBuilder();
    var result = builder.Create(new SupportBundleRequest
    {
      OutputDirectory = outputRoot,
      AppDataRoot = appDataRoot,
      BundleRoot = bundleRoot,
      IncludeSensitive = false
    });

    var extractRoot = Path.Combine(_root, "extract-sensitive-default");
    ZipFile.ExtractToDirectory(result.BundleZipPath, extractRoot);

    File.Exists(Path.Combine(extractRoot, "bundle", "Manifest", "credentials.json")).Should().BeFalse();
    var metadataPath = Path.Combine(extractRoot, "metadata", "system-info.json");
    var metadata = File.ReadAllText(metadataPath);
    metadata.Should().Contain("[redacted]");
  }

  [Fact]
  public void Create_WhenIncludeSensitiveEnabled_IncludesSensitiveFilesAndSourcePaths()
  {
    var appDataRoot = Path.Combine(_root, ".stigforge-sensitive-optin");
    var bundleRoot = Path.Combine(_root, "bundle-sensitive-optin");
    var outputRoot = Path.Combine(_root, "out-sensitive-optin");
    Directory.CreateDirectory(Path.Combine(appDataRoot, "logs"));
    Directory.CreateDirectory(Path.Combine(bundleRoot, "Manifest"));
    File.WriteAllText(Path.Combine(appDataRoot, "logs", "session.log"), "log");
    File.WriteAllText(Path.Combine(bundleRoot, "Manifest", "credentials.json"), "{\"secret\":\"value\"}");
    File.WriteAllText(Path.Combine(bundleRoot, "Manifest", "manifest.json"), "{}");

    var builder = new SupportBundleBuilder();
    var result = builder.Create(new SupportBundleRequest
    {
      OutputDirectory = outputRoot,
      AppDataRoot = appDataRoot,
      BundleRoot = bundleRoot,
      IncludeSensitive = true,
      SensitiveReason = "Incident response requires full diagnostics"
    });

    var extractRoot = Path.Combine(_root, "extract-sensitive-optin");
    ZipFile.ExtractToDirectory(result.BundleZipPath, extractRoot);

    File.Exists(Path.Combine(extractRoot, "bundle", "Manifest", "credentials.json")).Should().BeTrue();

    var metadataPath = Path.Combine(extractRoot, "metadata", "system-info.json");
    var metadataJson = File.ReadAllText(metadataPath);
    metadataJson.Should().NotContain("[redacted]");

    var manifestPath = Path.Combine(extractRoot, "metadata", "support-bundle-manifest.json");
    using var manifestDoc = JsonDocument.Parse(File.ReadAllText(manifestPath));
    manifestDoc.RootElement.GetProperty("files")
      .EnumerateArray()
      .Select(file => file.GetProperty("source").GetString())
      .Should().OnlyContain(source => !string.Equals(source, "[redacted]", StringComparison.Ordinal));
  }

  [Fact]
  public void Create_MissingBundlePathAddsWarningButStillBuildsArchive()
  {
    var appDataRoot = Path.Combine(_root, ".stigforge-missing-bundle");
    var outputRoot = Path.Combine(_root, "out-missing-bundle");
    Directory.CreateDirectory(Path.Combine(appDataRoot, "logs"));
    File.WriteAllText(Path.Combine(appDataRoot, "logs", "stigforge.log"), "log");

    var builder = new SupportBundleBuilder();
    var result = builder.Create(new SupportBundleRequest
    {
      OutputDirectory = outputRoot,
      AppDataRoot = appDataRoot,
      BundleRoot = Path.Combine(_root, "does-not-exist")
    });

    File.Exists(result.BundleZipPath).Should().BeTrue();
    result.Warnings.Should().Contain(w => w.Contains("Bundle directory not found", StringComparison.OrdinalIgnoreCase));
  }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace STIGForge.IntegrationTests.Release;

public sealed class CoverageReportScriptTests
{
  [Fact]
  public async Task InvokeCoverageReport_GeneratesSummaryJsonAndMarkdownArtifacts()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageReport.ps1");
    var coveragePath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "coverage-mixed.cobertura.xml");
    await using var outputDirectory = new TemporaryDirectory();

    var result = await ExecuteCoverageReportAsync(scriptPath, coveragePath, outputDirectory.Path);

    result.ExitCode.Should().Be(0);

    var summaryJsonPath = Path.Combine(outputDirectory.Path, "coverage-summary.json");
    var reportMarkdownPath = Path.Combine(outputDirectory.Path, "coverage-report.md");

    File.Exists(summaryJsonPath).Should().BeTrue();
    File.Exists(reportMarkdownPath).Should().BeTrue();
  }

  [Fact]
  public async Task InvokeCoverageReport_IncludesLineAndBranchMetricsInArtifacts()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageReport.ps1");
    var coveragePath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "coverage-mixed.cobertura.xml");
    await using var outputDirectory = new TemporaryDirectory();

    var result = await ExecuteCoverageReportAsync(scriptPath, coveragePath, outputDirectory.Path);

    result.ExitCode.Should().Be(0);

    var summaryJsonPath = Path.Combine(outputDirectory.Path, "coverage-summary.json");
    var summaryJson = await File.ReadAllTextAsync(summaryJsonPath);
    using var document = JsonDocument.Parse(summaryJson);

    var totals = document.RootElement.GetProperty("totals");
    totals.GetProperty("lineCoveragePercent").GetDouble().Should().Be(75.0);
    totals.GetProperty("branchCoveragePercent").GetDouble().Should().Be(60.0);
    totals.GetProperty("linesCovered").GetInt32().Should().Be(75);
    totals.GetProperty("linesValid").GetInt32().Should().Be(100);
    totals.GetProperty("branchesCovered").GetInt32().Should().Be(30);
    totals.GetProperty("branchesValid").GetInt32().Should().Be(50);

    var packages = document.RootElement.GetProperty("packages");
    packages.GetArrayLength().Should().Be(2);

    var orderedPackages = packages
      .EnumerateArray()
      .OrderBy(p => p.GetProperty("name").GetString(), StringComparer.OrdinalIgnoreCase)
      .ToArray();

    orderedPackages[0].GetProperty("name").GetString().Should().Be("Critical.Assembly");
    orderedPackages[0].GetProperty("linesCovered").GetInt32().Should().Be(40);
    orderedPackages[0].GetProperty("linesValid").GetInt32().Should().Be(50);
    orderedPackages[0].GetProperty("lineCoveragePercent").GetDouble().Should().Be(80.0);
    orderedPackages[0].GetProperty("branchesCovered").GetInt32().Should().Be(20);
    orderedPackages[0].GetProperty("branchesValid").GetInt32().Should().Be(30);
    orderedPackages[0].GetProperty("branchCoveragePercent").GetDouble().Should().Be(66.67);

    orderedPackages[1].GetProperty("name").GetString().Should().Be("NonCritical.Assembly");
    orderedPackages[1].GetProperty("linesCovered").GetInt32().Should().Be(35);
    orderedPackages[1].GetProperty("linesValid").GetInt32().Should().Be(50);
    orderedPackages[1].GetProperty("lineCoveragePercent").GetDouble().Should().Be(70.0);
    orderedPackages[1].GetProperty("branchesCovered").GetInt32().Should().Be(10);
    orderedPackages[1].GetProperty("branchesValid").GetInt32().Should().Be(20);
    orderedPackages[1].GetProperty("branchCoveragePercent").GetDouble().Should().Be(50.0);

    var reportMarkdownPath = Path.Combine(outputDirectory.Path, "coverage-report.md");
    var markdown = await File.ReadAllTextAsync(reportMarkdownPath);
    markdown.Should().Contain("# Coverage Report");
    markdown.Should().Contain("## Totals");
    markdown.Should().Contain("- Line Coverage: 75.00% (75/100)");
    markdown.Should().Contain("- Branch Coverage: 60.00% (30/50)");
    markdown.Should().Contain("## Packages");
    markdown.Should().Contain("| Package | Line Coverage | Branch Coverage |");
    markdown.Should().Contain("| Critical.Assembly | 80.00% | 66.67% |");
    markdown.Should().Contain("| NonCritical.Assembly | 70.00% | 50.00% |");
  }

  [Fact]
  public async Task InvokeCoverageReport_WhenCoverageFileIsMissing_ReturnsFailure()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageReport.ps1");
    var missingCoveragePath = Path.Combine(Path.GetTempPath(), $"coverage-missing-{Guid.NewGuid():N}.xml");
    await using var outputDirectory = new TemporaryDirectory();

    var result = await ExecuteCoverageReportAsync(scriptPath, missingCoveragePath, outputDirectory.Path);

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("Coverage report file not found:");
  }

  [Theory]
  [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?><coverage lines-covered=\"1\" lines-valid=\"1\" branches-covered=\"1\" branches-valid=\"1\"></coverage>")]
  [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?><coverage lines-covered=\"1\" lines-valid=\"1\" branches-covered=\"1\" branches-valid=\"1\"><packages></packages></coverage>")]
  [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?><coverage lines-covered=\"1\" lines-valid=\"1\" branches-covered=\"1\" branches-valid=\"1\"><packages><package /></packages></coverage>")]
  public async Task InvokeCoverageReport_WhenPackagesAreMissingOrEmpty_ReturnsFailure(string coverageXml)
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageReport.ps1");
    await using var coverageFile = await CreateCoverageFileAsync(coverageXml);
    await using var outputDirectory = new TemporaryDirectory();

    var result = await ExecuteCoverageReportAsync(scriptPath, coverageFile.Path, outputDirectory.Path);

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("Cobertura report does not contain valid <packages>/<package> entries.");
  }

  [Fact]
  public async Task InvokeCoverageReport_WhenMetricsAreNotIntegers_ReturnsFailure()
  {
    const string coverageXml = """
      <?xml version="1.0" encoding="utf-8"?>
      <coverage lines-covered="not-a-number" lines-valid="100" branches-covered="30" branches-valid="50">
        <packages>
          <package name="Critical.Assembly" lines-covered="40" lines-valid="50" branches-covered="20" branches-valid="30" />
        </packages>
      </coverage>
      """;

    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageReport.ps1");
    await using var coverageFile = await CreateCoverageFileAsync(coverageXml);
    await using var outputDirectory = new TemporaryDirectory();

    var result = await ExecuteCoverageReportAsync(scriptPath, coverageFile.Path, outputDirectory.Path);

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("Cobertura attribute 'lines-covered' must be an integer, found 'not-a-number'.");
  }

  private static async Task<TemporaryCoverageFile> CreateCoverageFileAsync(string coverageXml)
  {
    var coveragePath = Path.Combine(Path.GetTempPath(), $"coverage-report-tests-{Guid.NewGuid():N}.xml");
    await File.WriteAllTextAsync(coveragePath, coverageXml, Encoding.UTF8);
    return new TemporaryCoverageFile(coveragePath);
  }

  private static async Task<ScriptResult> ExecuteCoverageReportAsync(string scriptPath, string coveragePath, string outputDirectory)
  {
    var shell = ResolvePowerShellPath();
    var startInfo = new ProcessStartInfo
    {
      FileName = shell,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
    };

    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-ExecutionPolicy");
    startInfo.ArgumentList.Add("Bypass");
    startInfo.ArgumentList.Add("-File");
    startInfo.ArgumentList.Add(scriptPath);
    startInfo.ArgumentList.Add("-CoverageReportPath");
    startInfo.ArgumentList.Add(coveragePath);
    startInfo.ArgumentList.Add("-OutputDirectory");
    startInfo.ArgumentList.Add(outputDirectory);

    using var process = Process.Start(startInfo);
    process.Should().NotBeNull();

    var stdOut = await process!.StandardOutput.ReadToEndAsync();
    var stdErr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    var output = string.Concat(stdOut, Environment.NewLine, stdErr);
    return new ScriptResult(process.ExitCode, output);
  }

  private static string ResolvePowerShellPath()
  {
    var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    var pathSeparator = OperatingSystem.IsWindows() ? ';' : ':';
    var pathEntries = pathVariable.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    foreach (var executable in GetCandidates())
    {
      foreach (var entry in pathEntries)
      {
        var candidate = Path.Combine(entry, executable);
        if (File.Exists(candidate))
        {
          return candidate;
        }
      }
    }

    throw new InvalidOperationException("PowerShell executable was not found on PATH.");
  }

  private static IEnumerable<string> GetCandidates()
  {
    if (OperatingSystem.IsWindows())
    {
      yield return "pwsh.exe";
      yield return "powershell.exe";
    }
    else
    {
      yield return "pwsh";
      yield return "powershell";
    }
  }

  private static string FindRepositoryRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      var solutionPath = Path.Combine(current.FullName, "STIGForge.sln");
      if (File.Exists(solutionPath))
      {
        return current.FullName;
      }

      current = current.Parent;
    }

    throw new InvalidOperationException("Unable to locate repository root from test base directory.");
  }

  private sealed class TemporaryDirectory : IAsyncDisposable
  {
    public TemporaryDirectory()
    {
      Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"coverage-report-tests-{Guid.NewGuid():N}");
      Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public ValueTask DisposeAsync()
    {
      if (Directory.Exists(Path))
      {
        Directory.Delete(Path, recursive: true);
      }

      return ValueTask.CompletedTask;
    }
  }

  private sealed class TemporaryCoverageFile(string path) : IAsyncDisposable
  {
    public string Path { get; } = path;

    public ValueTask DisposeAsync()
    {
      if (File.Exists(Path))
      {
        File.Delete(Path);
      }

      return ValueTask.CompletedTask;
    }
  }

  private sealed record ScriptResult(int ExitCode, string Output);
}

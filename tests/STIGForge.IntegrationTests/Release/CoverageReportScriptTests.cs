using System.Diagnostics;
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

    var reportMarkdownPath = Path.Combine(outputDirectory.Path, "coverage-report.md");
    var markdown = await File.ReadAllTextAsync(reportMarkdownPath);
    markdown.Should().Contain("Line Coverage");
    markdown.Should().Contain("Branch Coverage");
    markdown.Should().Contain("60.00%");
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

  private sealed record ScriptResult(int ExitCode, string Output);
}

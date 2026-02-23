using System.Diagnostics;
using System.Text;
using FluentAssertions;
using System.Text.Json;

namespace STIGForge.IntegrationTests.Release;

public sealed class CoverageGateScriptTests
{
  [Fact]
  public async Task InvokeCoverageGate_WhenScopedCoverageMeetsThreshold_ReturnsSuccess()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageGate.ps1");
    var coveragePath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "coverage-pass.cobertura.xml");
    await using var policyFile = await CreatePolicyFileAsync(85, ["Critical.Assembly"]);

    var result = await ExecuteCoverageGateAsync(scriptPath, coveragePath, policyFile.Path);

    result.ExitCode.Should().Be(0);
    result.Output.Should().Contain("Coverage gate passed");
    result.Output.Should().Contain("threshold=85");
    result.Output.Should().Contain("actual=90");
  }

  [Fact]
  public async Task InvokeCoverageGate_WhenScopedCoverageIsBelowThreshold_ReturnsFailure()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageGate.ps1");
    var coveragePath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "coverage-fail.cobertura.xml");
    await using var policyFile = await CreatePolicyFileAsync(85, ["Critical.Assembly"]);

    var result = await ExecuteCoverageGateAsync(scriptPath, coveragePath, policyFile.Path);

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("Coverage gate failed");
    result.Output.Should().Contain("threshold=85");
    result.Output.Should().Contain("actual=80");
  }

  [Fact]
  public async Task InvokeCoverageGate_WhenScopedCoverageIs84Point995AndThresholdIs85_ReturnsFailure()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageGate.ps1");
    var coveragePath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "coverage-84_995.cobertura.xml");
    await using var policyFile = await CreatePolicyFileAsync(85, ["Critical.Assembly"]);

    var result = await ExecuteCoverageGateAsync(scriptPath, coveragePath, policyFile.Path);

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("Coverage gate failed");
    result.Output.Should().Contain("threshold=85");
    result.Output.Should().Contain("actual=85");
  }

  [Fact]
  public async Task InvokeCoverageGate_WhenScopedCoverageIsExactly85AndThresholdIs85_ReturnsSuccess()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageGate.ps1");
    var coveragePath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "coverage-85_0.cobertura.xml");
    await using var policyFile = await CreatePolicyFileAsync(85, ["Critical.Assembly"]);

    var result = await ExecuteCoverageGateAsync(scriptPath, coveragePath, policyFile.Path);

    result.ExitCode.Should().Be(0);
    result.Output.Should().Contain("Coverage gate passed");
    result.Output.Should().Contain("threshold=85");
    result.Output.Should().Contain("actual=85");
  }

  [Theory]
  [InlineData(-0.01)]
  [InlineData(100.01)]
  public async Task InvokeCoverageGate_WhenThresholdIsOutsideZeroToHundredRange_ReturnsFailure(double invalidThreshold)
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageGate.ps1");
    var coveragePath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "coverage-pass.cobertura.xml");
    await using var policyFile = await CreatePolicyFileAsync(invalidThreshold, ["Critical.Assembly"]);

    var result = await ExecuteCoverageGateAsync(scriptPath, coveragePath, policyFile.Path);

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("minimumLineCoveragePercent must be between 0 and 100");
  }

  [Fact]
  public async Task InvokeCoverageGate_WhenAnyPolicyCriticalAssemblyIsMissing_ReturnsFailureWithMissingAssemblies()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageGate.ps1");
    var coveragePath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "coverage-pass.cobertura.xml");
    await using var policyFile = await CreatePolicyFileAsync(85, ["Critical.Assembly", "Missing.Assembly.B", "Missing.Assembly.A"]);

    var result = await ExecuteCoverageGateAsync(scriptPath, coveragePath, policyFile.Path);

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("missing policy critical assemblies: Missing.Assembly.A, Missing.Assembly.B");
  }

  private static async Task<TemporaryPolicyFile> CreatePolicyFileAsync(double minimumLineCoveragePercent, string[] criticalAssemblies)
  {
    var policyPath = Path.Combine(Path.GetTempPath(), $"coverage-gate-policy-{Guid.NewGuid():N}.json");
    var policyJson = JsonSerializer.Serialize(new
    {
      minimumLineCoveragePercent,
      criticalAssemblies,
    });

    await File.WriteAllTextAsync(policyPath, policyJson, Encoding.UTF8);
    return new TemporaryPolicyFile(policyPath);
  }

  private static async Task<ScriptResult> ExecuteCoverageGateAsync(string scriptPath, string coveragePath, string policyPath)
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
    startInfo.ArgumentList.Add("-PolicyPath");
    startInfo.ArgumentList.Add(policyPath);

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

  private sealed class TemporaryPolicyFile(string path) : IAsyncDisposable
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

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace STIGForge.IntegrationTests.Release;

public sealed class MutationPolicyScriptTests
{
  [Fact]
  public async Task InvokeMutationPolicy_WhenEnforcementDisabled_ReportsOnlyAndReturnsSuccess()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-MutationPolicy.ps1");
    var currentResultPath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "mutation-current-regress.json");
    await using var policyFile = await CreatePolicyFileAsync(85, 3);

    var result = await ExecuteMutationPolicyAsync(scriptPath, currentResultPath, policyFile.Path, enforce: false);

    result.ExitCode.Should().Be(0);
    result.Output.Should().Contain("Mutation policy report");
    result.Output.Should().Contain("mode=report");
    result.Output.Should().Contain("baseline=85");
    result.Output.Should().Contain("allowedRegression=3");
    result.Output.Should().Contain("minimumAllowed=82");
    result.Output.Should().Contain("current=81");
  }

  [Fact]
  public async Task InvokeMutationPolicy_WhenEnforcementEnabledAndWithinThreshold_ReturnsSuccess()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-MutationPolicy.ps1");
    var currentResultPath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "mutation-current-pass.json");
    await using var policyFile = await CreatePolicyFileAsync(85, 3);

    var result = await ExecuteMutationPolicyAsync(scriptPath, currentResultPath, policyFile.Path, enforce: true);

    result.ExitCode.Should().Be(0);
    result.Output.Should().Contain("Mutation policy passed");
    result.Output.Should().Contain("mode=enforce");
    result.Output.Should().Contain("minimumAllowed=82");
    result.Output.Should().Contain("current=82");
  }

  [Fact]
  public async Task InvokeMutationPolicy_WhenEnforcementEnabledAndBelowThreshold_ReturnsFailure()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-MutationPolicy.ps1");
    var currentResultPath = Path.Combine(repositoryRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "mutation-current-regress.json");
    await using var policyFile = await CreatePolicyFileAsync(85, 3);

    var result = await ExecuteMutationPolicyAsync(scriptPath, currentResultPath, policyFile.Path, enforce: true);

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("Mutation policy failed");
    result.Output.Should().Contain("mode=enforce");
    result.Output.Should().Contain("minimumAllowed=82");
    result.Output.Should().Contain("current=81");
  }

  private static async Task<TemporaryPolicyFile> CreatePolicyFileAsync(double baselineMutationScore, double allowedRegression)
  {
    var policyPath = Path.Combine(Path.GetTempPath(), $"mutation-policy-{Guid.NewGuid():N}.json");
    var policyJson = JsonSerializer.Serialize(new
    {
      baselineMutationScore,
      allowedRegression,
    });

    await File.WriteAllTextAsync(policyPath, policyJson, Encoding.UTF8);
    return new TemporaryPolicyFile(policyPath);
  }

  private static async Task<ScriptResult> ExecuteMutationPolicyAsync(string scriptPath, string currentResultPath, string policyPath, bool enforce)
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
    startInfo.ArgumentList.Add("-CurrentResultPath");
    startInfo.ArgumentList.Add(currentResultPath);
    startInfo.ArgumentList.Add("-PolicyPath");
    startInfo.ArgumentList.Add(policyPath);
    if (enforce)
    {
      startInfo.ArgumentList.Add("-Enforce");
    }

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

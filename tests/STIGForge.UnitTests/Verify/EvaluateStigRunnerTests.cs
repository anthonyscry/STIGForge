using FluentAssertions;
using System.Text.RegularExpressions;
using STIGForge.Verify;
using STIGForge.UnitTests.TestInfrastructure;

namespace STIGForge.UnitTests.Verify;

public sealed class EvaluateStigRunnerTests : IDisposable
{
  private readonly string _tempDir;

  public EvaluateStigRunnerTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-evaluate-runner-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  [RequiresPowerShellFact]
  public void Run_WhenScriptSetsLastExitCode_PropagatesFailureExitCode()
  {
    var scriptPath = Path.Combine(_tempDir, "Evaluate-STIG.ps1");
    File.WriteAllText(scriptPath, "$global:LASTEXITCODE = 9\nWrite-Error \"boom\"\n");

    var runner = new EvaluateStigRunner();
    var result = runner.Run(_tempDir, string.Empty, _tempDir);

    result.ExitCode.Should().Be(9);
    result.Error.Should().Contain("boom");
  }

  [RequiresPowerShellFact]
  public async Task RunAsync_WhenScriptSetsLastExitCode_PropagatesFailureExitCode()
  {
    var scriptPath = Path.Combine(_tempDir, "Evaluate-STIG.ps1");
    File.WriteAllText(scriptPath, "$global:LASTEXITCODE = 9\nWrite-Error \"boom\"\n");

    var runner = new EvaluateStigRunner();
    var result = await runner.RunAsync(_tempDir, string.Empty, _tempDir, CancellationToken.None);

    result.ExitCode.Should().Be(9);
    result.Error.Should().Contain("boom");
  }

  [RequiresPowerShellFact]
  public async Task RunAsync_WhenTimeoutExceeded_ThrowsTimeoutException()
  {
    var scriptPath = Path.Combine(_tempDir, "Evaluate-STIG.ps1");
    File.WriteAllText(scriptPath, "Start-Sleep -Seconds 5\n$global:LASTEXITCODE = 0\n");

    var runner = new EvaluateStigRunner();

    await FluentActions.Awaiting(() =>
        runner.RunAsync(_tempDir, string.Empty, _tempDir, CancellationToken.None, TimeSpan.FromMilliseconds(100)))
      .Should().ThrowAsync<TimeoutException>();
  }

  [RequiresPowerShellFact]
  public async Task RunAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
  {
    var scriptPath = Path.Combine(_tempDir, "Evaluate-STIG.ps1");
    File.WriteAllText(scriptPath, "Start-Sleep -Seconds 5\n$global:LASTEXITCODE = 0\n");

    var runner = new EvaluateStigRunner();
    using var cts = new CancellationTokenSource();

    var runTask = runner.RunAsync(_tempDir, string.Empty, _tempDir, cts.Token, TimeSpan.FromSeconds(10));
    cts.CancelAfter(TimeSpan.FromMilliseconds(100));

    await FluentActions.Awaiting(() => runTask)
      .Should().ThrowAsync<OperationCanceledException>();
  }

  [RequiresPowerShellFact]
  public async Task RunAsync_WhenArgumentsContainShellMetacharacters_DoesNotExecuteInjectedCommands()
  {
    var scriptPath = Path.Combine(_tempDir, "Evaluate-STIG.ps1");
    File.WriteAllText(scriptPath, "Write-Output ('ARGS:' + ($args -join '|'))\n$global:LASTEXITCODE = 0\n");

    var runner = new EvaluateStigRunner();
    var result = await runner.RunAsync(_tempDir, "-Output CKL; Write-Output HACKED", _tempDir, CancellationToken.None);

    result.ExitCode.Should().Be(0);
    result.Output.Should().Contain("ARGS:");
    Regex.IsMatch(result.Output, "(?m)^HACKED$").Should().BeFalse();
  }
}

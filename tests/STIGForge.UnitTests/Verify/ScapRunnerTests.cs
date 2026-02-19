using FluentAssertions;
using STIGForge.Verify;
using STIGForge.UnitTests.TestInfrastructure;

namespace STIGForge.UnitTests.Verify;

public sealed class ScapRunnerTests : IDisposable
{
  private readonly string _tempDir;

  public ScapRunnerTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-scap-runner-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  [RequiresShellFact]
  public async Task RunAsync_WhenCommandExitsNonZero_PropagatesExitCode()
  {
    var shell = ResolveShellOrSkip();
    var runner = new ScapRunner();

    var result = await runner.RunAsync(shell.CommandPath, shell.NonZeroExitArgs, _tempDir, CancellationToken.None);

    result.ExitCode.Should().Be(9);
  }

  [RequiresShellFact]
  public async Task RunAsync_WhenTimeoutExceeded_ThrowsTimeoutException()
  {
    var shell = ResolveShellOrSkip();
    var runner = new ScapRunner();

    await FluentActions.Awaiting(() =>
        runner.RunAsync(shell.CommandPath, shell.SleepArgs, _tempDir, CancellationToken.None, TimeSpan.FromMilliseconds(100)))
      .Should().ThrowAsync<TimeoutException>();
  }

  [RequiresShellFact]
  public async Task RunAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
  {
    var shell = ResolveShellOrSkip();
    var runner = new ScapRunner();
    using var cts = new CancellationTokenSource();

    var runTask = runner.RunAsync(shell.CommandPath, shell.SleepArgs, _tempDir, cts.Token, TimeSpan.FromSeconds(10));
    cts.CancelAfter(TimeSpan.FromMilliseconds(100));

    await FluentActions.Awaiting(() => runTask)
      .Should().ThrowAsync<OperationCanceledException>();
  }

  private static ShellCommand ResolveShellOrSkip()
  {
    if (OperatingSystem.IsWindows())
    {
      var commandPath = Environment.GetEnvironmentVariable("ComSpec") ?? string.Empty;
      if (!string.IsNullOrWhiteSpace(commandPath) && File.Exists(commandPath))
      {
        return new ShellCommand(
          commandPath,
          "/c exit /b 9",
          "/c timeout /t 5 /nobreak > nul");
      }
    }
    else
    {
      var commandPath = "/bin/sh";
      if (File.Exists(commandPath))
      {
        return new ShellCommand(
          commandPath,
          "-c \"exit 9\"",
          "-c \"sleep 5\"");
      }
    }

    throw new InvalidOperationException("No supported shell command path available for ScapRunner tests.");
  }

  private sealed record ShellCommand(string CommandPath, string NonZeroExitArgs, string SleepArgs);
}

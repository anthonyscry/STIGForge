using STIGForge.Verify;

namespace STIGForge.IntegrationTests.Verify;

public sealed class RunnerPathResolutionTests : IDisposable
{
  private readonly string _tempRoot;
  private readonly string? _originalPath;

  public RunnerPathResolutionTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-runner-path-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
    _originalPath = Environment.GetEnvironmentVariable("PATH");
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable("PATH", _originalPath);
    try { Directory.Delete(_tempRoot, recursive: true); } catch { }
  }

  [Fact]
  public void EvaluateStigRunner_Run_ResolvesNestedEvaluateStigFolder()
  {
    if (OperatingSystem.IsWindows())
      return;

    var toolParent = Path.Combine(_tempRoot, "tools", "Evaluate-STIG");
    var toolRoot = Path.Combine(toolParent, "Evaluate-STIG");
    Directory.CreateDirectory(toolRoot);
    File.WriteAllText(Path.Combine(toolRoot, "Evaluate-STIG.ps1"), "Write-Host 'ok'");

    var fakePowerShellDir = Path.Combine(_tempRoot, "fake-bin");
    Directory.CreateDirectory(fakePowerShellDir);
    var fakePowerShellPath = Path.Combine(fakePowerShellDir, "powershell.exe");
    File.WriteAllText(fakePowerShellPath, "#!/usr/bin/env bash\nprintf '%s\n' \"$@\"\nexit 0\n");
    MakeExecutable(fakePowerShellPath);
    PrependPath(fakePowerShellDir);

    var runner = new EvaluateStigRunner();
    var result = runner.Run(toolParent, "-AnswerFile ./AnswerFile.xml", workingDirectory: null);

    Assert.Equal(0, result.ExitCode);
    Assert.Contains("Evaluate-STIG.ps1", result.Output, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ScapRunner_Run_ResolvesDirectoryToNestedCsccExecutable()
  {
    if (OperatingSystem.IsWindows())
      return;

    var sccRoot = Path.Combine(_tempRoot, "tools", "SCC");
    var nestedFolder = Path.Combine(sccRoot, "SCC_5_10");
    Directory.CreateDirectory(nestedFolder);

    var csccExePath = Path.Combine(nestedFolder, "cscc.exe");
    File.WriteAllText(csccExePath, "#!/usr/bin/env bash\necho CSCC_OK\nexit 0\n");
    MakeExecutable(csccExePath);

    var runner = new ScapRunner();
    var result = runner.Run(sccRoot, string.Empty, workingDirectory: null);

    Assert.Equal(0, result.ExitCode);
    Assert.Contains("CSCC_OK", result.Output, StringComparison.Ordinal);
  }

  [Fact]
  public void ScapRunner_Run_PrefersCsccOverSccGuiBinary()
  {
    if (OperatingSystem.IsWindows())
      return;

    var sccRoot = Path.Combine(_tempRoot, "tools", "SCC_PREF");
    Directory.CreateDirectory(sccRoot);

    var sccGuiPath = Path.Combine(sccRoot, "scc.exe");
    File.WriteAllText(sccGuiPath, "#!/usr/bin/env bash\necho GUI\nexit 0\n");
    MakeExecutable(sccGuiPath);

    var csccRemotePath = Path.Combine(sccRoot, "cscc-remote.exe");
    File.WriteAllText(csccRemotePath, "#!/usr/bin/env bash\necho CSCC_REMOTE_OK\nexit 0\n");
    MakeExecutable(csccRemotePath);

    var runner = new ScapRunner();
    var result = runner.Run(sccRoot, string.Empty, workingDirectory: null);

    Assert.Equal(0, result.ExitCode);
    Assert.Contains("CSCC_REMOTE_OK", result.Output, StringComparison.Ordinal);
    Assert.DoesNotContain("GUI", result.Output, StringComparison.Ordinal);
  }

  [Fact]
  public void ScapRunner_Run_WithOnlySccGuiBinary_ThrowsGuidanceError()
  {
    if (OperatingSystem.IsWindows())
      return;

    var sccRoot = Path.Combine(_tempRoot, "tools", "SCC_GUI_ONLY");
    Directory.CreateDirectory(sccRoot);

    var sccGuiPath = Path.Combine(sccRoot, "scc.exe");
    File.WriteAllText(sccGuiPath, "#!/usr/bin/env bash\necho GUI\nexit 0\n");
    MakeExecutable(sccGuiPath);

    var runner = new ScapRunner();
    var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(sccRoot, string.Empty, workingDirectory: null));
    Assert.Contains("cscc", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  private void PrependPath(string directory)
  {
    var separator = OperatingSystem.IsWindows() ? ';' : ':';
    var existing = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    Environment.SetEnvironmentVariable("PATH", directory + separator + existing);
  }

  private static void MakeExecutable(string path)
  {
    if (OperatingSystem.IsWindows())
      return;

    File.SetUnixFileMode(
      path,
      UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
      UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
      UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
  }
}

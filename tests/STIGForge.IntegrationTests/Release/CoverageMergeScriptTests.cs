using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;

namespace STIGForge.IntegrationTests.Release;

public sealed class CoverageMergeScriptTests
{
  [Fact]
  public async Task InvokeCoverageMerge_WhenSourceHasCoverageFiles_MergesTotalsAndPackages()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageMerge.ps1");
    await using var sourceDirectory = await CreateFixtureCoverageDirectoryAsync();
    await using var outputPath = new TemporaryFile(Path.Combine(Path.GetTempPath(), $"coverage-merge-{Guid.NewGuid():N}.xml"));

    var result = await ExecuteCoverageMergeAsync(scriptPath, sourceDirectory.Path, outputPath.Path);

    result.ExitCode.Should().Be(0, "Script output: {0}", result.Output);
    result.Output.Should().Contain("Wrote merged coverage report");
    File.Exists(outputPath.Path).Should().BeTrue();

    var mergedDocument = XDocument.Load(outputPath.Path);
    mergedDocument.Root!.Name.LocalName.Should().Be("coverage");
    mergedDocument.Root.Attribute("lines-covered")!.Value.Should().Be("55");
    mergedDocument.Root.Attribute("lines-valid")!.Value.Should().Be("145");
    mergedDocument.Root.Attribute("branches-covered")!.Value.Should().Be("14");
    mergedDocument.Root.Attribute("branches-valid")!.Value.Should().Be("60");

    var packages = mergedDocument.Root.Element("packages")!.Elements("package").ToList();
    packages.Select(x => (string)x.Attribute("name")!).Should().Equal("Critical.Assembly", "NonCritical.Assembly", "Unique.Assembly");

    packages[0].Attribute("lines-covered")!.Value.Should().Be("25");
    packages[0].Attribute("lines-valid")!.Value.Should().Be("75");
    packages[0].Attribute("branches-covered")!.Value.Should().Be("7");
    packages[0].Attribute("branches-valid")!.Value.Should().Be("20");

    packages[1].Attribute("lines-covered")!.Value.Should().Be("10");
    packages[1].Attribute("lines-valid")!.Value.Should().Be("50");
    packages[1].Attribute("branches-covered")!.Value.Should().Be("4");
    packages[1].Attribute("branches-valid")!.Value.Should().Be("20");

    packages[2].Attribute("lines-covered")!.Value.Should().Be("20");
    packages[2].Attribute("lines-valid")!.Value.Should().Be("20");
    packages[2].Attribute("branches-covered")!.Value.Should().Be("3");
    packages[2].Attribute("branches-valid")!.Value.Should().Be("20");
  }

  [Fact]
  public async Task InvokeCoverageMerge_WhenNoCoverageFiles_FailsClearly()
  {
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "tools", "release", "Invoke-CoverageMerge.ps1");
    await using var sourceDirectory = new TemporaryDirectory();
    Directory.CreateDirectory(sourceDirectory.Path);
    await using var outputPath = new TemporaryFile(Path.Combine(Path.GetTempPath(), $"coverage-merge-empty-{Guid.NewGuid():N}.xml"));

    var result = await ExecuteCoverageMergeAsync(scriptPath, sourceDirectory.Path, outputPath.Path);

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("No coverage files found under");
    File.Exists(outputPath.Path).Should().BeFalse();
  }

  private static async Task<TemporaryDirectory> CreateFixtureCoverageDirectoryAsync()
  {
    var root = new TemporaryDirectory();
    var nested = Directory.CreateDirectory(Path.Combine(root.Path, "nested", "coverage"));

    var firstFilePath = Path.Combine(root.Path, "coverage.cobertura.xml");
    var nestedFilePath = Path.Combine(nested.FullName, "coverage.cobertura.xml");

    var firstXml = """
<?xml version="1.0" encoding="utf-8"?>
<coverage lines-covered="30" lines-valid="100" branches-covered="9" branches-valid="30" timestamp="1735689600" version="1.9">
  <packages>
    <package name="Critical.Assembly" lines-covered="20" lines-valid="50" branches-covered="5" branches-valid="10" />
    <package name="NonCritical.Assembly" lines-covered="10" lines-valid="50" branches-covered="4" branches-valid="20" />
  </packages>
</coverage>
""".Trim();

    var secondXml = """
<?xml version="1.0" encoding="utf-8"?>
<coverage lines-covered="25" lines-valid="45" branches-covered="5" branches-valid="30" timestamp="1735689600" version="1.9">
  <packages>
    <package name="Critical.Assembly" lines-covered="5" lines-valid="25" branches-covered="2" branches-valid="10" />
    <package name="Unique.Assembly" lines-covered="20" lines-valid="20" branches-covered="3" branches-valid="20" />
  </packages>
</coverage>
""".Trim();

    await File.WriteAllTextAsync(firstFilePath, firstXml, Encoding.UTF8);
    await File.WriteAllTextAsync(nestedFilePath, secondXml, Encoding.UTF8);

    return root;
  }

  private static async Task<ScriptResult> ExecuteCoverageMergeAsync(string scriptPath, string sourceDirectory, string outputPath)
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
    startInfo.ArgumentList.Add("-SourceDirectory");
    startInfo.ArgumentList.Add(sourceDirectory);
    startInfo.ArgumentList.Add("-OutputPath");
    startInfo.ArgumentList.Add(outputPath);

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
      Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"coverage-merge-tests-{Guid.NewGuid():N}");
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

  private sealed class TemporaryFile(string path) : IAsyncDisposable
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

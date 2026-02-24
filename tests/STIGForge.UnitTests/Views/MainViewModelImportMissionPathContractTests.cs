namespace STIGForge.UnitTests.Views;

public sealed class MainViewModelImportMissionPathContractTests
{
  [Fact]
  public void ResolveWorkflowLocalMissionJsonPath_TargetsLocalWorkflowOutputRoot()
  {
    var source = LoadSource("MainViewModel.ToolDefaults.cs");

    Assert.Contains("Path.Combine(_paths.GetAppDataRoot(), \"local-workflow\")", source, StringComparison.Ordinal);
    Assert.Contains("return Path.Combine(ResolveWorkflowLocalOutputRoot(), \"mission.json\");", source, StringComparison.Ordinal);
  }

  [Fact]
  public void PersistImportScanSummary_DoesNotOverwriteMissionJsonPath()
  {
    var source = LoadSource("MainViewModel.Import.cs");

    Assert.DoesNotContain("MissionJsonPath = jsonPath;", source, StringComparison.Ordinal);
    Assert.Contains("LastOutputPath = jsonPath;", source, StringComparison.Ordinal);
  }

  private static string LoadSource(string fileName)
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null && !File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
      current = current.Parent;

    Assert.True(current is not null, "Could not locate repository root containing STIGForge.sln.");

    var path = Path.Combine(current!.FullName, "src", "STIGForge.App", fileName);
    Assert.True(File.Exists(path), $"Expected source file at '{path}'.");

    return File.ReadAllText(path);
  }
}

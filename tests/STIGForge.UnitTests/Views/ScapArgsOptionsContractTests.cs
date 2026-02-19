namespace STIGForge.UnitTests.Views;

public sealed class ScapArgsOptionsContractTests
{
  [Fact]
  public void MainViewModel_UsesSafeDefault_ForScapIncludeF()
  {
    var source = LoadMainViewModelSource();

    Assert.Contains("[ObservableProperty] private bool scapIncludeF;", source, StringComparison.Ordinal);
    Assert.Contains("[ObservableProperty] private string scapArgs = \"-u\";", source, StringComparison.Ordinal);
    Assert.DoesNotContain("[ObservableProperty] private bool scapIncludeF = true;", source, StringComparison.Ordinal);
  }

  [Fact]
  public void MainViewModelDashboard_OnlyAddsFWhenValuePresent()
  {
    var source = LoadDashboardSource();

    Assert.Contains("var includeF = ScapIncludeF && hasValueInExtraArgs;", source, StringComparison.Ordinal);
    Assert.Contains("if (includeF)", source, StringComparison.Ordinal);
    Assert.Contains("ScapIncludeF = includeF;", source, StringComparison.Ordinal);
    Assert.Contains("SCAP argument '-f' was missing a filename; removed invalid switch.", LoadVerificationWorkflowSource(), StringComparison.Ordinal);
  }

  private static string LoadMainViewModelSource()
    => LoadSource("src", "STIGForge.App", "MainViewModel.cs");

  private static string LoadDashboardSource()
    => LoadSource("src", "STIGForge.App", "MainViewModel.Dashboard.cs");

  private static string LoadVerificationWorkflowSource()
    => LoadSource("src", "STIGForge.Verify", "VerificationWorkflowService.cs");

  private static string LoadSource(params string[] segments)
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null && !File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
      current = current.Parent;

    Assert.True(current is not null, "Could not locate repository root containing STIGForge.sln.");

    var path = Path.Combine(new[] { current!.FullName }.Concat(segments).ToArray());
    Assert.True(File.Exists(path), $"Expected source file at '{path}'.");

    return File.ReadAllText(path);
  }
}

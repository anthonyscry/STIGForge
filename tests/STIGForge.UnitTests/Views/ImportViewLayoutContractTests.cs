namespace STIGForge.UnitTests.Views;

public sealed class ImportViewLayoutContractTests
{
  [Fact]
  public void ImportView_ContainsRequiredReadabilitySections()
  {
    var xaml = LoadImportViewXaml();

    Assert.Contains("Primary Actions", xaml, StringComparison.Ordinal);
    Assert.Contains("Machine Context", xaml, StringComparison.Ordinal);
    Assert.Contains("Content Library", xaml, StringComparison.Ordinal);
    Assert.Contains("Pack Details", xaml, StringComparison.Ordinal);
  }

  [Fact]
  public void ImportView_PreservesPrimaryActionBindings_WhenSectioned()
  {
    var xaml = LoadImportViewXaml();

    Assert.Contains("{Binding ScanImportFolderCommand}", xaml, StringComparison.Ordinal);
    Assert.Contains("{Binding OpenImportFolderCommand}", xaml, StringComparison.Ordinal);
    Assert.Contains("{Binding ComparePacksCommand}", xaml, StringComparison.Ordinal);
    Assert.Contains("{Binding OpenContentPickerCommand}", xaml, StringComparison.Ordinal);
  }

  [Fact]
  public void ImportView_ContainsMachineAndLibraryReadabilityLabels()
  {
    var xaml = LoadImportViewXaml();

    Assert.Contains("Scan context", xaml, StringComparison.Ordinal);
    Assert.Contains("Library filters", xaml, StringComparison.Ordinal);
    Assert.Contains("Library actions", xaml, StringComparison.Ordinal);
  }

  private static string LoadImportViewXaml()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null && !File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
      current = current.Parent;

    Assert.True(current is not null, "Could not locate repository root containing STIGForge.sln.");

    var importViewPath = Path.Combine(current!.FullName, "src", "STIGForge.App", "Views", "ImportView.xaml");
    Assert.True(File.Exists(importViewPath), $"Expected ImportView XAML at '{importViewPath}'.");

    return File.ReadAllText(importViewPath);
  }
}

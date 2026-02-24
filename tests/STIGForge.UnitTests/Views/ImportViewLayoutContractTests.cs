using System.Xml.Linq;

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

  [Fact]
  public void ImportView_ContainsAutoWorkspaceSubtabs()
  {
    var xaml = LoadImportViewXaml();

    Assert.Contains("Header=\"Auto Import\"", xaml, StringComparison.Ordinal);
    Assert.Contains("Header=\"Classification Results\"", xaml, StringComparison.Ordinal);
    Assert.Contains("Header=\"Exceptions Queue\"", xaml, StringComparison.Ordinal);
    Assert.Contains("Header=\"Activity Log\"", xaml, StringComparison.Ordinal);
  }

  [Fact]
  public void ImportView_BindsWorkspaceCollections()
  {
    var xaml = LoadImportViewXaml();

    Assert.Contains("SelectedIndex=\"{Binding SelectedImportWorkspaceTabIndex}\"", xaml, StringComparison.Ordinal);
    Assert.Contains("{Binding AutoImportQueueRows}", xaml, StringComparison.Ordinal);
    Assert.Contains("{Binding ClassificationResultRows}", xaml, StringComparison.Ordinal);
    Assert.Contains("{Binding ExceptionQueueRows}", xaml, StringComparison.Ordinal);
    Assert.Contains("{Binding ImportActivityLogRows}", xaml, StringComparison.Ordinal);
  }

  [Fact]
  public void ImportView_RendersQueueRowsWithImportColumns()
  {
    var xaml = LoadImportViewXaml();
    var view = XDocument.Parse(xaml);
    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    AssertQueueTabColumns(view, presentation, "Auto Import");
    AssertQueueTabColumns(view, presentation, "Classification Results");
    AssertQueueTabColumns(view, presentation, "Exceptions Queue");

    static void AssertQueueTabColumns(XDocument view, XNamespace presentation, string tabHeader)
    {
      var tabItem = view
        .Descendants(presentation + "TabItem")
        .FirstOrDefault(node => string.Equals((string?)node.Attribute("Header"), tabHeader, StringComparison.Ordinal));

      Assert.True(tabItem is not null, $"Expected TabItem with Header='{tabHeader}'.");

      var headers = tabItem!
        .Descendants(presentation + "GridViewColumn")
        .Select(node => (string?)node.Attribute("Header"))
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();

      Assert.Contains("FileName", headers, StringComparer.Ordinal);
      Assert.Contains("ArtifactKind", headers, StringComparer.Ordinal);
      Assert.Contains("State", headers, StringComparer.Ordinal);
      Assert.Contains("Detail", headers, StringComparer.Ordinal);
    }
  }

  [Fact]
  public void ImportView_UsesTextWrappingOnLongStatusAndDetailFields()
  {
    var xaml = LoadImportViewXaml();
    var view = XDocument.Parse(xaml);
    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    AssertWrappedTextBlock(view, presentation, "{Binding MachineScanSummary}");
    AssertWrappedTextBlock(view, presentation, "{Binding SelectedContentSummary}");
    AssertWrappedTextBlock(view, presentation, "{Binding PackDetailRoot}");

    static void AssertWrappedTextBlock(XDocument view, XNamespace presentation, string binding)
    {
      var textBlock = view
        .Descendants(presentation + "TextBlock")
        .FirstOrDefault(node => string.Equals((string?)node.Attribute("Text"), binding, StringComparison.Ordinal));

      Assert.True(textBlock is not null, $"Expected TextBlock with Text='{binding}'.");
      Assert.Equal("Wrap", (string?)textBlock!.Attribute("TextWrapping"));
    }
  }

  [Fact]
  public void ImportView_CodeBehindSurfacesMissionJsonPathBindingProperty()
  {
    var codeBehind = LoadImportViewCodeBehind();

    Assert.Contains("MissionJsonPathProperty", codeBehind, StringComparison.Ordinal);
    Assert.Contains("MainViewModel.MissionJsonPath", codeBehind, StringComparison.Ordinal);
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

  private static string LoadImportViewCodeBehind()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null && !File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
      current = current.Parent;

    Assert.True(current is not null, "Could not locate repository root containing STIGForge.sln.");

    var importViewCodeBehindPath = Path.Combine(current!.FullName, "src", "STIGForge.App", "Views", "ImportView.xaml.cs");
    Assert.True(File.Exists(importViewCodeBehindPath), $"Expected ImportView code-behind at '{importViewCodeBehindPath}'.");

    return File.ReadAllText(importViewCodeBehindPath);
  }
}

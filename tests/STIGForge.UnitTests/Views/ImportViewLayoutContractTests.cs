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

    Assert.Contains("{Binding AutoImportQueueRows}", xaml, StringComparison.Ordinal);
    Assert.Contains("{Binding ClassificationResultRows}", xaml, StringComparison.Ordinal);
    Assert.Contains("{Binding ExceptionQueueRows}", xaml, StringComparison.Ordinal);
    Assert.Contains("{Binding ImportActivityLogRows}", xaml, StringComparison.Ordinal);
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

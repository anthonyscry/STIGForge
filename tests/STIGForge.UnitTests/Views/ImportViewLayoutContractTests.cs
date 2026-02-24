using System.Xml.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using STIGForge.App;
using STIGForge.App.Views;
using STIGForge.UnitTests.Helpers;

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
  public void ImportView_DefinesMissionJsonDependencyPropertyContract()
  {
    var type = typeof(ImportView);
    var field = type.GetField("MissionJsonPathProperty", BindingFlags.Public | BindingFlags.Static);
    Assert.NotNull(field);
    Assert.Equal(typeof(DependencyProperty), field!.FieldType);

    var dp = field.GetValue(null) as DependencyProperty;
    Assert.NotNull(dp);
    Assert.Equal("MissionJsonPath", dp!.Name);
    Assert.Equal(typeof(string), dp.PropertyType);
    Assert.Equal(typeof(ImportView), dp.OwnerType);

    var property = type.GetProperty("MissionJsonPath", BindingFlags.Public | BindingFlags.Instance);
    Assert.NotNull(property);
    Assert.Equal(typeof(string), property!.PropertyType);
    Assert.NotNull(property.GetMethod);
    Assert.NotNull(property.SetMethod);
    Assert.False(property.SetMethod!.IsPublic);
  }

  [Fact]
  public void ImportView_ContainsLifecycleHooksForMissionPathBindingSync()
  {
    var type = typeof(ImportView);

    var onLoaded = type.GetMethod("OnLoaded", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(object), typeof(RoutedEventArgs) }, null);
    Assert.NotNull(onLoaded);
    Assert.Equal(typeof(void), onLoaded!.ReturnType);

    var onUnloaded = type.GetMethod("OnUnloaded", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(object), typeof(RoutedEventArgs) }, null);
    Assert.NotNull(onUnloaded);
    Assert.Equal(typeof(void), onUnloaded!.ReturnType);

    var onDataContextChanged = type.GetMethod("OnDataContextChanged", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    Assert.NotNull(onDataContextChanged);

    var onViewModelPropertyChanged = type.GetMethod("OnViewModelPropertyChanged", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(onViewModelPropertyChanged);

    var updateSurface = type.GetMethod("UpdateMissionJsonPathBindingSurface", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(updateSurface);
  }

  [Fact]
  public void ImportView_MissionPathSyncs_WhenDataContextSetBeforeLoaded()
  {
    StaThreadRunner.Run(() =>
    {
      var view = new ImportView();
      var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

      var onLoaded = typeof(ImportView).GetMethod("OnLoaded", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(object), typeof(RoutedEventArgs) }, null);
      var onUnloaded = typeof(ImportView).GetMethod("OnUnloaded", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(object), typeof(RoutedEventArgs) }, null);
      Assert.NotNull(onLoaded);
      Assert.NotNull(onUnloaded);

      view.DataContext = vm;
      onLoaded!.Invoke(view, new object[] { view, new RoutedEventArgs(FrameworkElement.LoadedEvent) });

      const string missionPath = @"C:\temp\mission.json";
      vm.MissionJsonPath = missionPath;

      Assert.Equal(missionPath, view.MissionJsonPath);

      onUnloaded!.Invoke(view, new object[] { view, new RoutedEventArgs(FrameworkElement.UnloadedEvent) });
    });
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

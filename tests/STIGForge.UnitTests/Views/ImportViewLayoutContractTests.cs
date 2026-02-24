using System.Xml.Linq;
using System.Reflection;
using System.Windows;
using STIGForge.App.Views;

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

    var onLoaded = type.GetMethod("OnLoaded", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(onLoaded);
    Assert.Equal(typeof(void), onLoaded!.ReturnType);

    var onUnloaded = type.GetMethod("OnUnloaded", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(onUnloaded);
    Assert.Equal(typeof(void), onUnloaded!.ReturnType);

    var onDataContextChanged = type.GetMethod("OnDataContextChanged", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(onDataContextChanged);

    var onViewModelPropertyChanged = type.GetMethod("OnViewModelPropertyChanged", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(onViewModelPropertyChanged);

    var updateSurface = type.GetMethod("UpdateMissionJsonPathBindingSurface", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(updateSurface);
  }

  [Fact]
  public void ImportView_OnDataContextChanged_BindsDirectlyWithoutLoadGate()
  {
    var type = typeof(ImportView);
    var onDataContextChanged = type.GetMethod("OnDataContextChanged", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(onDataContextChanged);

    var bindToViewModel = type.GetMethod("BindToViewModel", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(bindToViewModel);

    var boundViewModelField = type.GetField("_boundViewModel", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(boundViewModelField);

    var memberTokens = ReadReferencedMemberTokens(onDataContextChanged!);
    Assert.Contains(bindToViewModel!.MetadataToken, memberTokens);
    Assert.DoesNotContain(boundViewModelField!.MetadataToken, memberTokens);
  }

  private static HashSet<int> ReadReferencedMemberTokens(MethodInfo method)
  {
    var body = method.GetMethodBody();
    Assert.True(body is not null, $"Expected method body for '{method.Name}'.");

    var il = body!.GetILAsByteArray();
    Assert.True(il is not null && il.Length > 0, $"Expected IL for '{method.Name}'.");

    var tokens = new HashSet<int>();
    var i = 0;
    while (i < il!.Length)
    {
      var op = il[i++];
      if (op == 0xFE)
      {
        if (i >= il.Length)
          break;

        var ext = il[i++];
        if (ext == 0x16 || ext == 0x17 || ext == 0x18 || ext == 0x19 || ext == 0x1A || ext == 0x1B || ext == 0x1C || ext == 0x1D)
        {
          if (i + 3 >= il.Length)
            break;

          tokens.Add(BitConverter.ToInt32(il, i));
          i += 4;
        }

        continue;
      }

      if (op == 0x28 || op == 0x6F || op == 0x73 || op == 0x74 || op == 0x7B || op == 0x7C || op == 0x7D || op == 0x7E || op == 0x7F || op == 0x80)
      {
        if (i + 3 >= il.Length)
          break;

        tokens.Add(BitConverter.ToInt32(il, i));
        i += 4;
        continue;
      }

      i += GetOperandSize(op);
    }

    return tokens;
  }

  private static int GetOperandSize(byte op)
  {
    return op switch
    {
      0x00 => 0,
      0x01 => 0,
      0x02 => 0,
      0x03 => 0,
      0x04 => 0,
      0x05 => 0,
      0x06 => 0,
      0x07 => 0,
      0x08 => 0,
      0x09 => 0,
      0x0A => 0,
      0x0B => 0,
      0x0C => 0,
      0x0D => 0,
      0x0E => 1,
      0x0F => 1,
      0x10 => 1,
      0x11 => 1,
      0x12 => 1,
      0x13 => 1,
      0x14 => 0,
      0x15 => 0,
      0x16 => 0,
      0x17 => 0,
      0x18 => 0,
      0x19 => 0,
      0x1A => 0,
      0x1B => 0,
      0x1C => 0,
      0x1D => 0,
      0x1E => 0,
      0x1F => 1,
      0x20 => 4,
      0x21 => 8,
      0x22 => 4,
      0x23 => 8,
      0x25 => 0,
      0x26 => 0,
      0x27 => 0,
      0x29 => 8,
      0x2A => 0,
      0x2B => 1,
      0x2C => 1,
      0x2D => 1,
      0x2E => 1,
      0x2F => 1,
      0x30 => 1,
      0x31 => 1,
      0x32 => 1,
      0x33 => 1,
      0x34 => 1,
      0x35 => 1,
      0x36 => 1,
      0x37 => 1,
      0x38 => 4,
      0x39 => 4,
      0x3A => 4,
      0x3B => 4,
      0x3C => 4,
      0x3D => 4,
      0x3E => 4,
      0x3F => 4,
      0x40 => 4,
      0x41 => 4,
      0x42 => 4,
      0x43 => 4,
      0x44 => 4,
      0x45 => 4,
      0x46 => 4,
      0x47 => 4,
      0x48 => 4,
      0x49 => 4,
      0x4A => 4,
      0x4B => 4,
      0x4C => 4,
      0x4D => 4,
      0x4E => 4,
      0x4F => 4,
      0x50 => 4,
      0x51 => 4,
      0x52 => 4,
      0x53 => 4,
      0x54 => 4,
      0x55 => 4,
      0x56 => 4,
      0x57 => 4,
      0x58 => 0,
      0x59 => 0,
      0x5A => 0,
      0x5B => 0,
      0x5C => 0,
      0x5D => 0,
      0x5E => 0,
      0x5F => 0,
      0x60 => 0,
      0x61 => 0,
      0x62 => 0,
      0x63 => 0,
      0x64 => 0,
      0x65 => 0,
      0x66 => 0,
      0x67 => 0,
      0x68 => 0,
      0x69 => 0,
      0x6A => 0,
      0x6B => 0,
      0x6C => 0,
      0x6D => 0,
      0x6E => 0,
      0x70 => 4,
      0x71 => 4,
      0x72 => 4,
      0x75 => 4,
      0x76 => 4,
      0x79 => 4,
      0x7A => 4,
      0x81 => 4,
      0x82 => 4,
      0x83 => 4,
      0x84 => 2,
      0x85 => 4,
      0x86 => 4,
      0x87 => 4,
      0x88 => 4,
      0x89 => 4,
      0x8A => 4,
      0x8B => 4,
      0x8C => 4,
      0x8D => 4,
      0x8E => 0,
      0x8F => 0,
      0x90 => 0,
      0x91 => 0,
      0x92 => 0,
      0x93 => 0,
      0x94 => 0,
      0x95 => 0,
      0x96 => 0,
      0x97 => 0,
      0x98 => 0,
      0x99 => 0,
      0x9A => 0,
      0x9B => 0,
      0x9C => 0,
      0x9D => 0,
      0x9E => 0,
      0x9F => 0,
      0xA0 => 0,
      0xA1 => 0,
      0xA2 => 0,
      0xA3 => 0,
      0xA4 => 0,
      0xA5 => 0,
      0xB3 => 1,
      0xB4 => 4,
      0xB5 => 0,
      _ => 0
    };
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

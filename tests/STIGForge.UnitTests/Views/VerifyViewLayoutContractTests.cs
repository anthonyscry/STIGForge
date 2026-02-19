using System.Xml.Linq;

namespace STIGForge.UnitTests.Views;

public sealed class VerifyViewLayoutContractTests
{
  [Fact]
  public void VerifyView_SeparatesScanParametersAndToolMappingsIntoVerifyAndSettingsTabs()
  {
    var xaml = LoadVerifyViewXaml();
    var view = XDocument.Parse(xaml);
    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    var tabs = view
      .Descendants(presentation + "TabItem")
      .Select(node => (string?)node.Attribute("Header"))
      .Where(value => !string.IsNullOrWhiteSpace(value))
      .ToArray();

    Assert.Contains("Verify", tabs, StringComparer.Ordinal);
    Assert.Contains("Settings", tabs, StringComparer.Ordinal);
  }

  [Fact]
  public void VerifyView_ProvidesScannerModeSelectionOnVerifyTab()
  {
    var xaml = LoadVerifyViewXaml();
    var view = XDocument.Parse(xaml);
    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    var verifyTab = view
      .Descendants(presentation + "TabItem")
      .First(node => string.Equals((string?)node.Attribute("Header"), "Verify", StringComparison.Ordinal));

    Assert.Contains(verifyTab.Descendants(presentation + "ComboBox"),
      node => string.Equals((string?)node.Attribute("SelectedItem"), "{Binding VerifyScannerMode}", StringComparison.Ordinal));
    Assert.DoesNotContain(verifyTab.Descendants(presentation + "TextBox"),
      node => string.Equals((string?)node.Attribute("Text"), "{Binding EvaluateStigRoot}", StringComparison.Ordinal));
    Assert.DoesNotContain(verifyTab.Descendants(presentation + "TextBox"),
      node => string.Equals((string?)node.Attribute("Text"), "{Binding ScapCommandPath}", StringComparison.Ordinal));
  }

  [Fact]
  public void VerifyView_SettingsTabContainsToolMappingsAndPowerStigPaths()
  {
    var xaml = LoadVerifyViewXaml();
    var view = XDocument.Parse(xaml);
    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    var settingsTab = view
      .Descendants(presentation + "TabItem")
      .First(node => string.Equals((string?)node.Attribute("Header"), "Settings", StringComparison.Ordinal));

    Assert.Contains(settingsTab.Descendants(presentation + "TextBox"),
      node => string.Equals((string?)node.Attribute("Text"), "{Binding EvaluateStigRoot}", StringComparison.Ordinal));
    Assert.Contains(settingsTab.Descendants(presentation + "TextBox"),
      node => string.Equals((string?)node.Attribute("Text"), "{Binding ScapCommandPath}", StringComparison.Ordinal));
    Assert.Contains(settingsTab.Descendants(presentation + "TextBox"),
      node => string.Equals((string?)node.Attribute("Text"), "{Binding PowerStigModulePath}", StringComparison.Ordinal));
  }

  private static string LoadVerifyViewXaml()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null && !File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
      current = current.Parent;

    Assert.True(current is not null, "Could not locate repository root containing STIGForge.sln.");

    var verifyViewPath = Path.Combine(current!.FullName, "src", "STIGForge.App", "Views", "VerifyView.xaml");
    Assert.True(File.Exists(verifyViewPath), $"Expected VerifyView XAML at '{verifyViewPath}'.");

    return File.ReadAllText(verifyViewPath);
  }
}

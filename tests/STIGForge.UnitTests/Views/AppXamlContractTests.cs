using System.Xml.Linq;

namespace STIGForge.UnitTests.Views;

public sealed class AppXamlContractTests
{
  [Fact]
  public void AppXaml_DefinesSingleImplicitWindowStyle_WithSegoeUiDefault()
  {
    var xaml = LoadAppXaml();
    var document = XDocument.Parse(xaml);
    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    var windowStyles = document
      .Descendants(presentation + "Style")
      .Where(node => string.Equals((string?)node.Attribute("TargetType"), "Window", StringComparison.Ordinal))
      .ToList();

    Assert.Single(windowStyles);

    var windowStyle = windowStyles[0];
    var setters = windowStyle.Elements(presentation + "Setter").ToList();

    Assert.Contains(setters, setter =>
      string.Equals((string?)setter.Attribute("Property"), "FontFamily", StringComparison.Ordinal)
      && string.Equals((string?)setter.Attribute("Value"), "Segoe UI", StringComparison.Ordinal));

    Assert.Contains(setters, setter =>
      string.Equals((string?)setter.Attribute("Property"), "FontSize", StringComparison.Ordinal)
      && string.Equals((string?)setter.Attribute("Value"), "13", StringComparison.Ordinal));
  }

  private static string LoadAppXaml()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null && !File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
      current = current.Parent;

    Assert.True(current is not null, "Could not locate repository root containing STIGForge.sln.");

    var appXamlPath = Path.Combine(current!.FullName, "src", "STIGForge.App", "App.xaml");
    Assert.True(File.Exists(appXamlPath), $"Expected app XAML at '{appXamlPath}'.");

    return File.ReadAllText(appXamlPath);
  }
}

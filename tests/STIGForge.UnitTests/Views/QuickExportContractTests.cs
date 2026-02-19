using STIGForge.Export;

namespace STIGForge.UnitTests.Views;

public sealed class QuickExportContractTests
{
    [Fact]
    public void ExportView_ContainsQuickExportTab()
    {
        var xaml = LoadExportViewXaml();
        Assert.Contains("Header=\"Quick Export\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportView_QuickExportTab_AppearsBeforeEmassTab()
    {
        var xaml = LoadExportViewXaml();
        var quickExportIndex = xaml.IndexOf("Header=\"Quick Export\"", StringComparison.Ordinal);
        var emassIndex = xaml.IndexOf("Header=\"eMASS\"", StringComparison.Ordinal);

        Assert.True(quickExportIndex > 0, "Quick Export tab not found");
        Assert.True(emassIndex > 0, "eMASS tab not found");
        Assert.True(quickExportIndex < emassIndex, "Quick Export tab should appear before eMASS tab");
    }

    [Fact]
    public void ExportView_QuickExportTab_AppearsAfterDashboardTab()
    {
        var xaml = LoadExportViewXaml();
        var dashboardIndex = xaml.IndexOf("Header=\"Dashboard\"", StringComparison.Ordinal);
        var quickExportIndex = xaml.IndexOf("Header=\"Quick Export\"", StringComparison.Ordinal);

        Assert.True(dashboardIndex > 0, "Dashboard tab not found");
        Assert.True(quickExportIndex > 0, "Quick Export tab not found");
        Assert.True(dashboardIndex < quickExportIndex, "Quick Export should appear after Dashboard");
    }

    [Fact]
    public void ExportView_ContainsFormatComboBox()
    {
        var xaml = LoadExportViewXaml();
        Assert.Contains("{Binding ExportFormatNames}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedExportFormat}", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportView_ContainsQuickExportCommand()
    {
        var xaml = LoadExportViewXaml();
        Assert.Contains("{Binding QuickExportCommand}", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportView_ContainsSystemNameAndFileNameInputs()
    {
        var xaml = LoadExportViewXaml();
        Assert.Contains("{Binding QuickExportSystemName}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding QuickExportFileName}", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportView_QuickExportButton_UsesActionsEnabled()
    {
        var xaml = LoadExportViewXaml();
        // Find the Quick Export section and verify it has ActionsEnabled binding
        var quickSection = xaml.Substring(
            xaml.IndexOf("Header=\"Quick Export\"", StringComparison.Ordinal),
            xaml.IndexOf("Header=\"eMASS\"", StringComparison.Ordinal)
              - xaml.IndexOf("Header=\"Quick Export\"", StringComparison.Ordinal));
        Assert.Contains("{Binding ActionsEnabled}", quickSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportView_ContainsQuickExportStatus()
    {
        var xaml = LoadExportViewXaml();
        Assert.Contains("{Binding QuickExportStatus}", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportAdapterRegistry_RegistersFourAdapters()
    {
        var registry = new ExportAdapterRegistry();
        registry.Register(new CklExportAdapter());
        registry.Register(new XccdfExportAdapter());
        registry.Register(new CsvExportAdapter());
        registry.Register(new ExcelExportAdapter());

        var all = registry.GetAll();
        Assert.Equal(4, all.Count);
    }

    [Fact]
    public void ExportAdapterRegistry_ResolvesAllRegisteredFormats()
    {
        var registry = new ExportAdapterRegistry();
        registry.Register(new CklExportAdapter());
        registry.Register(new XccdfExportAdapter());
        registry.Register(new CsvExportAdapter());
        registry.Register(new ExcelExportAdapter());

        Assert.NotNull(registry.TryResolve("CKL"));
        Assert.NotNull(registry.TryResolve("XCCDF"));
        Assert.NotNull(registry.TryResolve("CSV"));
        Assert.NotNull(registry.TryResolve("Excel"));
    }

    [Fact]
    public void ExportView_KeepsExistingTabs()
    {
        var xaml = LoadExportViewXaml();
        Assert.Contains("Header=\"Dashboard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"eMASS\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Audit Log\"", xaml, StringComparison.Ordinal);
    }

    private static string LoadExportViewXaml()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
            current = current.Parent;

        Assert.True(current is not null, "Could not locate repository root containing STIGForge.sln.");

        var viewPath = Path.Combine(current!.FullName, "src", "STIGForge.App", "Views", "ExportView.xaml");
        Assert.True(File.Exists(viewPath), $"Expected ExportView XAML at '{viewPath}'.");

        return File.ReadAllText(viewPath);
    }
}

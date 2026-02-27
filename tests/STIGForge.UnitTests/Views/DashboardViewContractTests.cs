using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace STIGForge.UnitTests.Views;

public sealed class DashboardViewContractTests
{
    [Fact]
    public void DashboardView_DefinesComplianceSummaryCard()
    {
        var document = LoadDashboardViewDocument();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var summaryLabel = document
            .Descendants(presentation + "TextBlock")
            .FirstOrDefault(node => string.Equals((string?)node.Attribute("Text"), "Compliance Summary", StringComparison.Ordinal));

        Assert.NotNull(summaryLabel);

        var donut = document
            .Descendants()
            .FirstOrDefault(node => string.Equals(node.Name.LocalName, "ComplianceDonutChart", StringComparison.Ordinal));

        Assert.NotNull(donut);
        Assert.Contains("TotalRuleCount", document.ToString(), StringComparison.Ordinal);
    }

    private static XDocument LoadDashboardViewDocument()
    {
        var path = LocateDashboardViewPath();
        var content = File.ReadAllText(path);
        return XDocument.Parse(content);
    }

    private static string LocateDashboardViewPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
        {
            current = current.Parent;
        }

        Assert.True(current is not null, "Could not locate repository root containing STIGForge.sln.");

        var dashboardPath = Path.Combine(current.FullName, "src", "STIGForge.App", "Views", "DashboardView.xaml");
        Assert.True(File.Exists(dashboardPath), $"Expected DashboardView at '{dashboardPath}'.");
        return dashboardPath;
    }
}

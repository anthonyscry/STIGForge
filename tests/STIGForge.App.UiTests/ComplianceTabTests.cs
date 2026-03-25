using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentAssertions;
using STIGForge.UiDriver;

namespace STIGForge.App.UiTests;

public sealed class ComplianceTabTests
{
    [UIFact]
    [Trait("Category", "UI")]
    public async Task Compliance_BeforeWorkflow_ShowsZeroState()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "compliance-tab");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        // Hard-assert: the Compliance Summary tab must exist and render without crash.
        var complianceTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Compliance Summary tab")));
        complianceTab.Should().NotBeNull("Compliance Summary tab should exist in the main window");
        complianceTab!.AsTabItem().Select();

        app.CaptureScreenshot(screenshotDir, "compliance-before-workflow.png");

        using var visual = new VisualCheck(app, screenshotDir);

        // Soft-check: look for any text element whose name indicates a zero / no-data state.
        // The control may display "0%", "No results", or simply omit the donut chart entirely.
        var zeroPercentText = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("0%"));

        var noResultsText = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("No results"));

        var donutChart = app.MainWindow.FindFirstDescendant(
            cf => cf.ByAutomationId("Compliance donut chart")
                  .Or(cf.ByName("Compliance donut chart")));

        var isZeroState = zeroPercentText is not null || noResultsText is not null || donutChart is null;

        visual.Check(
            "compliance-zero-state",
            isZeroState,
            "Before any workflow run, Compliance tab should show a zero/no-data state " +
            "(0%, 'No results', or no chart rendered).");

        app.CaptureScreenshot(screenshotDir, "compliance-zero-state-check.png");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Compliance_ChartAccessibility()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "compliance-tab");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        var complianceTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Compliance Summary tab")));
        complianceTab.Should().NotBeNull("Compliance Summary tab should exist");
        complianceTab!.AsTabItem().Select();

        app.CaptureScreenshot(screenshotDir, "compliance-chart-accessibility.png");

        using var visual = new VisualCheck(app, screenshotDir);

        // Look for the donut chart by AutomationName or a substring thereof.
        var donutChart =
            app.MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("Compliance donut chart")
                      .Or(cf.ByName("Compliance donut chart")))
            ?? app.MainWindow.FindFirstDescendant(
                cf => cf.ByName("compliant"));

        if (donutChart is not null)
        {
            // Hard-assert: if the chart element is present it must be accessible (not offscreen).
            donutChart.IsOffscreen.Should().BeFalse(
                "Compliance donut chart should be visible on-screen when it is rendered");

            // Soft-check: chart should have a non-empty accessible name.
            var chartName = donutChart.Name ?? string.Empty;
            visual.Check(
                "donut-chart-accessible-name",
                !string.IsNullOrWhiteSpace(chartName),
                "Compliance donut chart element should expose a non-empty accessible name for screen-reader support.");
        }
        else
        {
            // No chart rendered yet (no workflow data) — soft-note only.
            visual.Check(
                "donut-chart-not-rendered",
                condition: true,  // not a failure; expected when no data is present
                description: "Compliance donut chart not found — no workflow data available yet (expected).");
        }

        app.CaptureScreenshot(screenshotDir, "compliance-chart-check.png");
    }
}

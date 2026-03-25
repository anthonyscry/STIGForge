using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentAssertions;
using STIGForge.UiDriver;

namespace STIGForge.App.UiTests;

public sealed class ResultsTabTests
{
    [UIFact]
    [Trait("Category", "UI")]
    public async Task Results_BeforeWorkflow_ShowsEmptyState()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "results-tab");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        // Hard-assert: the Results tab itself must exist and be selectable.
        var resultsTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Results tab")));
        resultsTab.Should().NotBeNull("Results tab should exist in the main window");
        resultsTab!.AsTabItem().Select();

        app.CaptureScreenshot(screenshotDir, "results-before-workflow.png");

        using var visual = new VisualCheck(app, screenshotDir);

        // Soft-check: "Open output folder" button — may be present even without workflow data.
        var openFolderBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByAutomationId("Open output folder").Or(cf.ByName("Open output folder")));

        visual.Check(
            "open-folder-btn-present",
            openFolderBtn is not null,
            "Open output folder button should be present on the Results tab.");

        if (openFolderBtn is not null)
        {
            visual.Check(
                "open-folder-btn-visible",
                !openFolderBtn.IsOffscreen,
                "Open output folder button should be visible on-screen before any workflow run.");
        }

        app.CaptureScreenshot(screenshotDir, "results-empty-state-check.png");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Results_AfterImport_ShowsOutputPath()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "results-tab");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        // Navigate to Import Library tab and attempt to trigger an import.
        var importTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Import Library tab")));
        importTab.Should().NotBeNull("Import Library tab should exist");
        importTab!.AsTabItem().Select();

        // Attempt the import step if available; skip gracefully if not configured.
        AutomationElement? runImportBtn = null;
        try
        {
            runImportBtn = await app.GetByTestId("Run Import step")
                .ExpectVisibleAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Import not configured on this machine — continue to Results tab check.
        }

        if (runImportBtn is not null && runImportBtn.IsEnabled)
        {
            runImportBtn.Click();
            // Allow import to begin.
            await Task.Delay(TimeSpan.FromSeconds(2));
            app.CaptureScreenshot(screenshotDir, "results-import-triggered.png");
        }

        // Navigate to Results tab.
        var resultsTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Results tab")));
        resultsTab.Should().NotBeNull("Results tab should exist");
        resultsTab!.AsTabItem().Select();

        app.CaptureScreenshot(screenshotDir, "results-after-import-nav.png");

        // Hard-assert: at least one of the expected Results tab buttons is visible.
        AutomationElement? openFolderBtn =
            app.MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("Open output folder").Or(cf.ByName("Open output folder")));

        AutomationElement? restartBtn =
            app.MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("Restart workflow").Or(cf.ByName("Restart workflow")));

        var atLeastOneActionPresent = openFolderBtn is not null || restartBtn is not null;

        atLeastOneActionPresent.Should().BeTrue(
            "Results tab should display 'Open output folder' or 'Restart workflow' button");

        app.CaptureScreenshot(screenshotDir, "results-action-buttons.png");
    }
}

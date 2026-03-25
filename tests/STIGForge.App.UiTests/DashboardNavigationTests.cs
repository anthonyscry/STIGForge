using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FluentAssertions;
using STIGForge.UiDriver;

namespace STIGForge.App.UiTests;

public sealed class DashboardNavigationTests
{
    [UIFact]
    [Trait("Category", "UI")]
    public async Task Dashboard_AllTabsAccessible()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "dashboard-nav");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        var tabNames = new[]
        {
            "Import Library tab",
            "Workflow tab",
            "Results tab",
            "Compliance Summary tab",
        };

        foreach (var tabName in tabNames)
        {
            var tab = app.MainWindow.FindFirstDescendant(
                cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName(tabName)));

            tab.Should().NotBeNull($"Tab '{tabName}' should exist in the main window");
            tab!.AsTabItem().Select();

            app.CaptureScreenshot(screenshotDir, $"tab-{tabName.Replace(' ', '-').ToLowerInvariant()}.png");
        }
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Dashboard_TabSwitching_PreservesState()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "dashboard-nav");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        // Navigate to Workflow tab and confirm its content is visible.
        var workflowTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Workflow tab")));
        workflowTab.Should().NotBeNull("Workflow tab should exist");
        workflowTab!.AsTabItem().Select();

        await app.GetByTestId("Run Scan step").ExpectVisibleAsync();

        // Switch away to Results tab.
        var resultsTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Results tab")));
        resultsTab.Should().NotBeNull("Results tab should exist");
        resultsTab!.AsTabItem().Select();

        app.CaptureScreenshot(screenshotDir, "tab-switch-results.png");

        // Switch back to Workflow tab and verify content is still present.
        workflowTab.AsTabItem().Select();

        await app.GetByTestId("Run Scan step").ExpectVisibleAsync();

        app.CaptureScreenshot(screenshotDir, "tab-switch-workflow-return.png");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Dashboard_HeaderButtons_RenderWithIcons()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "dashboard-nav");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        var helpElement = await app.GetByTestId("help-button").ExpectVisibleAsync();
        var aboutElement = await app.GetByTestId("about-button").ExpectVisibleAsync();
        var settingsElement = await app.GetByTestId("settings-button").ExpectVisibleAsync();

        using var visual = new VisualCheck(app, screenshotDir);

        var helpBounds = helpElement.BoundingRectangle;
        visual.Check(
            "icon-help",
            helpBounds.Width >= 30 && helpBounds.Height >= 30,
            $"Help button bounding rect {helpBounds.Width}x{helpBounds.Height} is smaller than 30x30 — icon may not be rendering.");

        var aboutBounds = aboutElement.BoundingRectangle;
        visual.Check(
            "icon-about",
            aboutBounds.Width >= 30 && aboutBounds.Height >= 30,
            $"About button bounding rect {aboutBounds.Width}x{aboutBounds.Height} is smaller than 30x30 — icon may not be rendering.");

        var settingsBounds = settingsElement.BoundingRectangle;
        visual.Check(
            "icon-settings",
            settingsBounds.Width >= 30 && settingsBounds.Height >= 30,
            $"Settings button bounding rect {settingsBounds.Width}x{settingsBounds.Height} is smaller than 30x30 — icon may not be rendering.");

        // Hard-assert: buttons are present and on-screen (VisualCheck is soft-only above).
        helpElement.IsOffscreen.Should().BeFalse("Help button should be on screen");
        aboutElement.IsOffscreen.Should().BeFalse("About button should be on screen");
        settingsElement.IsOffscreen.Should().BeFalse("Settings button should be on screen");

        app.CaptureScreenshot(screenshotDir, "header-buttons-icons.png");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Dashboard_KeyboardShortcuts_F1OpensHelp()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "dashboard-nav");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        // Ensure the main window has focus before sending the keystroke.
        app.MainWindow.SetForeground();
        app.MainWindow.Focus();

        Keyboard.Press(VirtualKeyShort.F1);

        // Wait briefly for any modal/help window to appear.
        await Task.Delay(TimeSpan.FromSeconds(2));

        app.CaptureScreenshot(screenshotDir, "f1-help-open.png");

        var modalWindows = app.MainWindow.ModalWindows;
        modalWindows.Should().NotBeEmpty("pressing F1 should open a help window or dialog");

        // Dismiss the modal.
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        await Task.Delay(TimeSpan.FromSeconds(1));

        app.CaptureScreenshot(screenshotDir, "f1-help-closed.png");
    }
}

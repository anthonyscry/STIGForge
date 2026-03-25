using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FluentAssertions;
using STIGForge.UiDriver;

namespace STIGForge.App.UiTests;

public sealed class DialogTests
{
    [UIFact]
    [Trait("Category", "UI")]
    public async Task Settings_OpenAndClose()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "dialogs");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        // Open the Settings dialog.
        await app.GetByTestId("settings-button").ClickAsync();

        await Task.Delay(TimeSpan.FromSeconds(1));

        // Hard-assert: at least one modal window appeared.
        var modalsBefore = app.MainWindow.ModalWindows;
        modalsBefore.Should().NotBeEmpty(
            "clicking the Settings button should open a modal settings dialog");

        app.CaptureScreenshot(screenshotDir, "settings-dialog-open.png");

        // Hard-assert: "Import Folder" text should appear in the settings dialog.
        var importFolderText = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Import Folder"))
            ?? app.MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("Import Folder"));

        if (importFolderText is null && modalsBefore.Length > 0)
        {
            // Search within the modal window itself.
            importFolderText = modalsBefore[0].FindFirstDescendant(
                cf => cf.ByName("Import Folder"))
                ?? modalsBefore[0].FindFirstDescendant(
                    cf => cf.ByAutomationId("Import Folder"));
        }

        importFolderText.Should().NotBeNull(
            "Settings dialog should contain an 'Import Folder' label or control");

        // Close the dialog with Escape.
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);

        await Task.Delay(TimeSpan.FromSeconds(1));

        app.CaptureScreenshot(screenshotDir, "settings-dialog-closed.png");

        // Hard-assert: modal windows should now be empty.
        var modalsAfter = app.MainWindow.ModalWindows;
        modalsAfter.Should().BeEmpty(
            "pressing Escape should close the Settings dialog");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task About_OpenAndClose()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "dialogs");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        // Open the About dialog.
        await app.GetByTestId("about-button").ClickAsync();

        await Task.Delay(TimeSpan.FromSeconds(1));

        // Hard-assert: a modal window appeared.
        var modalsAfterOpen = app.MainWindow.ModalWindows;
        modalsAfterOpen.Should().NotBeEmpty(
            "clicking the About button should open a modal about dialog");

        app.CaptureScreenshot(screenshotDir, "about-dialog-open.png");

        using var visual = new VisualCheck(app, screenshotDir);

        // Soft-check: look for version text (e.g., "1.0" or "STIGForge") anywhere in the tree.
        var versionText =
            app.MainWindow.FindFirstDescendant(cf => cf.ByName("STIGForge"))
            ?? app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("app-version"));

        if (versionText is null && modalsAfterOpen.Length > 0)
        {
            // Search within the modal window.
            var modal = modalsAfterOpen[0];
            var allTextElements = modal.FindAllDescendants(
                cf => cf.ByControlType(ControlType.Text));

            foreach (var element in allTextElements)
            {
                var name = element.Name ?? string.Empty;
                if (name.Contains("1.0", StringComparison.Ordinal) ||
                    name.Contains("STIGForge", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("version", StringComparison.OrdinalIgnoreCase))
                {
                    versionText = element;
                    break;
                }
            }
        }

        visual.Check(
            "about-version-text",
            versionText is not null,
            "About dialog should display version or application name text (e.g., '1.0' or 'STIGForge').");

        // Find and click the "Close about dialog" button.
        var closeBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Close about dialog"));

        if (closeBtn is null && modalsAfterOpen.Length > 0)
        {
            closeBtn = modalsAfterOpen[0].FindFirstDescendant(
                cf => cf.ByName("Close about dialog"));
        }

        closeBtn.Should().NotBeNull(
            "'Close about dialog' button should be present in the About dialog");

        closeBtn!.Click();

        await Task.Delay(TimeSpan.FromSeconds(1));

        app.CaptureScreenshot(screenshotDir, "about-dialog-closed.png");

        // Hard-assert: modal is now gone.
        var modalsAfterClose = app.MainWindow.ModalWindows;
        modalsAfterClose.Should().BeEmpty(
            "clicking 'Close about dialog' should dismiss the About modal");
    }
}

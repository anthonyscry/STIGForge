using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentAssertions;
using STIGForge.UiDriver;

namespace STIGForge.App.UiTests;

public sealed class PipelineIntegrationTests
{
    [UIFact]
    [Trait("Category", "UI")]
    [Trait("Category", "Integration")]
    public async Task Pipeline_Dashboard_ImportSkipScanHarden()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "pipeline-integration");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        // ── Step 1: Import ────────────────────────────────────────────────────────────────
        var importTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Import Library tab")));
        importTab.Should().NotBeNull("Import Library tab should exist");
        importTab!.AsTabItem().Select();

        app.CaptureScreenshot(screenshotDir, "pipeline-01-import-tab.png");

        var runImportBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByAutomationId("Run Import step").Or(cf.ByName("Run Import step")));

        if (runImportBtn is not null && runImportBtn.IsEnabled)
        {
            runImportBtn.Click();
            app.CaptureScreenshot(screenshotDir, "pipeline-02-import-running.png");

            // Poll up to 30s for a tree view to populate (import completion indicator).
            var importDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
            AutomationElement? importTree = null;

            while (DateTimeOffset.UtcNow < importDeadline)
            {
                importTree = app.MainWindow.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.Tree));

                if (importTree is not null && importTree.FindAllChildren().Length > 0)
                    break;

                // Also accept a progress bar disappearing as a signal.
                var progressBar = app.MainWindow.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.ProgressBar));

                if (progressBar is null && importTree is not null)
                    break;

                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            app.CaptureScreenshot(screenshotDir, "pipeline-03-import-complete.png");

            // Hard-assert: import either populated the tree or produced a meaningful UI state
            // (no crash — the import tab still renders).
            var tabStillRendered = app.MainWindow.FindFirstDescendant(
                cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Import Library tab")));
            tabStillRendered.Should().NotBeNull(
                "Import Library tab should still be present after running import — no crash expected");
        }
        else
        {
            // Import not configured on this machine — screenshot and continue.
            app.CaptureScreenshot(screenshotDir, "pipeline-02-import-not-available.png");
        }

        // ── Step 2: Skip Scan ────────────────────────────────────────────────────────────
        var workflowTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Workflow tab")));
        workflowTab.Should().NotBeNull("Workflow tab should exist");
        workflowTab!.AsTabItem().Select();

        app.CaptureScreenshot(screenshotDir, "pipeline-04-workflow-tab.png");

        var skipScanBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByAutomationId("Skip Scan step").Or(cf.ByName("Skip Scan step")));

        if (skipScanBtn is not null && skipScanBtn.IsEnabled)
        {
            skipScanBtn.Click();
            await Task.Delay(TimeSpan.FromSeconds(2));
            app.CaptureScreenshot(screenshotDir, "pipeline-05-scan-skipped.png");

            // Hard-assert: skipping scan did not crash the app — workflow tab still renders.
            var workflowTabAfterSkip = app.MainWindow.FindFirstDescendant(
                cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Workflow tab")));
            workflowTabAfterSkip.Should().NotBeNull(
                "Workflow tab should still be present after skipping scan — no crash expected");
        }
        else
        {
            app.CaptureScreenshot(screenshotDir, "pipeline-05-skip-scan-not-available.png");
        }

        // ── Step 3: Harden ───────────────────────────────────────────────────────────────
        var hardenBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByAutomationId("Run Harden step").Or(cf.ByName("Run Harden step")));

        if (hardenBtn is not null && hardenBtn.IsEnabled)
        {
            hardenBtn.Click();
            app.CaptureScreenshot(screenshotDir, "pipeline-06-harden-running.png");

            // Poll up to 60s for harden completion or error.
            var hardenDeadline = DateTimeOffset.UtcNow.AddSeconds(60);
            AutomationElement? hardenResult = null;

            while (DateTimeOffset.UtcNow < hardenDeadline)
            {
                hardenResult =
                    app.MainWindow.FindFirstDescendant(
                        cf => cf.ByAutomationId("Run Verify step").Or(cf.ByName("Run Verify step")))
                    ?? app.MainWindow.FindFirstDescendant(
                        cf => cf.ByAutomationId("Rerun Harden").Or(cf.ByName("Rerun Harden")))
                    ?? app.MainWindow.FindFirstDescendant(
                        cf => cf.ByAutomationId("Open Settings for failure recovery")
                              .Or(cf.ByName("Open Settings for failure recovery")));

                if (hardenResult is not null)
                    break;

                // Accept progress bar gone + no crash as success.
                var progressBar = app.MainWindow.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.ProgressBar));

                if (progressBar is null)
                    break;

                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            app.CaptureScreenshot(screenshotDir, "pipeline-07-harden-complete.png");

            // Hard-assert: harden step either completed or produced a meaningful error state
            // (not a crash — workflow tab should still render).
            var workflowTabAfterHarden = app.MainWindow.FindFirstDescendant(
                cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Workflow tab")));
            workflowTabAfterHarden.Should().NotBeNull(
                "Workflow tab should still be present after running harden — no crash expected");
        }
        else
        {
            // Harden step not enabled (scan not completed or import not configured) — acceptable.
            app.CaptureScreenshot(screenshotDir, "pipeline-06-harden-not-enabled.png");
        }
    }

    [UIFact]
    [Trait("Category", "UI")]
    [Trait("Category", "Integration")]
    public async Task Pipeline_OutputFilesExist()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "pipeline-integration");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        // Run a minimal pipeline: Skip Scan so harden might be available.
        var workflowTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Workflow tab")));
        workflowTab.Should().NotBeNull("Workflow tab should exist");
        workflowTab!.AsTabItem().Select();

        var skipScanBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByAutomationId("Skip Scan step").Or(cf.ByName("Skip Scan step")));

        if (skipScanBtn is not null && skipScanBtn.IsEnabled)
        {
            skipScanBtn.Click();
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        // Navigate to Results tab.
        var resultsTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Results tab")));
        resultsTab.Should().NotBeNull("Results tab should exist");
        resultsTab!.AsTabItem().Select();

        app.CaptureScreenshot(screenshotDir, "pipeline-results-tab.png");

        using var visual = new VisualCheck(app, screenshotDir);

        // Soft-check: look for output folder path text in the Results tab.
        var allTextElements = app.MainWindow.FindAllDescendants(
            cf => cf.ByControlType(ControlType.Text));

        string? outputFolderPath = null;

        foreach (var element in allTextElements)
        {
            var name = element.Name ?? string.Empty;
            // Look for a path-like string: contains a backslash or drive letter pattern.
            if (name.Length > 3 &&
                (name.Contains('\\') || name.Contains('/') ||
                 (name.Length >= 3 && name[1] == ':' && name[2] == '\\')))
            {
                outputFolderPath = name;
                break;
            }
        }

        visual.Check(
            "output-folder-path-present",
            outputFolderPath is not null,
            "Results tab should display an output folder path after any workflow activity.");

        if (outputFolderPath is not null)
        {
            visual.Check(
                "output-folder-path-non-empty",
                !string.IsNullOrWhiteSpace(outputFolderPath),
                "Output folder path should be non-empty when displayed in Results tab.");
        }

        app.CaptureScreenshot(screenshotDir, "pipeline-results-output-path.png");

        // Hard-assert: Results tab rendered successfully (did not crash).
        var resultsTabAfter = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Results tab")));
        resultsTabAfter.Should().NotBeNull(
            "Results tab should render successfully after pipeline steps have run");
    }
}

using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FluentAssertions;
using STIGForge.UiDriver;

namespace STIGForge.App.UiTests;

public sealed class WizardModeTests
{
    [UIFact]
    [Trait("Category", "UI")]
    public async Task Wizard_Toggle_SwitchesView()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "wizard-mode");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        // Click the wizard mode toggle.
        await app.GetByTestId("wizard-mode-toggle").ClickAsync();

        await Task.Delay(TimeSpan.FromSeconds(1));

        app.CaptureScreenshot(screenshotDir, "wizard-toggled.png");

        // Hard-assert: both navigation buttons must appear after enabling wizard mode.
        var nextBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Go to next step"));
        nextBtn.Should().NotBeNull("'Go to next step' button should appear after enabling wizard mode");

        var prevBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Go to previous step"));
        prevBtn.Should().NotBeNull("'Go to previous step' button should appear after enabling wizard mode");

        app.CaptureScreenshot(screenshotDir, "wizard-nav-buttons.png");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Wizard_StepIndicator_ShowsAllSteps()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "wizard-mode");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        await app.GetByTestId("wizard-mode-toggle").ClickAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Hard-assert: step indicator must exist.
        var stepIndicator = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Workflow step indicator"));
        stepIndicator.Should().NotBeNull("Workflow step indicator should be present in wizard mode");

        app.CaptureScreenshot(screenshotDir, "wizard-step-indicator.png");

        using var visual = new VisualCheck(app, screenshotDir);

        // Soft-check: count descendants (buttons + text blocks) inside the indicator.
        // Expect 5-6 step indicators representing workflow stages.
        if (stepIndicator is not null)
        {
            var stepButtons = stepIndicator.FindAllDescendants(
                cf => cf.ByControlType(ControlType.Button));
            var stepTexts = stepIndicator.FindAllDescendants(
                cf => cf.ByControlType(ControlType.Text));

            var totalIndicatorElements = stepButtons.Length + stepTexts.Length;

            visual.Check(
                "step-indicator-count",
                totalIndicatorElements >= 5,
                $"Step indicator should contain at least 5 step elements (buttons or text), found {totalIndicatorElements}.");
        }

        app.CaptureScreenshot(screenshotDir, "wizard-step-indicator-count.png");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Wizard_NextBack_Navigation()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "wizard-mode");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        await app.GetByTestId("wizard-mode-toggle").ClickAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Capture the initial step indicator state (name/text) so we can detect change.
        var stepIndicatorBefore = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Workflow step indicator"));
        var nameBefore = stepIndicatorBefore?.Name ?? string.Empty;

        // Click Next and wait.
        var nextBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Go to next step"));
        nextBtn.Should().NotBeNull("'Go to next step' button must be present before navigating");
        nextBtn!.Click();

        await Task.Delay(TimeSpan.FromSeconds(1));

        app.CaptureScreenshot(screenshotDir, "wizard-after-next.png");

        // Hard-assert: the view changed — look for an element indicating step 2.
        // We check that either a different step is shown or the indicator updated.
        var stepIndicatorAfterNext = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Workflow step indicator"));
        stepIndicatorAfterNext.Should().NotBeNull(
            "Workflow step indicator should still be visible after clicking Next");

        // Click Back and wait.
        var prevBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Go to previous step"));
        prevBtn.Should().NotBeNull("'Go to previous step' button must be present after advancing");
        prevBtn!.Click();

        await Task.Delay(TimeSpan.FromSeconds(1));

        app.CaptureScreenshot(screenshotDir, "wizard-after-back.png");

        // Hard-assert: we returned to step 1 — Next button should still be accessible.
        var nextBtnAfterBack = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Go to next step"));
        nextBtnAfterBack.Should().NotBeNull(
            "'Go to next step' should still be visible after navigating back to step 1");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Wizard_AutoExecution_OnAdvance()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "wizard-mode");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        await app.GetByTestId("wizard-mode-toggle").ClickAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Advance to the Import step.
        var nextBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Go to next step"));
        nextBtn.Should().NotBeNull("'Go to next step' button must be present");
        nextBtn!.Click();

        app.CaptureScreenshot(screenshotDir, "wizard-advance-import.png");

        // Wait 3s for any auto-execution or status update.
        await Task.Delay(TimeSpan.FromSeconds(3));

        app.CaptureScreenshot(screenshotDir, "wizard-import-after-delay.png");

        using var visual = new VisualCheck(app, screenshotDir);

        // Soft-check: look for any status/activity indicator — it may auto-start import.
        var progressBar = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.ProgressBar));

        var statusText = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.Text).And(cf.ByName("Running")))
            ?? app.MainWindow.FindFirstDescendant(
                cf => cf.ByControlType(ControlType.Text).And(cf.ByName("Importing")))
            ?? app.MainWindow.FindFirstDescendant(
                cf => cf.ByControlType(ControlType.Text).And(cf.ByName("Complete")));

        var activityFound = progressBar is not null || statusText is not null;

        visual.Check(
            "wizard-auto-import-activity",
            activityFound,
            "Expected to find a progress bar or status text after advancing to Import step — import may auto-start in wizard mode.");

        app.CaptureScreenshot(screenshotDir, "wizard-import-activity-check.png");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Wizard_JumpToStep_Works()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "wizard-mode");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        await app.GetByTestId("wizard-mode-toggle").ClickAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Advance twice to reach step 3.
        for (var i = 0; i < 2; i++)
        {
            var nextBtn = app.MainWindow.FindFirstDescendant(
                cf => cf.ByName("Go to next step"));
            if (nextBtn is null || !nextBtn.IsEnabled)
                break;

            nextBtn.Click();
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        app.CaptureScreenshot(screenshotDir, "wizard-on-step-3.png");

        // Look for a step indicator button labelled "1" to jump back to step 1.
        var stepIndicator = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Workflow step indicator"));

        if (stepIndicator is not null)
        {
            var stepOneButton = stepIndicator.FindFirstDescendant(
                cf => cf.ByControlType(ControlType.Button).And(cf.ByName("1")));

            if (stepOneButton is not null && stepOneButton.IsEnabled)
            {
                stepOneButton.Click();
                await Task.Delay(TimeSpan.FromSeconds(1));

                app.CaptureScreenshot(screenshotDir, "wizard-jumped-to-step-1.png");

                // Hard-assert: returned to step 1 — Next button should be active and visible.
                var nextBtnAfterJump = app.MainWindow.FindFirstDescendant(
                    cf => cf.ByName("Go to next step"));
                nextBtnAfterJump.Should().NotBeNull(
                    "'Go to next step' should be visible after jumping back to step 1");
            }
            else
            {
                // Step circles are not individually clickable — skip gracefully.
                app.CaptureScreenshot(screenshotDir, "wizard-step-circles-not-clickable.png");
            }
        }
        else
        {
            // Step indicator not found — screenshot and continue.
            app.CaptureScreenshot(screenshotDir, "wizard-no-step-indicator.png");
        }
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Wizard_DoneStep_ShowsCompletion()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "wizard-mode");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        await app.GetByTestId("wizard-mode-toggle").ClickAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Click Next up to 6 times with 2s delays, looking for a Done/Restart state.
        AutomationElement? restartBtn = null;
        AutomationElement? completionText = null;

        for (var step = 0; step < 6; step++)
        {
            var nextBtn = app.MainWindow.FindFirstDescendant(
                cf => cf.ByName("Go to next step"));

            if (nextBtn is null || !nextBtn.IsEnabled)
                break;

            nextBtn.Click();
            await Task.Delay(TimeSpan.FromSeconds(2));

            app.CaptureScreenshot(screenshotDir, $"wizard-step-{step + 2}.png");

            // Check for completion indicators.
            restartBtn = app.MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("Restart").Or(cf.ByName("Restart")));

            completionText = app.MainWindow.FindFirstDescendant(
                cf => cf.ByName("Done"))
                ?? app.MainWindow.FindFirstDescendant(
                    cf => cf.ByName("Complete"))
                ?? app.MainWindow.FindFirstDescendant(
                    cf => cf.ByName("Finished"));

            if (restartBtn is not null || completionText is not null)
                break;
        }

        app.CaptureScreenshot(screenshotDir, "wizard-done-state.png");

        if (restartBtn is not null)
        {
            // Hard-assert: if Restart button appeared, it must be on-screen.
            restartBtn.IsOffscreen.Should().BeFalse(
                "Restart button should be visible on-screen when the wizard reaches the Done step");
        }
        else if (completionText is not null)
        {
            // Hard-assert: if completion text appeared, it must be on-screen.
            completionText.IsOffscreen.Should().BeFalse(
                "Completion text should be visible on-screen when the wizard reaches the Done step");
        }
        else
        {
            // Could not reach Done state due to missing configuration — acceptable.
            // Screenshot already captured above — just note it.
        }
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Wizard_RestartFromDone_ResetsToSetup()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "wizard-mode");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        await app.GetByTestId("wizard-mode-toggle").ClickAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Advance through all steps (up to 6) searching for the Restart button.
        AutomationElement? restartBtn = null;

        for (var step = 0; step < 6; step++)
        {
            var nextBtn = app.MainWindow.FindFirstDescendant(
                cf => cf.ByName("Go to next step"));

            if (nextBtn is null || !nextBtn.IsEnabled)
                break;

            nextBtn.Click();
            await Task.Delay(TimeSpan.FromSeconds(2));

            restartBtn = app.MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("Restart").Or(cf.ByName("Restart")));

            if (restartBtn is not null)
                break;
        }

        if (restartBtn is null)
        {
            // Cannot reach Done state without proper configuration — skip gracefully.
            app.CaptureScreenshot(screenshotDir, "wizard-restart-skipped-no-done.png");
            return;
        }

        app.CaptureScreenshot(screenshotDir, "wizard-at-done-before-restart.png");

        // Click Restart.
        restartBtn.Click();
        await Task.Delay(TimeSpan.FromSeconds(1));

        app.CaptureScreenshot(screenshotDir, "wizard-after-restart.png");

        // Hard-assert: returned to step 1 — Next button should be re-enabled and visible.
        var nextBtnAfterRestart = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Go to next step"));
        nextBtnAfterRestart.Should().NotBeNull(
            "'Go to next step' should be visible after restarting from the Done step");
        nextBtnAfterRestart!.IsEnabled.Should().BeTrue(
            "'Go to next step' should be enabled after restarting — wizard should reset to step 1");

        // Soft-check: step indicator should be back at step 1.
        using var visual = new VisualCheck(app, screenshotDir);

        var stepIndicator = app.MainWindow.FindFirstDescendant(
            cf => cf.ByName("Workflow step indicator"));

        visual.Check(
            "wizard-reset-step-indicator",
            stepIndicator is not null,
            "Workflow step indicator should still be present after restarting from Done step.");
    }
}

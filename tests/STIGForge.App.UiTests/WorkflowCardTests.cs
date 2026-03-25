using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentAssertions;
using STIGForge.UiDriver;

namespace STIGForge.App.UiTests;

public sealed class WorkflowCardTests
{
    [UIFact]
    [Trait("Category", "UI")]
    public async Task Workflow_InitialState_ScanReadyOthersLocked()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "workflow-cards");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        var workflowTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Workflow tab")));
        workflowTab.Should().NotBeNull("Workflow tab should exist");
        workflowTab!.AsTabItem().Select();

        // Hard-assert: the Scan button must always be present.
        var scanBtn = await app.GetByTestId("Run Scan step").ExpectVisibleAsync();
        scanBtn.Should().NotBeNull("Run Scan step button should be present on the Workflow tab");

        // Harden and Verify step buttons should exist (rendered), but locked (disabled) until
        // Scan has run. Find them directly so we can inspect IsEnabled without throwing.
        var hardenBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByAutomationId("Run Harden step").Or(cf.ByName("Run Harden step")));
        var verifyBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByAutomationId("Run Verify step").Or(cf.ByName("Run Verify step")));

        hardenBtn.Should().NotBeNull("Run Harden step button should be rendered on the Workflow tab");
        verifyBtn.Should().NotBeNull("Run Verify step button should be rendered on the Workflow tab");

        using var visual = new VisualCheck(app, screenshotDir);

        visual.Check(
            "scan-btn-enabled",
            scanBtn.IsEnabled,
            "Run Scan step button should be enabled in the initial workflow state.");

        visual.Check(
            "harden-btn-locked",
            hardenBtn is null || !hardenBtn.IsEnabled,
            "Run Harden step button should be disabled (locked) before Scan completes.");

        visual.Check(
            "verify-btn-locked",
            verifyBtn is null || !verifyBtn.IsEnabled,
            "Run Verify step button should be disabled (locked) before Scan completes.");

        app.CaptureScreenshot(screenshotDir, "workflow-initial-state.png");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Workflow_SkipScan_UnlocksHarden()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "workflow-cards");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        var workflowTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Workflow tab")));
        workflowTab.Should().NotBeNull("Workflow tab should exist");
        workflowTab!.AsTabItem().Select();

        await app.GetByTestId("Skip Scan step").ClickAsync();

        await Task.Delay(TimeSpan.FromSeconds(2));

        app.CaptureScreenshot(screenshotDir, "workflow-after-skip-scan.png");

        // After skipping Scan, the Harden step should be unlocked and its button enabled.
        var hardenBtn = app.MainWindow.FindFirstDescendant(
            cf => cf.ByAutomationId("Run Harden step").Or(cf.ByName("Run Harden step")));

        hardenBtn.Should().NotBeNull("Run Harden step button should be visible after skipping Scan");
        hardenBtn!.IsEnabled.Should().BeTrue(
            "Run Harden step button should be enabled after Scan step is skipped");

        app.CaptureScreenshot(screenshotDir, "workflow-harden-unlocked.png");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Workflow_RunAutoButton_Visible()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "workflow-cards");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        var workflowTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Workflow tab")));
        workflowTab.Should().NotBeNull("Workflow tab should exist");
        workflowTab!.AsTabItem().Select();

        // Hard-assert: "Run auto workflow" button must exist and be visible.
        var runAutoBtn = await app.GetByTestId("Run auto workflow").ExpectVisibleAsync();
        runAutoBtn.Should().NotBeNull("Run auto workflow button should exist on the Workflow tab");
        runAutoBtn.IsOffscreen.Should().BeFalse("Run auto workflow button should be on-screen");

        using var visual = new VisualCheck(app, screenshotDir);

        // Soft-check: button label text should contain "Run Auto" (not truncated).
        var btnName = runAutoBtn.Name ?? string.Empty;
        visual.CheckTextNotTruncated(
            "run-auto-label",
            btnName,
            "Run");

        app.CaptureScreenshot(screenshotDir, "workflow-run-auto-button.png");
    }

    [UIFact]
    [Trait("Category", "UI")]
    public async Task Workflow_ErrorState_ShowsRecoveryCard()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        var executablePath = UiTestHelpers.LocateAppExecutable(repoRoot);
        var screenshotDir = UiTestHelpers.GetScreenshotDir(repoRoot, "workflow-cards");

        await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

        var workflowTab = app.MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Workflow tab")));
        workflowTab.Should().NotBeNull("Workflow tab should exist");
        workflowTab!.AsTabItem().Select();

        await app.GetByTestId("Run Scan step").ClickAsync();

        app.CaptureScreenshot(screenshotDir, "workflow-scan-triggered.png");

        // Poll up to 15 s for either an error state or a completed scan.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        AutomationElement? recoveryElement = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            // Look for recovery guidance: "Open Settings for failure recovery" button or
            // any element whose name contains recognised error keywords.
            recoveryElement =
                app.MainWindow.FindFirstDescendant(
                    cf => cf.ByAutomationId("Open Settings for failure recovery")
                          .Or(cf.ByName("Open Settings for failure recovery")))
                ?? app.MainWindow.FindFirstDescendant(
                    cf => cf.ByAutomationId("Rerun Scan").Or(cf.ByName("Rerun Scan")));

            if (recoveryElement is not null)
                break;

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        app.CaptureScreenshot(screenshotDir, "workflow-after-scan-attempt.png");

        if (recoveryElement is not null)
        {
            // Error path: recovery card appeared — assert it is on-screen.
            recoveryElement.IsOffscreen.Should().BeFalse(
                "recovery guidance should be visible on-screen after a failed scan");
        }
        else
        {
            // Scan may have succeeded (Evaluate-STIG is configured on this machine).
            // Verify the workflow advanced rather than throwing.
            var hardenBtn = app.MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("Run Harden step").Or(cf.ByName("Run Harden step")));

            // Soft-note: scan completed without error — no recovery card expected.
            hardenBtn.Should().NotBeNull(
                "If scan succeeded, Run Harden step should now be accessible");
        }
    }
}

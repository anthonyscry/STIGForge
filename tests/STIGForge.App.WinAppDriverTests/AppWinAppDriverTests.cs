using System.IO;
using FluentAssertions;
using STIGForge.UiDriver;

namespace STIGForge.App.WinAppDriverTests;

/// <summary>
/// Smoke tests for STIGForge using the WinAppDriver (Appium) backend.
/// Requires WinAppDriver.exe to be running before executing these tests.
/// Set UI_TESTS_ENABLED=true to enable; configure the service URL via WINAPPDRIVER_URL (default: http://127.0.0.1:4723/).
/// </summary>
public sealed class AppWinAppDriverTests
{
    private static (string executablePath, Uri serviceUrl, string screenshotRoot) GetTestContext()
    {
        var repoRoot = UiTestHelpers.LocateRepositoryRoot();
        return (
            UiTestHelpers.LocateAppExecutable(repoRoot),
            ResolveServiceUrl(),
            Path.Combine(repoRoot, ".artifacts", "ui-smoke", "winappdriver"));
    }

    [UIFact]
    [Trait("Category", "UI")]
    [Trait("Backend", "WinAppDriver")]
    public async Task MainWindow_ShowsHeaderButtons_ViaWinAppDriver()
    {
        var (executablePath, serviceUrl, screenshotRoot) = GetTestContext();

        await using var client = await WinAppDriverClient.LaunchAsync(executablePath, serviceUrl, TimeSpan.FromSeconds(60));

        await client.GetById("help-button").ExpectVisibleAsync();
        await client.GetById("about-button").ExpectVisibleAsync();
        await client.GetById("settings-button").ExpectVisibleAsync();

        var screenshotPath = client.CaptureScreenshot(screenshotRoot, "wad-smoke-header-buttons.png");
        File.Exists(screenshotPath).Should().BeTrue();
    }

    [UIFact]
    [Trait("Category", "UI")]
    [Trait("Backend", "WinAppDriver")]
    public async Task WorkflowTab_ShowsRunButtons_ViaWinAppDriver()
    {
        var (executablePath, serviceUrl, screenshotRoot) = GetTestContext();

        await using var client = await WinAppDriverClient.LaunchAsync(executablePath, serviceUrl, TimeSpan.FromSeconds(60));

        // Import Library tab (index 0) is selected by default. Press Right arrow to move to
        // the Workflow tab (index 1). Direct Click() on a non-selected WPF TabItem via
        // WinAppDriver does not reliably select it because its content is not yet in the
        // accessibility tree — keyboard navigation is the robust alternative.
        client.Driver.FindElementByAccessibilityId("import-tab").SendKeys("\uE014"); // Right arrow
        await Task.Delay(300); // allow tab content to enter accessibility tree

        await client.GetById("run-auto-workflow").ExpectVisibleAsync();
        await client.GetByName("Run Scan step").ExpectVisibleAsync();
        await client.GetByName("Run Harden step").ExpectVisibleAsync();
        await client.GetByName("Run Verify step").ExpectVisibleAsync();

        var screenshotPath = client.CaptureScreenshot(screenshotRoot, "wad-smoke-workflow-tab.png");
        File.Exists(screenshotPath).Should().BeTrue();
    }

    [UIFact]
    [Trait("Category", "UI")]
    [Trait("Backend", "WinAppDriver")]
    public async Task ImportTab_IsAccessible_ViaWinAppDriver()
    {
        var (executablePath, serviceUrl, screenshotRoot) = GetTestContext();

        await using var client = await WinAppDriverClient.LaunchAsync(executablePath, serviceUrl, TimeSpan.FromSeconds(60));

        await client.GetById("import-tab").ClickAsync();
        await client.GetByName("Run Import step").ExpectVisibleAsync();

        var screenshotPath = client.CaptureScreenshot(screenshotRoot, "wad-smoke-import-tab.png");
        File.Exists(screenshotPath).Should().BeTrue();
    }

    [UIFact]
    [Trait("Category", "UI")]
    [Trait("Backend", "WinAppDriver")]
    public async Task ResultsTab_IsAccessible_ViaWinAppDriver()
    {
        var (executablePath, serviceUrl, screenshotRoot) = GetTestContext();

        await using var client = await WinAppDriverClient.LaunchAsync(executablePath, serviceUrl, TimeSpan.FromSeconds(60));

        await client.GetByName("Results tab").ClickAsync();

        var screenshotPath = client.CaptureScreenshot(screenshotRoot, "wad-smoke-results-tab.png");
        File.Exists(screenshotPath).Should().BeTrue();
    }

    [UIFact]
    [Trait("Category", "UI")]
    [Trait("Backend", "WinAppDriver")]
    public async Task ComplianceSummaryTab_IsAccessible_ViaWinAppDriver()
    {
        var (executablePath, serviceUrl, screenshotRoot) = GetTestContext();

        await using var client = await WinAppDriverClient.LaunchAsync(executablePath, serviceUrl, TimeSpan.FromSeconds(60));

        await client.GetById("compliance-summary-tab").ClickAsync();

        var screenshotPath = client.CaptureScreenshot(screenshotRoot, "wad-smoke-compliance-tab.png");
        File.Exists(screenshotPath).Should().BeTrue();
    }

    private static Uri ResolveServiceUrl()
    {
        var envUrl = Environment.GetEnvironmentVariable("WINAPPDRIVER_URL");
        return string.IsNullOrWhiteSpace(envUrl)
            ? new Uri("http://127.0.0.1:4723/")
            : new Uri(envUrl);
    }
}

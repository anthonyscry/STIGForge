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
    [UIFact]
    [Trait("Category", "UI")]
    [Trait("Backend", "WinAppDriver")]
    public async Task MainWindow_ShowsHeaderButtons_ViaWinAppDriver()
    {
        var repoRoot = LocateRepositoryRoot();
        var executablePath = LocateAppExecutable(repoRoot);
        var serviceUrl = ResolveServiceUrl();
        var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "winappdriver");

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
        var repoRoot = LocateRepositoryRoot();
        var executablePath = LocateAppExecutable(repoRoot);
        var serviceUrl = ResolveServiceUrl();
        var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "winappdriver");

        await using var client = await WinAppDriverClient.LaunchAsync(executablePath, serviceUrl, TimeSpan.FromSeconds(60));

        await client.GetById("workflow-tab").ClickAsync();

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
        var repoRoot = LocateRepositoryRoot();
        var executablePath = LocateAppExecutable(repoRoot);
        var serviceUrl = ResolveServiceUrl();
        var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "winappdriver");

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
        var repoRoot = LocateRepositoryRoot();
        var executablePath = LocateAppExecutable(repoRoot);
        var serviceUrl = ResolveServiceUrl();
        var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "winappdriver");

        await using var client = await WinAppDriverClient.LaunchAsync(executablePath, serviceUrl, TimeSpan.FromSeconds(60));

        await client.GetById("results-tab").ClickAsync();

        var screenshotPath = client.CaptureScreenshot(screenshotRoot, "wad-smoke-results-tab.png");
        File.Exists(screenshotPath).Should().BeTrue();
    }

    [UIFact]
    [Trait("Category", "UI")]
    [Trait("Backend", "WinAppDriver")]
    public async Task ComplianceSummaryTab_IsAccessible_ViaWinAppDriver()
    {
        var repoRoot = LocateRepositoryRoot();
        var executablePath = LocateAppExecutable(repoRoot);
        var serviceUrl = ResolveServiceUrl();
        var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "winappdriver");

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

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test base directory.");
    }

    private static string LocateAppExecutable(string repoRoot)
    {
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", "STIGForge.App", "bin", "Debug", "net8.0-windows", "STIGForge.App.exe"),
            Path.Combine(repoRoot, "src", "STIGForge.App", "bin", "Release", "net8.0-windows", "STIGForge.App.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var binRoot = Path.Combine(repoRoot, "src", "STIGForge.App", "bin");
        if (Directory.Exists(binRoot))
        {
            var discovered = Directory.EnumerateFiles(binRoot, "STIGForge.App.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(discovered))
                return discovered!;
        }

        throw new FileNotFoundException("Could not locate STIGForge.App.exe. Build STIGForge.App before running UI tests.");
    }
}

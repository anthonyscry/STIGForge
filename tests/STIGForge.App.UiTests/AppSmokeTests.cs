using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentAssertions;
using STIGForge.UiDriver;

namespace STIGForge.App.UiTests;

public sealed class AppSmokeTests
{
  [Fact]
  [Trait("Category", "UI")]
  public async Task MainWindow_ShowsWorkflowControls_UsingUiDriver()
  {
    var repoRoot = LocateRepositoryRoot();
    var executablePath = LocateAppExecutable(repoRoot);
    var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "local");

    await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

    await app.GetByTestId("Help").ExpectVisibleAsync();
    await app.GetByTestId("Workflow").ClickAsync();
    await app.GetByTestId("Run Scan step").ExpectVisibleAsync();

    var screenshotPath = app.CaptureScreenshot(screenshotRoot, "app-smoke-main-window.png");
    File.Exists(screenshotPath).Should().BeTrue();
  }

  [Fact]
  [Trait("Category", "UI")]
  public async Task MainWindow_ShowsHeaderButtons()
  {
    var repoRoot = LocateRepositoryRoot();
    var executablePath = LocateAppExecutable(repoRoot);
    var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "local");

    await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

    await app.GetByTestId("Help").ExpectVisibleAsync();
    await app.GetByTestId("About").ExpectVisibleAsync();
    await app.GetByTestId("Settings").ExpectVisibleAsync();

    var screenshotPath = app.CaptureScreenshot(screenshotRoot, "app-smoke-header-buttons.png");
    File.Exists(screenshotPath).Should().BeTrue();
  }

  [Fact]
  [Trait("Category", "UI")]
  public async Task ImportTab_ShowsImportControls()
  {
    var repoRoot = LocateRepositoryRoot();
    var executablePath = LocateAppExecutable(repoRoot);
    var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "local");

    await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

    var importTab = app.MainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Import Library tab")));
    importTab.Should().NotBeNull("Import Library tab should exist");
    importTab!.AsTabItem().Select();

    await app.GetByTestId("Run Import step").ExpectVisibleAsync();

    var screenshotPath = app.CaptureScreenshot(screenshotRoot, "app-smoke-import-tab.png");
    File.Exists(screenshotPath).Should().BeTrue();
  }

  [Fact]
  [Trait("Category", "UI")]
  public async Task WorkflowTab_ShowsAllStepCards()
  {
    var repoRoot = LocateRepositoryRoot();
    var executablePath = LocateAppExecutable(repoRoot);
    var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "local");

    await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

    await app.GetByTestId("Workflow").ClickAsync();

    await app.GetByTestId("Run auto workflow").ExpectVisibleAsync();
    await app.GetByTestId("Run Scan step").ExpectVisibleAsync();
    await app.GetByTestId("Run Harden step").ExpectVisibleAsync();
    await app.GetByTestId("Run Verify step").ExpectVisibleAsync();

    var screenshotPath = app.CaptureScreenshot(screenshotRoot, "app-smoke-workflow-steps.png");
    File.Exists(screenshotPath).Should().BeTrue();
  }

  [Fact]
  [Trait("Category", "UI")]
  public async Task ResultsTab_IsAccessible()
  {
    var repoRoot = LocateRepositoryRoot();
    var executablePath = LocateAppExecutable(repoRoot);
    var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "local");

    await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

    var resultsTab = app.MainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Results tab")));
    resultsTab.Should().NotBeNull("Results tab should exist");
    resultsTab!.AsTabItem().Select();

    var screenshotPath = app.CaptureScreenshot(screenshotRoot, "app-smoke-results-tab.png");
    File.Exists(screenshotPath).Should().BeTrue();
  }

  [Fact]
  [Trait("Category", "UI")]
  public async Task ComplianceSummaryTab_IsAccessible()
  {
    var repoRoot = LocateRepositoryRoot();
    var executablePath = LocateAppExecutable(repoRoot);
    var screenshotRoot = Path.Combine(repoRoot, ".artifacts", "ui-smoke", "local");

    await using var app = await UiAppDriver.LaunchAsync(executablePath, TimeSpan.FromSeconds(45));

    var complianceTab = app.MainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName("Compliance Summary tab")));
    complianceTab.Should().NotBeNull("Compliance Summary tab should exist");
    complianceTab!.AsTabItem().Select();

    var screenshotPath = app.CaptureScreenshot(screenshotRoot, "app-smoke-compliance-tab.png");
    File.Exists(screenshotPath).Should().BeTrue();
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
      Path.Combine(repoRoot, "src", "STIGForge.App", "bin", "Release", "net8.0-windows", "STIGForge.App.exe")
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

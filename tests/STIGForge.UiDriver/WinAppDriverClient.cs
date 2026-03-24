using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;

namespace STIGForge.UiDriver;

/// <summary>
/// WinAppDriver / Appium backend for E2E testing.
/// Requires WinAppDriver.exe to be running on the target machine (default port 4723).
/// </summary>
public sealed class WinAppDriverClient : IAsyncDisposable
{
    private readonly WindowsDriver _driver;

    private WinAppDriverClient(WindowsDriver driver)
    {
        _driver = driver;
    }

    public WindowsDriver Driver => _driver;

    /// <summary>
    /// Launches the application via WinAppDriver and returns a connected client.
    /// </summary>
    /// <param name="executablePath">Full path to the application executable.</param>
    /// <param name="winAppDriverUrl">WinAppDriver service URL (default: http://127.0.0.1:4723).</param>
    /// <param name="timeout">Session creation timeout.</param>
    public static Task<WinAppDriverClient> LaunchAsync(
        string executablePath,
        Uri? winAppDriverUrl = null,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path must be provided.", nameof(executablePath));

        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Unable to find app executable for WinAppDriver test run.", executablePath);

        var serviceUrl = winAppDriverUrl ?? new Uri("http://127.0.0.1:4723/");

        var options = new AppiumOptions();
        options.AddAdditionalAppiumOption("app", executablePath);
        options.PlatformName = "Windows";
        options.AutomationName = "Windows";

        var commandTimeout = timeout ?? TimeSpan.FromSeconds(60);
        var driver = new WindowsDriver(serviceUrl, options, commandTimeout);

        return Task.FromResult(new WinAppDriverClient(driver));
    }

    /// <summary>
    /// Returns an element locator using the given accessibility ID (AutomationId).
    /// </summary>
    public WinAppDriverLocator GetById(string automationId)
        => new(_driver, WinAppDriverLocatorStrategy.AccessibilityId, automationId);

    /// <summary>
    /// Returns an element locator using the given XPath expression.
    /// </summary>
    public WinAppDriverLocator GetByXPath(string xpath)
        => new(_driver, WinAppDriverLocatorStrategy.XPath, xpath);

    /// <summary>
    /// Returns an element locator using the element's accessible name.
    /// </summary>
    public WinAppDriverLocator GetByName(string name)
        => new(_driver, WinAppDriverLocatorStrategy.Name, name);

    /// <summary>
    /// Takes a screenshot and saves it to <paramref name="outputRoot"/>/<paramref name="fileName"/>.
    /// </summary>
    public string CaptureScreenshot(string outputRoot, string fileName)
    {
        Directory.CreateDirectory(outputRoot);
        var path = Path.Combine(outputRoot, fileName);
        _driver.GetScreenshot().SaveAsFile(path);
        return path;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _driver.Quit();
        }
        catch
        {
        }

        _driver.Dispose();
        return ValueTask.CompletedTask;
    }
}

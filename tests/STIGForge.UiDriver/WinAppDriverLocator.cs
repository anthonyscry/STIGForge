using System.Diagnostics;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;

namespace STIGForge.UiDriver;

public enum WinAppDriverLocatorStrategy
{
    AccessibilityId,
    XPath,
    Name,
}

/// <summary>
/// Fluent element locator for the WinAppDriver backend.
/// </summary>
public sealed class WinAppDriverLocator
{
    private readonly WindowsDriver<WindowsElement> _driver;
    private readonly WinAppDriverLocatorStrategy _strategy;
    private readonly string _selector;

    internal WinAppDriverLocator(WindowsDriver<WindowsElement> driver, WinAppDriverLocatorStrategy strategy, string selector)
    {
        _driver = driver;
        _strategy = strategy;
        _selector = selector;
    }

    public Task<WindowsElement> ExpectVisibleAsync(TimeSpan? timeout = null)
    {
        var element = WaitForElement(timeout ?? TimeSpan.FromSeconds(30));
        if (!element.Displayed)
            throw new InvalidOperationException($"Element '{_selector}' was found but is not displayed.");

        return Task.FromResult(element);
    }

    public async Task ClickAsync(TimeSpan? timeout = null)
    {
        var element = await ExpectVisibleAsync(timeout).ConfigureAwait(false);
        if (!element.Enabled)
            throw new InvalidOperationException($"Element '{_selector}' is disabled.");

        element.Click();
    }

    public async Task FillAsync(string text, TimeSpan? timeout = null)
    {
        var element = await ExpectVisibleAsync(timeout).ConfigureAwait(false);
        if (!element.Enabled)
            throw new InvalidOperationException($"Element '{_selector}' is disabled.");

        element.Clear();
        element.SendKeys(text);
    }

    private WindowsElement WaitForElement(TimeSpan timeout)
    {
        var by = _strategy switch
        {
            WinAppDriverLocatorStrategy.AccessibilityId => MobileBy.AccessibilityId(_selector),
            WinAppDriverLocatorStrategy.XPath => By.XPath(_selector),
            WinAppDriverLocatorStrategy.Name => By.Name(_selector),
            _ => throw new ArgumentOutOfRangeException(nameof(_strategy)),
        };

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                var elements = _driver.FindElements(by);
                if (elements.Count > 0)
                    return elements[0];
            }
            catch (WebDriverException)
            {
                // session not ready yet; keep polling
            }

            Thread.Sleep(200);
        }

        throw new TimeoutException($"Timed out waiting for WinAppDriver element '{_selector}' ({_strategy}).");
    }
}

using System.Diagnostics;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace STIGForge.UiDriver;

public sealed class UiLocator
{
  private readonly Window _window;
  private readonly string _testId;
  private readonly bool _strictAutomationId;

  internal UiLocator(Window window, string testId, bool strictAutomationId = false)
  {
    _window = window;
    _testId = testId;
    _strictAutomationId = strictAutomationId;
  }

  public Task<AutomationElement> ExpectVisibleAsync(TimeSpan? timeout = null)
  {
    var element = WaitForElement(timeout ?? TimeSpan.FromSeconds(10));
    if (element.IsOffscreen)
      throw new InvalidOperationException($"Element '{_testId}' was found but is offscreen.");

    return Task.FromResult(element);
  }

  public async Task ClickAsync(TimeSpan? timeout = null)
  {
    var element = await ExpectVisibleAsync(timeout).ConfigureAwait(false);
    if (!element.IsEnabled)
      throw new InvalidOperationException($"Element '{_testId}' is disabled.");

    element.Click();
  }

  public async Task FillAsync(string text, TimeSpan? timeout = null)
  {
    var element = await ExpectVisibleAsync(timeout).ConfigureAwait(false);
    if (!element.IsEnabled)
      throw new InvalidOperationException($"Element '{_testId}' is disabled.");

    var textBox = element.AsTextBox();
    if (textBox is not null)
    {
      textBox.Text = text;
      return;
    }

    element.Focus();
    Keyboard.Type(text);
  }

  private AutomationElement WaitForElement(TimeSpan timeout)
  {
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < timeout)
    {
      AutomationElement? match;
      if (_strictAutomationId)
      {
        match = _window.FindFirstDescendant(cf => cf.ByAutomationId(_testId));
      }
      else
      {
        match = _window.FindFirstDescendant(cf => cf.ByAutomationId(_testId))
                ?? _window.FindFirstDescendant(cf => cf.ByName(_testId));
      }

      if (match is not null)
        return match;

      Thread.Sleep(100);
    }

    throw new TimeoutException($"Timed out waiting for UI element '{_testId}'.");
  }
}

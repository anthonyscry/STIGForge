using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace STIGForge.UiDriver;

public sealed class UiAppDriver : IAsyncDisposable
{
  private readonly Application _application;
  private readonly UIA3Automation _automation;

  private UiAppDriver(Application application, UIA3Automation automation, Window mainWindow)
  {
    _application = application;
    _automation = automation;
    MainWindow = mainWindow;
  }

  public Window MainWindow { get; }

  public static Task<UiAppDriver> LaunchAsync(string executablePath, TimeSpan? timeout = null)
  {
    if (string.IsNullOrWhiteSpace(executablePath))
      throw new ArgumentException("Executable path must be provided.", nameof(executablePath));

    if (!File.Exists(executablePath))
      throw new FileNotFoundException("Unable to find app executable for UI test run.", executablePath);

    var app = Application.Launch(executablePath);
    var automation = new UIA3Automation();
    var window = app.GetMainWindow(automation, timeout ?? TimeSpan.FromSeconds(30));

    if (window is null)
    {
      automation.Dispose();
      app.Dispose();
      throw new InvalidOperationException("Main window did not appear before timeout.");
    }

    window.SetForeground();
    return Task.FromResult(new UiAppDriver(app, automation, window));
  }

  public UiLocator GetByTestId(string testId)
  {
    return new UiLocator(MainWindow, testId);
  }

  public string CaptureScreenshot(string outputRoot, string fileName)
  {
    Directory.CreateDirectory(outputRoot);
    var path = Path.Combine(outputRoot, fileName);
    MainWindow.CaptureToFile(path);
    return path;
  }

  public ValueTask DisposeAsync()
  {
    try
    {
      if (!_application.HasExited)
        _application.Close();
    }
    catch
    {
    }

    _automation.Dispose();
    _application.Dispose();
    return ValueTask.CompletedTask;
  }
}

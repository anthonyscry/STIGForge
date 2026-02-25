using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace STIGForge.App.Views;

public partial class SettingsWindow : Window
{
  private const int DwmwaCaptionColor = 35;
  private const int DwmwaTextColor = 36;

  public SettingsWindow()
  {
    InitializeComponent();
    Loaded += (_, _) => ApplyTitleBarColors();
  }

  private void Cancel_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = false;
    Close();
  }

  private void Save_Click(object sender, RoutedEventArgs e)
  {
    if (DataContext is WorkflowViewModel vm && vm.SaveSettingsCommand.CanExecute(null))
    {
      vm.SaveSettingsCommand.Execute(null);
      DialogResult = true;
      Close();
      return;
    }

    DialogResult = false;
    Close();
  }

  private void ApplyTitleBarColors()
  {
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      return;

    var hwnd = new WindowInteropHelper(this).Handle;
    if (hwnd == IntPtr.Zero)
      return;

    if (TryResolveColor("WindowBackgroundBrush", out var captionColor))
    {
      var captionColorRef = ToColorRef(captionColor);
      _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColorRef, sizeof(int));
    }

    if (TryResolveColor("AccentBrush", out var textColor))
    {
      var textColorRef = ToColorRef(textColor);
      _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColorRef, sizeof(int));
    }
  }

  private static bool TryResolveColor(string resourceKey, out Color color)
  {
    if (Application.Current?.Resources[resourceKey] is SolidColorBrush brush)
    {
      color = brush.Color;
      return true;
    }

    color = default;
    return false;
  }

  private static int ToColorRef(Color color)
  {
    return color.R | (color.G << 8) | (color.B << 16);
  }

  [DllImport("dwmapi.dll")]
  private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}

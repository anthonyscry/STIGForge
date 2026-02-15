using System.Runtime.InteropServices;
using System.Windows;

namespace STIGForge.App.Views;

public partial class AboutDialog : Window
{
  public AboutDialog(string dataRoot, int packCount, int profileCount, int overlayCount)
  {
    InitializeComponent();
    DataContext = new
    {
      RuntimeVersion = RuntimeInformation.FrameworkDescription,
      DataRoot = dataRoot,
      PackCount = packCount.ToString(),
      ProfileCount = profileCount.ToString(),
      OverlayCount = overlayCount.ToString()
    };
    SourceInitialized += (_, _) => MainViewModel.SetDarkTitleBar(this, IsDarkThemeActive());
  }

  private static bool IsDarkThemeActive()
  {
    var app = Application.Current;
    if (app == null)
      return true;

    foreach (var dict in app.Resources.MergedDictionaries)
    {
      if (dict.Source?.OriginalString.IndexOf("DarkTheme", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    return false;
  }

  private void Close_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }
}

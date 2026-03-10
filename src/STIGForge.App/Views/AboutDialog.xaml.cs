using System.Reflection;
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
      AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
      RuntimeVersion = RuntimeInformation.FrameworkDescription,
      DataRoot = dataRoot,
      PackCount = packCount.ToString(),
      ProfileCount = profileCount.ToString(),
      OverlayCount = overlayCount.ToString()
    };
  }

  private void Close_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }
}

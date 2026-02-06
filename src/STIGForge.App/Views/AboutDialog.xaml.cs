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
  }

  private void Close_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }
}

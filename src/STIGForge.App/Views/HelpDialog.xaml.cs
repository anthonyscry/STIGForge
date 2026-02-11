using System.Windows;

namespace STIGForge.App.Views;

public partial class HelpDialog : Window
{
  public HelpDialog()
  {
    InitializeComponent();
  }

  private void Close_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }
}

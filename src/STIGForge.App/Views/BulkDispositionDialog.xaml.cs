using System.Windows;

namespace STIGForge.App.Views;

public partial class BulkDispositionDialog : Window
{
  public BulkDispositionDialog()
  {
    InitializeComponent();
  }

  private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

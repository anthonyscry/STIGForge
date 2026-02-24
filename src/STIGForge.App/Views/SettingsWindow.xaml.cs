using System.Windows;

namespace STIGForge.App.Views;

public partial class SettingsWindow : Window
{
  public SettingsWindow()
  {
    InitializeComponent();
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
}

using System.Windows;

namespace STIGForge.App;

public partial class MainWindow : Window
{
  public MainWindow(MainViewModel vm)
  {
    InitializeComponent();
    DataContext = vm;
    SourceInitialized += (_, _) => MainViewModel.SetDarkTitleBar(this, vm.IsDarkTheme);
  }
}

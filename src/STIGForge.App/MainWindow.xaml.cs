using System;
using System.Windows;

namespace STIGForge.App;

public partial class MainWindow : Window
{
  private MainViewModel? _viewModel;

  public MainWindow()
  {
    InitializeComponent();
    SourceInitialized += OnSourceInitialized;
  }

  public MainWindow(MainViewModel vm)
    : this()
  {
    BindViewModel(vm);
  }

  public void BindViewModel(MainViewModel vm)
  {
    _viewModel = vm;
    DataContext = vm;
    MainViewModel.SetDarkTitleBar(this, vm.IsDarkTheme);
  }

  private void OnSourceInitialized(object? sender, EventArgs e)
  {
    if (_viewModel != null)
      MainViewModel.SetDarkTitleBar(this, _viewModel.IsDarkTheme);
  }
}

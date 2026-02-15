using System.Windows;
using STIGForge.App.ViewModels;

namespace STIGForge.App.Views;

public partial class DiffViewer : Window
{
  public DiffViewer(DiffViewerViewModel viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
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
}

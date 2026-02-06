using System.Windows;
using STIGForge.App.ViewModels;

namespace STIGForge.App.Views;

public partial class DiffViewer : Window
{
  public DiffViewer(DiffViewerViewModel viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
  }
}

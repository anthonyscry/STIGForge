using System.Windows;
using STIGForge.App.ViewModels;

namespace STIGForge.App.Views;

public partial class RebaseWizard : Window
{
  public RebaseWizard(RebaseWizardViewModel viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
    
    viewModel.CloseRequested += () => Close();
  }
}

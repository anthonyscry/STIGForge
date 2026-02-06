using System.Windows;
using STIGForge.App.ViewModels;

namespace STIGForge.App.Views;

public partial class ManualCheckWizard : Window
{
  public ManualCheckWizard(ManualCheckWizardViewModel viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
  }
}

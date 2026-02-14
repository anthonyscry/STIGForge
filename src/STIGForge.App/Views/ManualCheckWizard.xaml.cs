using System.Windows;
using STIGForge.App.ViewModels;

namespace STIGForge.App.Views;

public partial class ManualCheckWizard : Window
{
  public ManualCheckWizard(ManualCheckWizardViewModel viewModel)
  {
    // Inherit app-level theme dictionaries so DynamicResource resolves
    foreach (var dict in Application.Current.Resources.MergedDictionaries)
      Resources.MergedDictionaries.Add(dict);

    InitializeComponent();
    DataContext = viewModel;
  }
}

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using STIGForge.Core.Models;

namespace STIGForge.App.Views;

public partial class PackComparisonDialog : Window
{
  public string BaselinePackId { get; private set; } = string.Empty;
  public string TargetPackId { get; private set; } = string.Empty;

  public PackComparisonDialog(List<ContentPack> availablePacks)
  {
    InitializeComponent();
    DataContext = new { AvailablePacks = availablePacks };
    SourceInitialized += (_, _) => MainViewModel.SetDarkTitleBar(this, IsDarkThemeActive());
  }

  private static bool IsDarkThemeActive()
  {
    var app = Application.Current;
    if (app == null)
      return true;

    return app.Resources.MergedDictionaries
      .Any(d => d.Source?.OriginalString.IndexOf("DarkTheme", StringComparison.OrdinalIgnoreCase) >= 0);
  }

  private void Compare_Click(object sender, RoutedEventArgs e)
  {
    var baselinePack = BaselineListBox.SelectedItem as ContentPack;
    var targetPack = TargetListBox.SelectedItem as ContentPack;

    if (baselinePack == null || targetPack == null)
    {
      MessageBox.Show("Please select both baseline and target packs.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    if (baselinePack.PackId == targetPack.PackId)
    {
      MessageBox.Show("Please select different packs to compare.", "Invalid Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    BaselinePackId = baselinePack.PackId;
    TargetPackId = targetPack.PackId;
    DialogResult = true;
    Close();
  }

  private void Cancel_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = false;
    Close();
  }
}

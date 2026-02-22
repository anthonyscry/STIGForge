using System.Windows;
using System.Windows.Controls;

namespace STIGForge.App.Views;

public partial class BreakGlassDialog : Window
{
  public string Reason { get; private set; } = string.Empty;
  public string BypassDescription { get; }
  public bool Confirmed { get; private set; }

  public BreakGlassDialog(string bypassDescription)
  {
    InitializeComponent();
    BypassDescription = bypassDescription;
    BypassDescriptionText.Text = bypassDescription;
    SourceInitialized += (_, _) => MainViewModel.SetDarkTitleBar(this, IsDarkThemeActive());
  }

  private void ReasonTextBox_TextChanged(object sender, TextChangedEventArgs e)
  {
    var text = ReasonTextBox.Text?.Trim() ?? string.Empty;
    OverrideButton.IsEnabled = text.Length >= 8;
  }

  private void Override_Click(object sender, RoutedEventArgs e)
  {
    Reason = ReasonTextBox.Text?.Trim() ?? string.Empty;
    Confirmed = true;
    DialogResult = true;
    Close();
  }

  private void Cancel_Click(object sender, RoutedEventArgs e)
  {
    Confirmed = false;
    DialogResult = false;
    Close();
  }

  private static bool IsDarkThemeActive()
  {
    var app = Application.Current;
    if (app == null) return true;
    foreach (var dict in app.Resources.MergedDictionaries)
    {
      if (dict.Source?.OriginalString.IndexOf("DarkTheme", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }
    return false;
  }
}

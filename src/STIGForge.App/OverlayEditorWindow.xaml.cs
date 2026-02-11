using System.Windows;

namespace STIGForge.App;

public partial class OverlayEditorWindow : Window
{
  public OverlayEditorWindow()
  {
    // Inherit app-level theme dictionaries so DynamicResource resolves
    foreach (var dict in Application.Current.Resources.MergedDictionaries)
      Resources.MergedDictionaries.Add(dict);

    InitializeComponent();
    DataContext = ((App)Application.Current).Services.GetService(typeof(OverlayEditorViewModel));
  }
}

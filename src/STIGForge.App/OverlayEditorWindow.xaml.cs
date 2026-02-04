using System.Windows;

namespace STIGForge.App;

public partial class OverlayEditorWindow : Window
{
  public OverlayEditorWindow()
  {
    InitializeComponent();
    DataContext = ((App)Application.Current).Services.GetService(typeof(OverlayEditorViewModel));
  }
}

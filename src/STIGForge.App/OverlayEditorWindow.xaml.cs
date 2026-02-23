using System.Windows;

namespace STIGForge.App;

public partial class OverlayEditorWindow : Window
{
  private readonly OverlayEditorViewModel _viewModel;

  public OverlayEditorWindow() : this(Array.Empty<string>())
  {
  }

  public OverlayEditorWindow(IReadOnlyList<string> packIds)
  {
    // Inherit app-level theme dictionaries so DynamicResource resolves
    foreach (var dict in Application.Current.Resources.MergedDictionaries)
      Resources.MergedDictionaries.Add(dict);

    InitializeComponent();
    _viewModel = (OverlayEditorViewModel)((App)Application.Current).Services.GetService(typeof(OverlayEditorViewModel))!;
    DataContext = _viewModel;

    // Load available rules from the specified packs
    if (packIds.Count > 0)
    {
      Loaded += async (s, e) => await _viewModel.LoadAvailableRulesAsync(packIds, System.Threading.CancellationToken.None);
    }
  }
}

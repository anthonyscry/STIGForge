using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace STIGForge.App.Views;

public partial class ImportView : UserControl
{
    public static readonly DependencyProperty MissionJsonPathProperty =
        DependencyProperty.Register(
            nameof(MissionJsonPath),
            typeof(string),
            typeof(ImportView),
            new PropertyMetadata(string.Empty));

    public string MissionJsonPath
    {
        get => (string)GetValue(MissionJsonPathProperty);
        set => SetValue(MissionJsonPathProperty, value);
    }

    public ImportView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Bind MissionJsonPath to DataContext.MissionJsonPath if available
        if (DataContext != null)
        {
            var binding = new Binding("MissionJsonPath")
            {
                Source = DataContext,
                Mode = BindingMode.TwoWay
            };
            SetBinding(MissionJsonPathProperty, binding);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        BindingOperations.ClearBinding(this, MissionJsonPathProperty);
    }
}

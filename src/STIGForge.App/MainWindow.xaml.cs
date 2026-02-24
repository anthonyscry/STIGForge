using System.Windows;

namespace STIGForge.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new WorkflowViewModel();
    }
}

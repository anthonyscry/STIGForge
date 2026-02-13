using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace STIGForge.App.Views;

public partial class ManualView : UserControl
{
    public ManualView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;

        RestoreColumnWidths(ManualReviewGrid, vm.ManualReviewColumnWidths);
        RestoreColumnWidths(FullReviewGrid, vm.FullReviewColumnWidths);

        ManualReviewGrid.MouseLeftButtonUp += (s, _) => SaveGridWidths();
        FullReviewGrid.MouseLeftButtonUp += (s, _) => SaveGridWidths();
    }

    private void SaveGridWidths()
    {
        var vm = GetViewModel();
        if (vm == null) return;
        vm.ManualReviewColumnWidths = ManualReviewGrid.Columns.Select(c => c.ActualWidth).ToList();
        vm.FullReviewColumnWidths = FullReviewGrid.Columns.Select(c => c.ActualWidth).ToList();
    }

    private static void RestoreColumnWidths(DataGrid grid, List<double>? widths)
    {
        if (widths == null || widths.Count != grid.Columns.Count) return;
        for (int i = 0; i < widths.Count; i++)
            grid.Columns[i].Width = new DataGridLength(widths[i]);
    }

    private MainViewModel? GetViewModel() => DataContext as MainViewModel;
}

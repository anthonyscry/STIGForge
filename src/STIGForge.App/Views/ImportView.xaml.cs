using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using STIGForge.App;
namespace STIGForge.App.Views;
public partial class ImportView : UserControl
{
    private string _lastSortColumn = "";
    private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;

    public ImportView() { InitializeComponent(); }

    private void ContentLibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SelectedLibraryItems.Clear();
            foreach (var item in ContentLibraryList.SelectedItems.OfType<MainViewModel.ImportedLibraryItem>())
                vm.SelectedLibraryItems.Add(item);
        }
    }

    private void SelectAllLibraryItems_Click(object sender, RoutedEventArgs e)
    {
        ContentLibraryList.SelectAll();
    }

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header || header.Column == null)
            return;

        var binding = header.Column.DisplayMemberBinding as Binding;
        var sortBy = binding?.Path?.Path;
        if (string.IsNullOrEmpty(sortBy)) return;

        var direction = sortBy == _lastSortColumn && _lastSortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        _lastSortColumn = sortBy;
        _lastSortDirection = direction;

        var view = CollectionViewSource.GetDefaultView(ContentLibraryList.ItemsSource);
        if (view == null) return;

        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(sortBy, direction));
        view.Refresh();
    }
}

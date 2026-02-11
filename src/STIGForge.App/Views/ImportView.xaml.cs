using System.Linq;
using System.Windows;
using System.Windows.Controls;
using STIGForge.App;
namespace STIGForge.App.Views;
public partial class ImportView : UserControl
{
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
}

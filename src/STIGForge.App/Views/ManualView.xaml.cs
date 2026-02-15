using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using STIGForge.App;

namespace STIGForge.App.Views;

public partial class ManualView : UserControl
{
    private string _lastSortProperty = string.Empty;
    private ListSortDirection _lastDirection = ListSortDirection.Ascending;

    public ManualView() { InitializeComponent(); }

    private void ManualGridHeader_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (e.OriginalSource is not GridViewColumnHeader header || header.Column == null)
            return;

        var headerText = (header.Column.Header ?? header.Content)?.ToString() ?? string.Empty;
        var sortProperty = headerText switch
        {
            "Rule" => nameof(MainViewModel.ManualControlItem.RuleId),
            "Title" => nameof(MainViewModel.ManualControlItem.Title),
            "CAT" => nameof(MainViewModel.ManualControlItem.CatSortOrder),
            "STIG Group" => nameof(MainViewModel.ManualControlItem.StigGroup),
            "Stat" => nameof(MainViewModel.ManualControlItem.Status),
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sortProperty))
            return;

        var direction = string.Equals(_lastSortProperty, sortProperty, StringComparison.Ordinal)
            && _lastDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

        var view = vm.ManualControlsView;
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(sortProperty, direction));
            if (!string.Equals(sortProperty, nameof(MainViewModel.ManualControlItem.RuleId), StringComparison.Ordinal))
            {
                view.SortDescriptions.Add(new SortDescription(nameof(MainViewModel.ManualControlItem.RuleId), ListSortDirection.Ascending));
            }
        }

        _lastSortProperty = sortProperty;
        _lastDirection = direction;
    }
}

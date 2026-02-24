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
    private MainViewModel? _boundViewModel;

    public static readonly DependencyProperty MissionJsonPathProperty = DependencyProperty.Register(
        nameof(MissionJsonPath),
        typeof(string),
        typeof(ImportView),
        new PropertyMetadata(string.Empty));

    public string MissionJsonPath
    {
        get => (string)GetValue(MissionJsonPathProperty);
        private set => SetValue(MissionJsonPathProperty, value);
    }

    public ImportView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BindToViewModel(DataContext as MainViewModel);
        UpdateMissionJsonPathBindingSurface();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnbindFromViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        BindToViewModel(e.NewValue as MainViewModel);
        UpdateMissionJsonPathBindingSurface();
    }

    private void BindToViewModel(MainViewModel? viewModel)
    {
        if (ReferenceEquals(_boundViewModel, viewModel))
            return;

        UnbindFromViewModel();
        _boundViewModel = viewModel;
        if (_boundViewModel != null)
            _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void UnbindFromViewModel()
    {
        var current = _boundViewModel;
        if (current == null)
            return;

        current.PropertyChanged -= OnViewModelPropertyChanged;
        _boundViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MainViewModel.MissionJsonPath), StringComparison.Ordinal))
            return;

        UpdateMissionJsonPathBindingSurface();
    }

    private void UpdateMissionJsonPathBindingSurface()
    {
        MissionJsonPath = _boundViewModel?.MissionJsonPath ?? string.Empty;
    }

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

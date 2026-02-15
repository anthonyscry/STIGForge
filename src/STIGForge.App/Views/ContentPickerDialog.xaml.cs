using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace STIGForge.App.Views;

public partial class ContentPickerDialog : Window
{
  public List<ContentPickerItem> Items { get; }
  public List<string> SelectedPackIds { get; private set; } = new();
  public HashSet<string> RecommendedPackIds { get; }
  private readonly List<CheckBox> _checkBoxes = new();
  private readonly Func<IReadOnlyCollection<string>, string>? _statusProvider;
  private readonly IReadOnlyList<string> _warningLines;

  private static readonly (string Key, string Label)[] SectionOrder = new[]
  {
    ("STIG", "STIG Content  —  controls and rules"),
    ("SCAP", "SCAP Benchmarks  —  XCCDF + OVAL"),
    ("GPO", "GPO / LGPO  —  local policy packages"),
    ("ADMX", "ADMX Templates  —  policy templates"),
  };

  public ContentPickerDialog(
    List<ContentPickerItem> items,
    HashSet<string>? recommendedIds = null,
    Func<IReadOnlyCollection<string>, string>? statusProvider = null,
    IReadOnlyList<string>? warningLines = null)
  {
    InitializeComponent();
    Items = items;
    RecommendedPackIds = recommendedIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    _statusProvider = statusProvider;
    _warningLines = warningLines ?? Array.Empty<string>();
    BuildGroupedUI();
    UpdateCount();
    SelectRecommendedBtn.Visibility = RecommendedPackIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
  }

  private void BuildGroupedUI()
  {
    var grouped = Items
      .GroupBy(i => i.Format)
      .ToDictionary(g => g.Key, g => g.ToList());

    foreach (var (key, label) in SectionOrder)
    {
      if (!grouped.TryGetValue(key, out var group) || group.Count == 0)
        continue;

      AddSection(label, group);
    }

    var otherKeys = grouped.Keys
      .Where(k => !SectionOrder.Any(s => s.Key == k))
      .OrderBy(k => k)
      .ToList();

    foreach (var key in otherKeys)
    {
      AddSection(key + " Content", grouped[key]);
    }
  }

  private void AddSection(string header, List<ContentPickerItem> items)
  {
    var headerBlock = new TextBlock
    {
      Text = header,
      FontSize = 13,
      FontWeight = FontWeights.SemiBold,
      Margin = new Thickness(0, _checkBoxes.Count > 0 ? 14 : 0, 0, 6),
      Foreground = (Brush)FindResource("AccentBrush")
    };
    GroupedPanel.Children.Add(headerBlock);

    var border = new Border
    {
      BorderBrush = (Brush)FindResource("BorderBrush"),
      BorderThickness = new Thickness(1),
      CornerRadius = new CornerRadius(6),
      Background = (Brush)FindResource("SurfaceBrush"),
      Padding = new Thickness(8),
      Margin = new Thickness(0, 0, 0, 4)
    };

    var stack = new StackPanel();

    foreach (var item in items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
    {
      var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

      var cb = new CheckBox
      {
        IsChecked = item.IsSelected,
        VerticalAlignment = VerticalAlignment.Center,
        Tag = item,
        IsEnabled = !item.IsLocked,
        ToolTip = item.IsLocked && !string.IsNullOrWhiteSpace(item.LockReason)
          ? item.LockReason
          : null
      };
      cb.Checked += (_, _) => { item.IsSelected = true; UpdateCount(); };
      cb.Unchecked += (_, _) => { item.IsSelected = false; UpdateCount(); };
      _checkBoxes.Add(cb);

      var name = new TextBlock
      {
        Text = item.Name,
        Width = 340,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 0, 0),
        TextTrimming = TextTrimming.CharacterEllipsis
      };

      var imported = new TextBlock
      {
        Text = item.ImportedAtLabel,
        Width = 130,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = (Brush)FindResource("TextMutedBrush")
      };

      var source = new TextBlock
      {
        Text = item.SourceLabel,
        Width = 120,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = (Brush)FindResource("TextMutedBrush")
      };

      row.Children.Add(cb);
      row.Children.Add(name);
      row.Children.Add(imported);
      row.Children.Add(source);

      if (item.IsLocked)
      {
        var auto = new TextBlock
        {
          Text = "Auto-included",
          Width = 90,
          VerticalAlignment = VerticalAlignment.Center,
          Foreground = (Brush)FindResource("TextMutedBrush")
        };
        row.Children.Add(auto);
      }

      stack.Children.Add(row);
    }

    border.Child = stack;
    GroupedPanel.Children.Add(border);
  }

  private void SelectAll_Click(object sender, RoutedEventArgs e)
  {
    foreach (var item in Items.Where(i => !i.IsLocked)) item.IsSelected = true;
    foreach (var cb in _checkBoxes.Where(c => c.IsEnabled)) cb.IsChecked = true;
    UpdateCount();
  }

  private void SelectNone_Click(object sender, RoutedEventArgs e)
  {
    foreach (var item in Items.Where(i => !i.IsLocked)) item.IsSelected = false;
    foreach (var cb in _checkBoxes.Where(c => c.IsEnabled)) cb.IsChecked = false;
    UpdateCount();
  }

  private void SelectRecommended_Click(object sender, RoutedEventArgs e)
  {
    foreach (var cb in _checkBoxes)
    {
      if (cb.Tag is ContentPickerItem item)
      {
        if (item.IsLocked) continue;
        var match = RecommendedPackIds.Contains(item.PackId);
        item.IsSelected = match;
        cb.IsChecked = match;
      }
    }
    UpdateCount();
  }

  private void Confirm_Click(object sender, RoutedEventArgs e)
  {
    SelectedPackIds = Items.Where(i => i.IsSelected).Select(i => i.PackId).ToList();
    if (SelectedPackIds.Count == 0)
    {
      MessageBox.Show("Select at least one content pack.", "Selection Required",
        MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    DialogResult = true;
    Close();
  }

  private void Cancel_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = false;
    Close();
  }

  private void UpdateCount()
  {
    var count = Items.Count(i => i.IsSelected);
    SelectionCount.Text = count + " of " + Items.Count + " selected";

    var selectedStigIds = Items
      .Where(i => i.IsSelected && string.Equals(i.Format, "STIG", StringComparison.OrdinalIgnoreCase))
      .Select(i => i.PackId)
      .ToList();

    var status = _statusProvider?.Invoke(selectedStigIds) ?? string.Empty;
    PickerStatus.Text = status;
    PickerStatus.Visibility = string.IsNullOrWhiteSpace(status) ? Visibility.Collapsed : Visibility.Visible;
    PickerStatus.Foreground = !string.IsNullOrWhiteSpace(status)
      && status.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase)
      ? (Brush)FindResource("WarningBrush")
      : (Brush)FindResource("TextMutedBrush");

    WarningLinesList.ItemsSource = _warningLines;
    WarningLinesList.Visibility = _warningLines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
  }
}

public class ContentPickerItem
{
  public string PackId { get; set; } = "";
  public string Name { get; set; } = "";
  public string Format { get; set; } = "";
  public string SourceLabel { get; set; } = "";
  public string ImportedAtLabel { get; set; } = "";
  public bool IsSelected { get; set; }
  public bool IsLocked { get; set; }
  public string LockReason { get; set; } = "";
}

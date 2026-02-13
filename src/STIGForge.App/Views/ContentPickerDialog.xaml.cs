using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using STIGForge.Core.Constants;

namespace STIGForge.App.Views;

public partial class ContentPickerDialog : Window
{
  public List<ContentPickerItem> Items { get; }
  public List<string> SelectedPackIds { get; private set; } = new();
  public HashSet<string> RecommendedPackIds { get; }
  private readonly List<CheckBox> _stigCheckBoxes = new();
  private readonly List<ContentPickerItem> _scapItems = new();
  private readonly List<CheckBox> _scapCheckBoxes = new();
  private readonly List<CheckBox> _autoCheckBoxes = new();

  public ContentPickerDialog(List<ContentPickerItem> items, HashSet<string>? recommendedIds = null)
  {
    InitializeComponent();
    Items = items;
    RecommendedPackIds = new HashSet<string>(
      (recommendedIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase))
        .Select(id => (id ?? string.Empty).Trim())
        .Where(id => !string.IsNullOrWhiteSpace(id)),
      StringComparer.OrdinalIgnoreCase);
    BuildGroupedUI();
    SyncScapToStigs();
    UpdateCount();
    SelectRecommendedBtn.Visibility = RecommendedPackIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
  }

  private void BuildGroupedUI()
  {
    var grouped = Items
      .GroupBy(i => i.Format)
      .ToDictionary(g => g.Key, g => g.ToList());

    if (grouped.TryGetValue(PackTypes.Stig, out var stigs) && stigs.Count > 0)
      AddStigSection(stigs);

    if (grouped.TryGetValue(PackTypes.Scap, out var scaps) && scaps.Count > 0)
      AddScapSection(scaps);

    var autoKeys = new[] { PackTypes.Gpo, PackTypes.Admx, PackTypes.LocalPolicy };
    foreach (var key in autoKeys)
    {
      if (grouped.TryGetValue(key, out var group) && group.Count > 0)
        AddAutoSection(key + "  —  auto-included on import", group);
    }

    var otherKeys = grouped.Keys
      .Where(k => k != PackTypes.Stig && k != PackTypes.Scap && !autoKeys.Contains(k))
      .OrderBy(k => k)
      .ToList();

    foreach (var key in otherKeys)
      AddAutoSection(key + " Content", grouped[key]);
  }

  private void AddStigSection(List<ContentPickerItem> items)
  {
    AddHeader("STIG Content  —  select the STIGs to include (SCAP auto-matches below)", false);

    var border = CreateSectionBorder();
    var stack = new StackPanel();

    foreach (var item in items)
    {
      var row = CreateItemRow();
      var cb = new CheckBox
      {
        IsChecked = item.IsSelected,
        VerticalAlignment = VerticalAlignment.Center,
        Tag = item
      };
      cb.Checked += (_, _) => { item.IsSelected = true; SyncScapToStigs(); UpdateCount(); };
      cb.Unchecked += (_, _) => { item.IsSelected = false; SyncScapToStigs(); UpdateCount(); };
      _stigCheckBoxes.Add(cb);

      row.Children.Add(cb);
      row.Children.Add(CreateNameBlock(item.Name));
      row.Children.Add(CreateMutedBlock(item.ImportedAtLabel, 130));
      stack.Children.Add(row);
    }

    border.Child = stack;
    GroupedPanel.Children.Add(border);
  }

  private void AddScapSection(List<ContentPickerItem> items)
  {
    _scapItems.AddRange(items);

    foreach (var item in items)
      item.IsSelected = IsRecommendedPack(item.PackId);

    AddHeader("SCAP Benchmarks  —  auto-matched to selected STIGs", true);
    AddHint("SCAP packs are automatically selected when a matching STIG is checked above.");

    var border = CreateSectionBorder();
    var stack = new StackPanel();

    foreach (var item in items)
    {
      var row = CreateItemRow();
      var cb = new CheckBox
      {
        IsChecked = item.IsSelected,
        VerticalAlignment = VerticalAlignment.Center,
        Tag = item,
        IsEnabled = false
      };
      _scapCheckBoxes.Add(cb);

      row.Children.Add(cb);
      row.Children.Add(CreateNameBlock(item.Name));
      row.Children.Add(CreateMutedBlock(item.ImportedAtLabel, 130));
      stack.Children.Add(row);
    }

    border.Child = stack;
    GroupedPanel.Children.Add(border);
  }

  private void AddAutoSection(string header, List<ContentPickerItem> items)
  {
    AddHeader(header, true);
    AddHint("Applicable packs are pre-selected. Uncheck any you don't want to apply.");

    var border = CreateSectionBorder();
    var stack = new StackPanel();

    foreach (var item in items)
    {
      var isApplicable = IsRecommendedPack(item.PackId);
      item.IsSelected = isApplicable;

      var row = CreateItemRow();
      var cb = new CheckBox
      {
        IsChecked = isApplicable,
        VerticalAlignment = VerticalAlignment.Center,
        IsEnabled = true,
        Tag = item
      };
      cb.Checked += (_, _) => { item.IsSelected = true; UpdateCount(); };
      cb.Unchecked += (_, _) => { item.IsSelected = false; UpdateCount(); };
      _autoCheckBoxes.Add(cb);

      row.Children.Add(cb);
      row.Children.Add(CreateNameBlock(item.Name));

      if (!isApplicable)
      {
        row.Children.Add(CreateMutedBlock("(not applicable to this machine)", 200));
      }
      else
      {
        row.Children.Add(CreateMutedBlock(item.ImportedAtLabel, 130));
      }

      stack.Children.Add(row);
    }

    border.Child = stack;
    GroupedPanel.Children.Add(border);
  }

  private void SyncScapToStigs()
  {
    var selectedStigTokens = Items
      .Where(i => i.Format == PackTypes.Stig && i.IsSelected)
      .Select(i => ExtractProductToken(i.Name))
      .Where(t => !string.IsNullOrWhiteSpace(t))
      .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < _scapItems.Count; i++)
    {
      var scapItem = _scapItems[i];
      var scapToken = ExtractProductToken(scapItem.Name);
      var matched = !string.IsNullOrWhiteSpace(scapToken) && selectedStigTokens.Contains(scapToken);
      var directlyRecommended = IsRecommendedPack(scapItem.PackId);
      var shouldSelect = matched || directlyRecommended;
      scapItem.IsSelected = shouldSelect;
      if (i < _scapCheckBoxes.Count)
        _scapCheckBoxes[i].IsChecked = shouldSelect;
    }
  }

  internal static string ExtractProductToken(string packName)
  {
    if (string.IsNullOrWhiteSpace(packName))
      return string.Empty;

    var normalized = packName.Replace("_", " ").Replace("-", " ");

    var prefixes = new[] { "U ", "DoD " };
    foreach (var prefix in prefixes)
    {
      if (normalized.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
        normalized = normalized.Substring(prefix.Length);
    }

    normalized = Regex.Replace(normalized,
      @"\s+(STIG|Benchmark|SCAP|XCCDF|Manual|Security Technical Implementation Guide)\b.*$",
      "", RegexOptions.IgnoreCase);

    normalized = Regex.Replace(normalized, @"\s+[Vv]\d+[Rr]\d+\s*$", "");

    return normalized.Trim();
  }

  private void AddHeader(string text, bool topMargin)
  {
    GroupedPanel.Children.Add(new TextBlock
    {
      Text = text,
      FontSize = 13,
      FontWeight = FontWeights.SemiBold,
      Margin = new Thickness(0, topMargin ? 14 : 0, 0, 6),
      Foreground = (Brush)FindResource("AccentBrush")
    });
  }

  private void AddHint(string text)
  {
    GroupedPanel.Children.Add(new TextBlock
    {
      Text = text,
      FontSize = 11,
      Margin = new Thickness(0, 0, 0, 4),
      Foreground = (Brush)FindResource("TextMutedBrush"),
      FontStyle = FontStyles.Italic
    });
  }

  private Border CreateSectionBorder() => new Border
  {
    BorderBrush = (Brush)FindResource("BorderBrush"),
    BorderThickness = new Thickness(1),
    CornerRadius = new CornerRadius(6),
    Background = (Brush)FindResource("SurfaceBrush"),
    Padding = new Thickness(8),
    Margin = new Thickness(0, 0, 0, 4)
  };

  private static StackPanel CreateItemRow() => new StackPanel
  {
    Orientation = Orientation.Horizontal,
    Margin = new Thickness(0, 2, 0, 2)
  };

  private static TextBlock CreateNameBlock(string text) => new TextBlock
  {
    Text = text,
    Width = 380,
    VerticalAlignment = VerticalAlignment.Center,
    Margin = new Thickness(6, 0, 0, 0),
    TextTrimming = TextTrimming.CharacterEllipsis
  };

  private TextBlock CreateMutedBlock(string text, double width) => new TextBlock
  {
    Text = text,
    Width = width,
    VerticalAlignment = VerticalAlignment.Center,
    Foreground = (Brush)FindResource("TextMutedBrush")
  };

  private void SelectAll_Click(object sender, RoutedEventArgs e)
  {
    foreach (var cb in _stigCheckBoxes)
    {
      if (cb.Tag is ContentPickerItem item)
      {
        item.IsSelected = true;
        cb.IsChecked = true;
      }
    }
    SyncScapToStigs();
    UpdateCount();
  }

  private void SelectNone_Click(object sender, RoutedEventArgs e)
  {
    foreach (var cb in _stigCheckBoxes)
    {
      if (cb.Tag is ContentPickerItem item)
      {
        item.IsSelected = false;
        cb.IsChecked = false;
      }
    }
    SyncScapToStigs();
    UpdateCount();
  }

  private void SelectRecommended_Click(object sender, RoutedEventArgs e)
  {
    foreach (var cb in _stigCheckBoxes)
    {
      if (cb.Tag is ContentPickerItem item)
      {
        var match = IsRecommendedPack(item.PackId);
        item.IsSelected = match;
        cb.IsChecked = match;
      }
    }
    foreach (var cb in _autoCheckBoxes)
    {
      if (cb.Tag is ContentPickerItem item)
      {
        var match = IsRecommendedPack(item.PackId);
        item.IsSelected = match;
        cb.IsChecked = match;
      }
    }
    foreach (var cb in _scapCheckBoxes)
    {
      if (cb.Tag is ContentPickerItem item)
      {
        var match = IsRecommendedPack(item.PackId);
        item.IsSelected = match;
        cb.IsChecked = match;
      }
    }
    SyncScapToStigs();
    UpdateCount();
  }

  private void Confirm_Click(object sender, RoutedEventArgs e)
  {
    SelectedPackIds = Items.Where(i => i.IsSelected).Select(i => i.PackId).ToList();
    if (SelectedPackIds.Count == 0)
    {
      MessageBox.Show("Select at least one STIG to include.", "Selection Required",
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
    var stigCount = Items.Count(i => i.Format == PackTypes.Stig && i.IsSelected);
    var scapCount = _scapItems.Count(i => i.IsSelected);
    var otherCount = Items.Count(i => i.Format != PackTypes.Stig && i.Format != PackTypes.Scap && i.IsSelected);
    var total = stigCount + scapCount + otherCount;
    SelectionCount.Text = $"{stigCount} STIGs, {scapCount} SCAP matched, {otherCount} auto-included ({total} total)";
  }

  private bool IsRecommendedPack(string? packId)
  {
    var normalized = (packId ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(normalized))
      return false;
    return RecommendedPackIds.Contains(normalized);
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
}

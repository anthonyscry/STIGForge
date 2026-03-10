using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using STIGForge.Apply.OrgSettings;
using STIGForge.Core.Models;

namespace STIGForge.App.Views;

public partial class PreflightDialog : Window
{
  private const int DwmwaCaptionColor = 35;
  private const int DwmwaTextColor = 36;

  public PreflightDialog()
  {
    InitializeComponent();
    Loaded += (_, _) => ApplyTitleBarColors();
  }

  /// <summary>
  /// The completed profile after user clicks Apply. Null if skipped/cancelled.
  /// </summary>
  public OrgSettingsProfile? CompletedProfile { get; private set; }

  private PreflightDialogViewModel? ViewModel => DataContext as PreflightDialogViewModel;

  private void Import_Click(object sender, RoutedEventArgs e)
  {
    var dlg = new OpenFileDialog
    {
      Title = "Import Organizational Settings",
      Filter = "STIGForge OrgSettings (*.stigorgsettings.json)|*.stigorgsettings.json|All Files (*.*)|*.*",
      DefaultExt = ".stigorgsettings.json"
    };

    if (dlg.ShowDialog(this) == true)
    {
      var loaded = OrgSettingsSerializer.Load(dlg.FileName);
      if (loaded != null)
      {
        ViewModel?.MergeFrom(loaded);
      }
      else
      {
        MessageBox.Show(this, "Could not load the selected file.", "Import Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }
  }

  private void Export_Click(object sender, RoutedEventArgs e)
  {
    var vm = ViewModel;
    if (vm == null) return;

    var dlg = new SaveFileDialog
    {
      Title = "Export Organizational Settings",
      Filter = "STIGForge OrgSettings (*.stigorgsettings.json)|*.stigorgsettings.json",
      DefaultExt = ".stigorgsettings.json",
      FileName = string.IsNullOrWhiteSpace(vm.ProfileName) ? "OrgSettings" : vm.ProfileName
    };

    if (dlg.ShowDialog(this) == true)
    {
      var profile = vm.BuildProfile();
      OrgSettingsSerializer.Save(dlg.FileName, profile);
    }
  }

  private void Skip_Click(object sender, RoutedEventArgs e)
  {
    CompletedProfile = null;
    DialogResult = false;
    Close();
  }

  private void Apply_Click(object sender, RoutedEventArgs e)
  {
    var vm = ViewModel;
    if (vm == null) return;

    CompletedProfile = vm.BuildProfile();
    DialogResult = true;
    Close();
  }

  private void ApplyTitleBarColors()
  {
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      return;

    var hwnd = new WindowInteropHelper(this).Handle;
    if (hwnd == IntPtr.Zero)
      return;

    if (TryResolveColor("WindowBackgroundBrush", out var captionColor))
    {
      var captionColorRef = ToColorRef(captionColor);
      _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColorRef, sizeof(int));
    }

    if (TryResolveColor("AccentBrush", out var textColor))
    {
      var textColorRef = ToColorRef(textColor);
      _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColorRef, sizeof(int));
    }
  }

  private static bool TryResolveColor(string resourceKey, out Color color)
  {
    if (Application.Current?.Resources[resourceKey] is SolidColorBrush brush)
    {
      color = brush.Color;
      return true;
    }
    color = default;
    return false;
  }

  private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

  [DllImport("dwmapi.dll")]
  private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}

/// <summary>
/// ViewModel for the Pre-flight dialog. Drives the category filter, entry list,
/// and tracks completion state.
/// </summary>
public sealed class PreflightDialogViewModel : INotifyPropertyChanged
{
  private string _profileName = "Default";
  private readonly string _osTarget;
  private readonly string _roleTemplate;
  private readonly string _stigVersion;
  private readonly List<OrgSettingEntry> _allEntries;

  public PreflightDialogViewModel(
    List<OrgSettingEntry> entries,
    string osTarget = "",
    string roleTemplate = "",
    string stigVersion = "")
  {
    _allEntries = entries;
    _osTarget = osTarget;
    _roleTemplate = roleTemplate;
    _stigVersion = stigVersion;

    var categoryNames = entries
      .Select(e => e.Category)
      .Where(c => !string.IsNullOrWhiteSpace(c))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(c => c)
      .ToList();

    var all = new CategoryFilter("All", true);
    all.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FilteredEntries));
    Categories.Add(all);

    foreach (var cat in categoryNames)
    {
      var f = new CategoryFilter(cat, false);
      f.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FilteredEntries));
      Categories.Add(f);
    }
  }

  public ObservableCollection<CategoryFilter> Categories { get; } = new();

  public string ProfileName
  {
    get => _profileName;
    set { _profileName = value; OnPropertyChanged(nameof(ProfileName)); }
  }

  public int TotalCount => _allEntries.Count;

  public int FilledCount => _allEntries.Count(e => !string.IsNullOrWhiteSpace(e.Value));

  public int HighSeverityCount => _allEntries.Count(e =>
    string.Equals(e.Severity, "high", StringComparison.OrdinalIgnoreCase));

  public bool HasHighSeverity => HighSeverityCount > 0;

  public IEnumerable<OrgSettingEntry> FilteredEntries
  {
    get
    {
      var allFilter = Categories.FirstOrDefault(c => c.Name == "All");
      if (allFilter?.IsSelected == true)
        return _allEntries;

      var selected = Categories
        .Where(c => c.Name != "All" && c.IsSelected)
        .Select(c => c.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      if (selected.Count == 0)
        return _allEntries;

      return _allEntries.Where(e => selected.Contains(e.Category));
    }
  }

  public void MergeFrom(OrgSettingsProfile saved)
  {
    OrgSettingsSerializer.MergeAnswers(_allEntries, saved);
    if (!string.IsNullOrWhiteSpace(saved.ProfileName))
      ProfileName = saved.ProfileName;

    OnPropertyChanged(nameof(FilledCount));
    OnPropertyChanged(nameof(FilteredEntries));
  }

  public OrgSettingsProfile BuildProfile()
  {
    return new OrgSettingsProfile
    {
      ProfileName = ProfileName,
      OsTarget = _osTarget,
      RoleTemplate = _roleTemplate,
      StigVersion = _stigVersion,
      CreatedAt = DateTimeOffset.UtcNow,
      UpdatedAt = DateTimeOffset.UtcNow,
      CreatedBy = Environment.UserName,
      Entries = _allEntries.ToList()
    };
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged(string name) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Category filter toggle for the pre-flight dialog.
/// </summary>
public sealed class CategoryFilter : INotifyPropertyChanged
{
  private bool _isSelected;

  public CategoryFilter(string name, bool isSelected)
  {
    Name = name;
    _isSelected = isSelected;
  }

  public string Name { get; }

  public bool IsSelected
  {
    get => _isSelected;
    set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
  }

  public event PropertyChangedEventHandler? PropertyChanged;
}

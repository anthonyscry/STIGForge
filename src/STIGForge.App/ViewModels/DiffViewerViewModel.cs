using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using static STIGForge.Core.Services.BaselineDiffService;

namespace STIGForge.App.ViewModels;

public partial class DiffViewerViewModel : ObservableObject
{
  private readonly BaselineDiff _diff;

  [ObservableProperty] private string _baselinePackName = string.Empty;
  [ObservableProperty] private string _targetPackName = string.Empty;
  [ObservableProperty] private int _addedCount;
  [ObservableProperty] private int _removedCount;
  [ObservableProperty] private int _modifiedCount;
  [ObservableProperty] private int _reviewRequiredCount;
  [ObservableProperty] private int _unchangedCount;
  [ObservableProperty] private bool _hasDiff;
  [ObservableProperty] private string _statusMessage = "Loading comparison...";

  // Added controls
  [ObservableProperty] private List<ControlRecord> _addedControls = new();
  [ObservableProperty] private ControlRecord? _selectedAddedControl;
  public bool HasSelectedAddedControl => SelectedAddedControl != null;

  // Removed controls
  [ObservableProperty] private List<ControlRecord> _removedControls = new();
  [ObservableProperty] private ControlRecord? _selectedRemovedControl;
  public bool HasSelectedRemovedControl => SelectedRemovedControl != null;

  // Modified controls
  [ObservableProperty] private List<ModifiedControlDisplay> _modifiedControls = new();
  [ObservableProperty] private ModifiedControlDisplay? _selectedModifiedItem;
  [ObservableProperty] private List<FieldChangeDisplay> _selectedModifiedChanges = new();
  public bool HasSelectedModifiedItem => SelectedModifiedItem != null;

  // Review-required controls
  [ObservableProperty] private List<ReviewRequiredControlDisplay> _reviewRequiredControls = new();
  [ObservableProperty] private ReviewRequiredControlDisplay? _selectedReviewRequiredControl;
  public bool HasSelectedReviewRequiredControl => SelectedReviewRequiredControl != null;

  public DiffViewerViewModel(BaselineDiff diff, string baselinePackName, string targetPackName)
  {
    _diff = diff;
    BaselinePackName = baselinePackName;
    TargetPackName = targetPackName;

    LoadDiffData();
  }

  private void LoadDiffData()
  {
    AddedCount = _diff.TotalAdded;
    RemovedCount = _diff.TotalRemoved;
    ModifiedCount = _diff.TotalModified;
    ReviewRequiredCount = _diff.TotalReviewRequired;
    UnchangedCount = _diff.TotalUnchanged;

    AddedControls = _diff.AddedControls.Select(d => d.NewControl!).ToList();
    RemovedControls = _diff.RemovedControls.Select(d => d.BaselineControl!).ToList();

    // Convert modified controls to display items
    ModifiedControls = _diff.ModifiedControls.Select(m => new ModifiedControlDisplay
    {
      ControlId = m.NewControl?.ControlId ?? m.BaselineControl?.ControlId ?? "(Unknown)",
      Title = m.NewControl?.Title ?? "(No title)",
      Classification = m.RequiresReview ? "review-required" : "changed",
      Impact = GetHighestImpact(m.Changes).ToString(),
      ImpactColor = GetImpactBrush(GetHighestImpact(m.Changes)),
      ChangesSummary = string.Join(", ", m.Changes.Select(c => c.FieldName)),
      Changes = m.Changes
    }).ToList();

    ReviewRequiredControls = _diff.ReviewRequiredControls.Select(c => new ReviewRequiredControlDisplay
    {
      ControlKey = c.ControlKey,
      Title = c.NewControl?.Title ?? c.BaselineControl?.Title ?? "(No title)",
      ChangeType = c.ChangeType.ToString(),
      Reason = string.IsNullOrWhiteSpace(c.ReviewReason)
        ? "Manual review required before promotion."
        : c.ReviewReason!
    }).ToList();

    HasDiff = true;
    StatusMessage = string.Empty;
  }

  private static FieldChangeImpact GetHighestImpact(List<ControlFieldChange> changes)
  {
    if (changes.Any(c => c.Impact == FieldChangeImpact.High)) return FieldChangeImpact.High;
    if (changes.Any(c => c.Impact == FieldChangeImpact.Medium)) return FieldChangeImpact.Medium;
    return FieldChangeImpact.Low;
  }

  partial void OnSelectedAddedControlChanged(ControlRecord? value)
  {
    OnPropertyChanged(nameof(HasSelectedAddedControl));
  }

  partial void OnSelectedRemovedControlChanged(ControlRecord? value)
  {
    OnPropertyChanged(nameof(HasSelectedRemovedControl));
  }

  partial void OnSelectedModifiedItemChanged(ModifiedControlDisplay? value)
  {
    if (value != null)
    {
      SelectedModifiedChanges = value.Changes.Select(c => new FieldChangeDisplay
      {
        FieldName = c.FieldName,
        OldValue = TruncateForDisplay(c.OldValue ?? "(null)"),
        NewValue = TruncateForDisplay(c.NewValue ?? "(null)"),
        Impact = c.Impact.ToString(),
        ImpactColor = GetImpactBrush(c.Impact)
      }).ToList();
    }
    else
    {
      SelectedModifiedChanges = new();
    }
    OnPropertyChanged(nameof(HasSelectedModifiedItem));
  }

  partial void OnSelectedReviewRequiredControlChanged(ReviewRequiredControlDisplay? value)
  {
    OnPropertyChanged(nameof(HasSelectedReviewRequiredControl));
  }

  [RelayCommand]
  private async Task ExportReport()
  {
    var dialog = new SaveFileDialog
    {
      Filter = "Markdown files (*.md)|*.md|Text files (*.txt)|*.txt|All files (*.*)|*.*",
      DefaultExt = "md",
      FileName = BuildDefaultFileName("md")
    };

    if (dialog.ShowDialog() == true)
    {
      try
      {
        await File.WriteAllTextAsync(dialog.FileName, GenerateMarkdownReport(), Encoding.UTF8);
        MessageBox.Show($"Report exported to:\n{dialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Failed to export report:\n{ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }
  }

  [RelayCommand]
  private async Task ExportJsonReport()
  {
    var dialog = new SaveFileDialog
    {
      Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
      DefaultExt = "json",
      FileName = BuildDefaultFileName("json")
    };

    if (dialog.ShowDialog() == true)
    {
      try
      {
        var json = JsonSerializer.Serialize(_diff, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(dialog.FileName, json, Encoding.UTF8);
        MessageBox.Show($"JSON report exported to:\n{dialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Failed to export JSON report:\n{ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }
  }

  private string GenerateMarkdownReport()
  {
    var sb = new StringBuilder();
    sb.AppendLine($"# STIG Pack Comparison Report");
    sb.AppendLine();
    sb.AppendLine($"**Baseline:** {BaselinePackName}");
    sb.AppendLine($"**Target:** {TargetPackName}");
    sb.AppendLine();
    sb.AppendLine("## Summary");
    sb.AppendLine();
    sb.AppendLine($"- **Added:** {AddedCount} controls");
    sb.AppendLine($"- **Removed:** {RemovedCount} controls");
    sb.AppendLine($"- **Changed:** {ModifiedCount} controls");
    sb.AppendLine($"- **Review Required:** {ReviewRequiredCount} controls");
    sb.AppendLine($"- **Unchanged:** {UnchangedCount} controls");
    sb.AppendLine();

    // Added controls
    if (AddedControls.Count > 0)
    {
      sb.AppendLine("## Added Controls");
      sb.AppendLine();
      foreach (var control in AddedControls)
      {
        sb.AppendLine($"### {control.ControlId}");
        sb.AppendLine($"**Title:** {control.Title}");
        sb.AppendLine($"**Severity:** {control.Severity}");
        sb.AppendLine();
      }
    }

    // Removed controls
    if (RemovedControls.Count > 0)
    {
      sb.AppendLine("## Removed Controls");
      sb.AppendLine();
      foreach (var control in RemovedControls)
      {
        sb.AppendLine($"### {control.ControlId}");
        sb.AppendLine($"**Title:** {control.Title}");
        sb.AppendLine($"**Severity:** {control.Severity}");
        sb.AppendLine();
      }
    }

    // Modified controls
    if (ModifiedControls.Count > 0)
    {
      sb.AppendLine("## Changed Controls");
      sb.AppendLine();
      foreach (var modified in ModifiedControls)
      {
        sb.AppendLine($"### {modified.ControlId}");
        sb.AppendLine($"**Title:** {modified.Title}");
        sb.AppendLine($"**Classification:** {modified.Classification}");
        sb.AppendLine($"**Impact:** {modified.Impact}");
        sb.AppendLine($"**Changes:** {modified.ChangesSummary}");
        sb.AppendLine();

        foreach (var change in modified.Changes)
        {
          sb.AppendLine($"#### Field: {change.FieldName} ({change.Impact})");
          sb.AppendLine("**Before:**");
          sb.AppendLine("```");
          sb.AppendLine(change.OldValue ?? "(null)");
          sb.AppendLine("```");
          sb.AppendLine("**After:**");
          sb.AppendLine("```");
          sb.AppendLine(change.NewValue ?? "(null)");
          sb.AppendLine("```");
          sb.AppendLine();
        }
      }
    }

    if (_diff.ReviewRequiredControls.Count > 0)
    {
      sb.AppendLine("## Review Required Controls");
      sb.AppendLine();
      foreach (var control in _diff.ReviewRequiredControls)
        sb.AppendLine($"- **{control.ControlKey}** - {control.ReviewReason}");
      sb.AppendLine();
    }

    return sb.ToString();
  }

  private static Brush GetImpactBrush(FieldChangeImpact impact)
  {
    return impact switch
    {
      FieldChangeImpact.Low => new SolidColorBrush(Color.FromRgb(100, 180, 100)), // Green
      FieldChangeImpact.Medium => new SolidColorBrush(Color.FromRgb(255, 165, 0)), // Orange
      FieldChangeImpact.High => new SolidColorBrush(Color.FromRgb(220, 50, 50)),   // Red
      _ => Brushes.Gray
    };
  }

  private static string TruncateForDisplay(string text)
  {
    const int maxLength = 500;
    if (text.Length <= maxLength) return text;
    return text.Substring(0, maxLength) + "... (truncated)";
  }

  private string BuildDefaultFileName(string extension)
  {
    var baseline = SanitizeFileNamePart(BaselinePackName);
    var target = SanitizeFileNamePart(TargetPackName);
    return $"diff_{baseline}_to_{target}.{extension}";
  }

  private static string SanitizeFileNamePart(string value)
  {
    var invalidChars = Path.GetInvalidFileNameChars();
    var cleaned = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
  }
}

// Display models
public class ModifiedControlDisplay
{
  public string ControlId { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string Classification { get; set; } = string.Empty;
  public string Impact { get; set; } = string.Empty;
  public Brush ImpactColor { get; set; } = Brushes.Gray;
  public string ChangesSummary { get; set; } = string.Empty;
  public List<ControlFieldChange> Changes { get; set; } = new();
}

public class FieldChangeDisplay
{
  public string FieldName { get; set; } = string.Empty;
  public string OldValue { get; set; } = string.Empty;
  public string NewValue { get; set; } = string.Empty;
  public string Impact { get; set; } = string.Empty;
  public Brush ImpactColor { get; set; } = Brushes.Gray;
}

public class ReviewRequiredControlDisplay
{
  public string ControlKey { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string ChangeType { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
}

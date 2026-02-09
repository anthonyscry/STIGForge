using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using static STIGForge.Core.Services.OverlayRebaseService;

namespace STIGForge.App.ViewModels;

public partial class RebaseWizardViewModel : ObservableObject
{
  private readonly IControlRepository _controls;
  private readonly IOverlayRepository _overlays;
  private readonly OverlayRebaseService _rebaseService;
  private RebaseReport? _report;

  public event Action? CloseRequested;

  [ObservableProperty] private WizardScreen _currentScreen = WizardScreen.Welcome;
  [ObservableProperty] private string _stepText = "Step 1 of 3";
  
  // Welcome screen
  [ObservableProperty] private List<Overlay> _availableOverlays = new();
  [ObservableProperty] private List<ContentPack> _availablePacks = new();
  [ObservableProperty] private Overlay? _selectedOverlay;
  [ObservableProperty] private ContentPack? _selectedBaselinePack;
  [ObservableProperty] private ContentPack? _selectedTargetPack;

  // Analysis screen
  [ObservableProperty] private int _totalOverrides;
  [ObservableProperty] private int _autoRebasedCount;
  [ObservableProperty] private int _needsReviewCount;
  [ObservableProperty] private int _blockingConflictCount;
  [ObservableProperty] private int _safeActionCount;
  [ObservableProperty] private int _highRiskCount;
  [ObservableProperty] private string _overallConfidenceDisplay = "0%";
  [ObservableProperty] private string _blockingConflictSummary = string.Empty;
  [ObservableProperty] private string _analysisStatus = string.Empty;
  [ObservableProperty] private string _recoveryGuidance = string.Empty;
  [ObservableProperty] private List<RebaseActionDisplay> _autoRebasedActions = new();
  [ObservableProperty] private List<RebaseActionDisplay> _needsReviewActions = new();
  [ObservableProperty] private RebaseActionDisplay? _selectedReviewAction;
  public bool HasSelectedReviewAction => SelectedReviewAction != null;
  public bool HasBlockingConflicts => BlockingConflictCount > 0;
  public bool CanApplyRebase => CurrentScreen == WizardScreen.Analysis && !HasBlockingConflicts;
  public bool CanExportReport => CurrentScreen == WizardScreen.Analysis && _report != null;

  // Completion screen
  [ObservableProperty] private string _newOverlayId = string.Empty;

  // Screen visibility
  public bool ShowWelcome => CurrentScreen == WizardScreen.Welcome;
  public bool ShowAnalysis => CurrentScreen == WizardScreen.Analysis;
  public bool ShowCompletion => CurrentScreen == WizardScreen.Completion;

  public RebaseWizardViewModel(
    IControlRepository controls, 
    IOverlayRepository overlays,
    List<Overlay> availableOverlays,
    List<ContentPack> availablePacks,
    IAuditTrailService? audit = null)
  {
    _controls = controls;
    _overlays = overlays;
    _rebaseService = new OverlayRebaseService(_overlays, new BaselineDiffService(_controls), audit);
    AvailableOverlays = availableOverlays;
    AvailablePacks = availablePacks;
  }

  [RelayCommand]
  private async Task AnalyzeRebase()
  {
    try
    {
      if (SelectedOverlay == null || SelectedBaselinePack == null || SelectedTargetPack == null)
        return;

      if (SelectedBaselinePack.PackId == SelectedTargetPack.PackId)
      {
        System.Windows.MessageBox.Show("Baseline and target packs must be different.", "Invalid Selection", 
          System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        return;
      }

      // Run rebase analysis
      _report = await _rebaseService.RebaseOverlayAsync(
        SelectedOverlay.OverlayId,
        SelectedBaselinePack.PackId,
        SelectedTargetPack.PackId,
        CancellationToken.None);

      // Convert report to display models
      TotalOverrides = _report.Actions.Count;
      
      var autoActions = _report.Actions
        .Where(a => !a.IsBlockingConflict && (a.ActionType == RebaseActionType.Keep || a.ActionType == RebaseActionType.KeepWithWarning))
        .ToList();
      AutoRebasedCount = autoActions.Count;
      AutoRebasedActions = autoActions.Select(ToDisplay).ToList();

      var reviewActions = _report.Actions
        .Where(a => a.IsBlockingConflict || a.RequiresReview || a.ActionType == RebaseActionType.ReviewRequired || a.ActionType == RebaseActionType.Remove || a.ActionType == RebaseActionType.Remap)
        .ToList();
      NeedsReviewCount = reviewActions.Count;
      NeedsReviewActions = reviewActions.Select(ToDisplay).ToList();
      BlockingConflictCount = _report.BlockingConflicts;
      SafeActionCount = _report.SafeActions;
      HighRiskCount = _report.HighRisk;
      OverallConfidenceDisplay = _report.OverallConfidence.ToString("P0");
      BlockingConflictSummary = BlockingConflictCount == 0
        ? string.Empty
        : string.Join(Environment.NewLine, _report.Actions
          .Where(a => a.IsBlockingConflict)
          .Select(a => $"{a.OriginalControlKey}: {a.RecommendedAction}"));
      AnalysisStatus = BlockingConflictCount == 0
        ? "Rebase analysis complete. You can export artifacts or apply the rebase."
        : "Rebase analysis complete. Resolve blocking conflicts before applying rebase.";
      RecoveryGuidance = BuildRecoveryGuidance(BlockingConflictCount > 0);

      CurrentScreen = WizardScreen.Analysis;
      StepText = "Step 2 of 3";
      OnPropertyChanged(nameof(ShowWelcome));
      OnPropertyChanged(nameof(ShowAnalysis));
      OnPropertyChanged(nameof(ShowCompletion));
      OnPropertyChanged(nameof(HasBlockingConflicts));
      OnPropertyChanged(nameof(CanApplyRebase));
      OnPropertyChanged(nameof(CanExportReport));
    }
    catch (Exception ex)
    {
      System.Windows.MessageBox.Show($"Rebase analysis failed:\n{ex.Message}", "Error", 
        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
  }

  [RelayCommand]
  private async Task ApplyRebase()
  {
    try
    {
      if (_report == null || SelectedOverlay == null) return;

      if (_report.HasBlockingConflicts)
      {
        var message = string.IsNullOrWhiteSpace(BlockingConflictSummary)
          ? "Resolve blocking conflicts before applying rebase."
          : "Resolve blocking conflicts before applying rebase:\n\n" + BlockingConflictSummary;
        if (!string.IsNullOrWhiteSpace(RecoveryGuidance))
          message += "\n\n" + RecoveryGuidance;
        System.Windows.MessageBox.Show(message, "Rebase blocked", 
          System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        return;
      }

      var rebasedOverlay = await _rebaseService.ApplyRebaseAsync(SelectedOverlay.OverlayId, _report, CancellationToken.None);

      NewOverlayId = rebasedOverlay.OverlayId;
      AnalysisStatus = "Rebase apply completed successfully.";
      RecoveryGuidance = "Next action: validate the rebased overlay with diff/verify workflows before promotion. Rollback guidance: keep the original overlay as fallback until validation completes.";
      CurrentScreen = WizardScreen.Completion;
      StepText = "Step 3 of 3";
      OnPropertyChanged(nameof(ShowWelcome));
      OnPropertyChanged(nameof(ShowAnalysis));
      OnPropertyChanged(nameof(ShowCompletion));
      OnPropertyChanged(nameof(CanApplyRebase));
      OnPropertyChanged(nameof(CanExportReport));
    }
    catch (Exception ex)
    {
      System.Windows.MessageBox.Show($"Failed to apply rebase:\n{ex.Message}", "Error", 
        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
  }

  [RelayCommand]
  private void Cancel()
  {
    CloseRequested?.Invoke();
  }

  [RelayCommand]
  private void Close()
  {
    CloseRequested?.Invoke();
  }

  partial void OnSelectedReviewActionChanged(RebaseActionDisplay? value)
  {
    OnPropertyChanged(nameof(HasSelectedReviewAction));
  }

  partial void OnCurrentScreenChanged(WizardScreen value)
  {
    OnPropertyChanged(nameof(CanApplyRebase));
    OnPropertyChanged(nameof(CanExportReport));
  }

  partial void OnBlockingConflictCountChanged(int value)
  {
    OnPropertyChanged(nameof(HasBlockingConflicts));
    OnPropertyChanged(nameof(CanApplyRebase));
  }

  [RelayCommand]
  private async Task ExportMarkdownReport()
  {
    if (_report == null) return;

    var dialog = new SaveFileDialog
    {
      Filter = "Markdown files (*.md)|*.md|Text files (*.txt)|*.txt|All files (*.*)|*.*",
      DefaultExt = "md",
      FileName = BuildDefaultFileName("md")
    };

    if (dialog.ShowDialog() != true)
      return;

    try
    {
      await File.WriteAllTextAsync(dialog.FileName, GenerateMarkdownReport(_report), Encoding.UTF8);
      AnalysisStatus = "Rebase markdown report exported: " + dialog.FileName;
    }
    catch (Exception ex)
    {
      System.Windows.MessageBox.Show($"Failed to export markdown report:\n{ex.Message}", "Export Failed",
        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
  }

  [RelayCommand]
  private async Task ExportJsonReport()
  {
    if (_report == null) return;

    var dialog = new SaveFileDialog
    {
      Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
      DefaultExt = "json",
      FileName = BuildDefaultFileName("json")
    };

    if (dialog.ShowDialog() != true)
      return;

    try
    {
      var json = JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true });
      await File.WriteAllTextAsync(dialog.FileName, json, Encoding.UTF8);
      AnalysisStatus = "Rebase JSON report exported: " + dialog.FileName;
    }
    catch (Exception ex)
    {
      System.Windows.MessageBox.Show($"Failed to export JSON report:\n{ex.Message}", "Export Failed",
        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
  }

  private string GenerateMarkdownReport(RebaseReport report)
  {
    var sb = new StringBuilder();
    sb.AppendLine($"# Rebase Report: {report.OverlayId}");
    sb.AppendLine();
    sb.AppendLine($"- **Baseline:** {report.BaselinePackId}");
    sb.AppendLine($"- **Target:** {report.NewPackId}");
    sb.AppendLine($"- **Overall Confidence:** {report.OverallConfidence:P0}");
    sb.AppendLine($"- **Blocking conflicts:** {report.BlockingConflicts}");
    sb.AppendLine();
    sb.AppendLine("| Control | Action | Confidence | Requires Review | Blocking | Reason | Recommended Action |");
    sb.AppendLine("|---------|--------|------------|-----------------|----------|--------|--------------------|");

    foreach (var action in report.Actions)
    {
      sb.AppendLine($"| {action.OriginalControlKey} | {action.ActionType} | {action.Confidence:P0} | {(action.RequiresReview ? "Yes" : "No")} | {(action.IsBlockingConflict ? "Yes" : "No")} | {action.Reason} | {action.RecommendedAction} |");
    }

    return sb.ToString();
  }

  private string BuildDefaultFileName(string extension)
  {
    var overlay = SelectedOverlay?.Name ?? SelectedOverlay?.OverlayId ?? "overlay";
    var baseline = SelectedBaselinePack?.Name ?? SelectedBaselinePack?.PackId ?? "baseline";
    var target = SelectedTargetPack?.Name ?? SelectedTargetPack?.PackId ?? "target";

    return $"rebase_{SanitizeFileNamePart(overlay)}_{SanitizeFileNamePart(baseline)}_to_{SanitizeFileNamePart(target)}.{extension}";
  }

  private static string BuildRecoveryGuidance(bool hasBlockingConflicts)
  {
    if (hasBlockingConflicts)
    {
      return "Required artifacts: export Markdown and JSON rebase reports before sign-off. Next action: update overlay overrides in Profiles > Edit Overlays, then rerun Analyze Rebase. Rollback guidance: keep using the current overlay until blocking conflicts are resolved.";
    }

    return "Required artifacts: export Markdown and JSON rebase reports for release evidence. Next action: apply rebase and validate with verify/export workflows. Rollback guidance: retain the original overlay until rebased output is validated.";
  }

  private static string SanitizeFileNamePart(string value)
  {
    var invalidChars = Path.GetInvalidFileNameChars();
    var cleaned = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
  }

  private static RebaseActionDisplay ToDisplay(RebaseAction action)
  {
    return new RebaseActionDisplay
    {
      ControlId = action.OriginalControlKey,
      Action = action.ActionType.ToString(),
      Confidence = action.Confidence,
      ConfidenceDisplay = $"{action.Confidence:P0}",
      Reason = action.Reason,
      RecommendedAction = action.RecommendedAction,
      IsBlockingConflict = action.IsBlockingConflict,
      ChangesSummary = action.FieldChanges.Count > 0 
        ? string.Join(", ", action.FieldChanges.Select(f => f.FieldName)) 
        : "(No changes)"
    };
  }

  public enum WizardScreen
  {
    Welcome,
    Analysis,
    Completion
  }
}

// Display model
public class RebaseActionDisplay
{
  public string ControlId { get; set; } = string.Empty;
  public string Action { get; set; } = string.Empty;
  public double Confidence { get; set; }
  public string ConfidenceDisplay { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string RecommendedAction { get; set; } = string.Empty;
  public bool IsBlockingConflict { get; set; }
  public string ChangesSummary { get; set; } = string.Empty;
}

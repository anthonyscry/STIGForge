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

namespace STIGForge.App.ViewModels;

public partial class AnswerRebaseWizardViewModel : ObservableObject
{
  private readonly IControlRepository _controls;
  private readonly AnswerRebaseService _rebaseService;
  private readonly ManualAnswerService _answerService;
  private AnswerRebaseReport? _report;

  public event Action? CloseRequested;

  [ObservableProperty] private AnswerRebaseWizardScreen _currentScreen = AnswerRebaseWizardScreen.Welcome;
  [ObservableProperty] private string _stepText = "Step 1 of 3";

  // Welcome screen
  [ObservableProperty] private List<ContentPack> _availablePacks = new();
  [ObservableProperty] private ContentPack? _selectedBaselinePack;
  [ObservableProperty] private ContentPack? _selectedTargetPack;
  [ObservableProperty] private string _bundleRoot = string.Empty;

  // Analysis screen
  [ObservableProperty] private int _totalAnswers;
  [ObservableProperty] private int _autoCarriedCount;
  [ObservableProperty] private int _needsReviewCount;
  [ObservableProperty] private int _blockingConflictCount;
  [ObservableProperty] private int _safeActionCount;
  [ObservableProperty] private int _highRiskCount;
  [ObservableProperty] private string _overallConfidenceDisplay = "0%";
  [ObservableProperty] private string _blockingConflictSummary = string.Empty;
  [ObservableProperty] private string _analysisStatus = string.Empty;
  [ObservableProperty] private string _recoveryGuidance = string.Empty;
  [ObservableProperty] private List<AnswerRebaseActionDisplay> _autoCarriedActions = new();
  [ObservableProperty] private List<AnswerRebaseActionDisplay> _needsReviewActions = new();
  [ObservableProperty] private AnswerRebaseActionDisplay? _selectedReviewAction;
  public bool HasSelectedReviewAction => SelectedReviewAction != null;
  public bool HasBlockingConflicts => BlockingConflictCount > 0;
  public bool CanApplyRebase => CurrentScreen == AnswerRebaseWizardScreen.Analysis && !HasBlockingConflicts;
  public bool CanExportReport => CurrentScreen == AnswerRebaseWizardScreen.Analysis && _report != null;

  // Completion screen
  [ObservableProperty] private string _rebasedFilePath = string.Empty;

  // Screen visibility
  public bool ShowWelcome => CurrentScreen == AnswerRebaseWizardScreen.Welcome;
  public bool ShowAnalysis => CurrentScreen == AnswerRebaseWizardScreen.Analysis;
  public bool ShowCompletion => CurrentScreen == AnswerRebaseWizardScreen.Completion;

  public AnswerRebaseWizardViewModel(
    IControlRepository controls,
    List<ContentPack> availablePacks,
    string bundleRoot,
    IAuditTrailService? audit = null)
  {
    _controls = controls;
    _answerService = new ManualAnswerService();
    var diffService = new BaselineDiffService(_controls);
    _rebaseService = new AnswerRebaseService(_answerService, diffService, audit);
    AvailablePacks = availablePacks;
    BundleRoot = bundleRoot;
  }

  [RelayCommand]
  private async Task AnalyzeRebase()
  {
    try
    {
      if (SelectedBaselinePack == null || SelectedTargetPack == null)
        return;

      if (SelectedBaselinePack.PackId == SelectedTargetPack.PackId)
      {
        System.Windows.MessageBox.Show("Baseline and target packs must be different.", "Invalid Selection",
          System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        return;
      }

      if (string.IsNullOrWhiteSpace(BundleRoot) || !Directory.Exists(BundleRoot))
      {
        System.Windows.MessageBox.Show("Bundle root path is invalid or does not exist.", "Invalid Bundle",
          System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        return;
      }

      _report = await _rebaseService.RebaseAnswersAsync(
        BundleRoot,
        SelectedBaselinePack.PackId,
        SelectedTargetPack.PackId,
        CancellationToken.None);

      TotalAnswers = _report.Actions.Count;

      var autoActions = _report.Actions
        .Where(a => !a.IsBlockingConflict && (a.ActionType == AnswerRebaseActionType.Carry || a.ActionType == AnswerRebaseActionType.CarryWithWarning))
        .ToList();
      AutoCarriedCount = autoActions.Count;
      AutoCarriedActions = autoActions.Select(ToDisplay).ToList();

      var reviewActions = _report.Actions
        .Where(a => a.IsBlockingConflict || a.RequiresReview || a.ActionType == AnswerRebaseActionType.ReviewRequired || a.ActionType == AnswerRebaseActionType.Remove || a.ActionType == AnswerRebaseActionType.Remap)
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
          .Select(a => $"{a.ControlKey}: {a.RecommendedAction}"));
      AnalysisStatus = BlockingConflictCount == 0
        ? "Answer rebase analysis complete. You can export artifacts or apply the rebase."
        : "Answer rebase analysis complete. Resolve blocking conflicts before applying rebase.";
      RecoveryGuidance = BuildRecoveryGuidance(BlockingConflictCount > 0);

      CurrentScreen = AnswerRebaseWizardScreen.Analysis;
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
      System.Windows.MessageBox.Show($"Answer rebase analysis failed:\n{ex.Message}", "Error",
        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
  }

  [RelayCommand]
  private void ApplyRebase()
  {
    try
    {
      if (_report == null) return;

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

      var sourceAnswers = _answerService.LoadAnswerFile(BundleRoot);
      var rebased = _rebaseService.ApplyAnswerRebase(_report, sourceAnswers);

      var rebasedPath = Path.Combine(BundleRoot, "Manual", "answers_rebased.json");
      Directory.CreateDirectory(Path.GetDirectoryName(rebasedPath)!);
      var json = JsonSerializer.Serialize(rebased, new JsonSerializerOptions
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      });
      File.WriteAllText(rebasedPath, json);

      RebasedFilePath = rebasedPath;
      AnalysisStatus = "Answer rebase apply completed successfully.";
      RecoveryGuidance = "Next action: validate the rebased answers during the next mission run. Use Overlay Rebase Wizard for overlay-level rebase alongside this answer rebase.";
      CurrentScreen = AnswerRebaseWizardScreen.Completion;
      StepText = "Step 3 of 3";
      OnPropertyChanged(nameof(ShowWelcome));
      OnPropertyChanged(nameof(ShowAnalysis));
      OnPropertyChanged(nameof(ShowCompletion));
      OnPropertyChanged(nameof(CanApplyRebase));
      OnPropertyChanged(nameof(CanExportReport));
    }
    catch (Exception ex)
    {
      System.Windows.MessageBox.Show($"Failed to apply answer rebase:\n{ex.Message}", "Error",
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

  partial void OnSelectedReviewActionChanged(AnswerRebaseActionDisplay? value)
  {
    OnPropertyChanged(nameof(HasSelectedReviewAction));
  }

  partial void OnCurrentScreenChanged(AnswerRebaseWizardScreen value)
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
      AnalysisStatus = "Answer rebase markdown report exported: " + dialog.FileName;
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
      AnalysisStatus = "Answer rebase JSON report exported: " + dialog.FileName;
    }
    catch (Exception ex)
    {
      System.Windows.MessageBox.Show($"Failed to export JSON report:\n{ex.Message}", "Export Failed",
        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
  }

  private string GenerateMarkdownReport(AnswerRebaseReport report)
  {
    var sb = new StringBuilder();
    sb.AppendLine("# Answer Rebase Report");
    sb.AppendLine();
    sb.AppendLine($"- **Bundle:** {report.BundleRoot}");
    sb.AppendLine($"- **Baseline:** {report.BaselinePackId}");
    sb.AppendLine($"- **Target:** {report.NewPackId}");
    sb.AppendLine($"- **Overall Confidence:** {report.OverallConfidence:P0}");
    sb.AppendLine($"- **Blocking conflicts:** {report.BlockingConflicts}");
    sb.AppendLine();
    sb.AppendLine("| Control | Action | Confidence | Requires Review | Blocking | Reason | Recommended Action |");
    sb.AppendLine("|---------|--------|------------|-----------------|----------|--------|--------------------|");

    foreach (var action in report.Actions)
    {
      sb.AppendLine($"| {action.ControlKey} | {action.ActionType} | {action.Confidence:P0} | {(action.RequiresReview ? "Yes" : "No")} | {(action.IsBlockingConflict ? "Yes" : "No")} | {action.Reason} | {action.RecommendedAction} |");
    }

    return sb.ToString();
  }

  private string BuildDefaultFileName(string extension)
  {
    var baseline = SelectedBaselinePack?.Name ?? SelectedBaselinePack?.PackId ?? "baseline";
    var target = SelectedTargetPack?.Name ?? SelectedTargetPack?.PackId ?? "target";
    return $"answer_rebase_{SanitizeFileNamePart(baseline)}_to_{SanitizeFileNamePart(target)}.{extension}";
  }

  private static string BuildRecoveryGuidance(bool hasBlockingConflicts)
  {
    if (hasBlockingConflicts)
    {
      return "Required artifacts: export Markdown and JSON rebase reports before sign-off. Next action: review removed or high-impact controls in the Manual Check Wizard, then rerun Analyze Rebase. Rollback guidance: keep the current answers.json until blocking conflicts are resolved.";
    }

    return "Required artifacts: export Markdown and JSON rebase reports for release evidence. Next action: apply rebase and validate rebased answers during the next mission run. Rollback guidance: retain the original answers.json until rebased output is validated.";
  }

  private static string SanitizeFileNamePart(string value)
  {
    var invalidChars = Path.GetInvalidFileNameChars();
    var cleaned = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
  }

  private static AnswerRebaseActionDisplay ToDisplay(AnswerRebaseAction action)
  {
    return new AnswerRebaseActionDisplay
    {
      ControlId = action.ControlKey,
      Action = action.ActionType.ToString(),
      Confidence = action.Confidence,
      ConfidenceDisplay = $"{action.Confidence:P0}",
      Reason = action.Reason,
      RecommendedAction = action.RecommendedAction,
      IsBlockingConflict = action.IsBlockingConflict,
      ExistingStatus = action.OriginalAnswer?.Status ?? "(none)",
      ChangesSummary = action.FieldChanges.Count > 0
        ? string.Join(", ", action.FieldChanges.Select(f => f.FieldName))
        : "(No changes)"
    };
  }

  public enum AnswerRebaseWizardScreen
  {
    Welcome,
    Analysis,
    Completion
  }
}

// Display model for answer rebase actions
public class AnswerRebaseActionDisplay
{
  public string ControlId { get; set; } = string.Empty;
  public string Action { get; set; } = string.Empty;
  public double Confidence { get; set; }
  public string ConfidenceDisplay { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string RecommendedAction { get; set; } = string.Empty;
  public bool IsBlockingConflict { get; set; }
  public string ExistingStatus { get; set; } = string.Empty;
  public string ChangesSummary { get; set; } = string.Empty;
}

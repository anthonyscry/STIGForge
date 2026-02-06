using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using static STIGForge.Core.Services.OverlayRebaseService;

namespace STIGForge.App.ViewModels;

public partial class RebaseWizardViewModel : ObservableObject
{
  private readonly IControlRepository _controls;
  private readonly IOverlayRepository _overlays;
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
  [ObservableProperty] private List<RebaseActionDisplay> _autoRebasedActions = new();
  [ObservableProperty] private List<RebaseActionDisplay> _needsReviewActions = new();
  [ObservableProperty] private RebaseActionDisplay? _selectedReviewAction;
  public bool HasSelectedReviewAction => SelectedReviewAction != null;

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
    List<ContentPack> availablePacks)
  {
    _controls = controls;
    _overlays = overlays;
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
      var diffService = new BaselineDiffService(_controls);
      var rebaseService = new OverlayRebaseService(_overlays, diffService);
      _report = await rebaseService.RebaseOverlayAsync(
        SelectedOverlay.OverlayId,
        SelectedBaselinePack.PackId,
        SelectedTargetPack.PackId,
        CancellationToken.None);

      // Convert report to display models
      TotalOverrides = _report.Actions.Count;
      
      var autoActions = _report.Actions
        .Where(a => a.ActionType == RebaseActionType.Keep || a.ActionType == RebaseActionType.KeepWithWarning)
        .ToList();
      AutoRebasedCount = autoActions.Count;
      AutoRebasedActions = autoActions.Select(ToDisplay).ToList();

      var reviewActions = _report.Actions
        .Where(a => a.ActionType == RebaseActionType.ReviewRequired || a.ActionType == RebaseActionType.Remove || a.ActionType == RebaseActionType.Remap)
        .ToList();
      NeedsReviewCount = reviewActions.Count;
      NeedsReviewActions = reviewActions.Select(ToDisplay).ToList();

      CurrentScreen = WizardScreen.Analysis;
      StepText = "Step 2 of 3";
      OnPropertyChanged(nameof(ShowWelcome));
      OnPropertyChanged(nameof(ShowAnalysis));
      OnPropertyChanged(nameof(ShowCompletion));
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

      // Apply rebase using the overlay from the original
      // Keep overrides that passed rebase analysis
      var keptOverrides = SelectedOverlay.Overrides
        .Where(ovr =>
        {
          var key = ovr.RuleId ?? ovr.VulnId ?? "";
          return _report.Actions.Any(a => 
            a.OriginalControlKey == key && 
            (a.ActionType == RebaseActionType.Keep || a.ActionType == RebaseActionType.KeepWithWarning));
        })
        .ToList();

      var rebasedOverlay = new Overlay
      {
        OverlayId = Guid.NewGuid().ToString("N"),
        Name = $"{SelectedOverlay.Name} (rebased)",
        UpdatedAt = DateTimeOffset.UtcNow,
        Overrides = keptOverrides,
        PowerStigOverrides = SelectedOverlay.PowerStigOverrides
      };

      await _overlays.SaveAsync(rebasedOverlay, CancellationToken.None);

      NewOverlayId = rebasedOverlay.OverlayId;
      CurrentScreen = WizardScreen.Completion;
      StepText = "Step 3 of 3";
      OnPropertyChanged(nameof(ShowWelcome));
      OnPropertyChanged(nameof(ShowAnalysis));
      OnPropertyChanged(nameof(ShowCompletion));
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

  private static RebaseActionDisplay ToDisplay(RebaseAction action)
  {
    return new RebaseActionDisplay
    {
      ControlId = action.OriginalControlKey,
      Action = action.ActionType.ToString(),
      Confidence = action.Confidence,
      ConfidenceDisplay = $"{action.Confidence:P0}",
      Reason = action.Reason,
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
  public string ChangesSummary { get; set; } = string.Empty;
}

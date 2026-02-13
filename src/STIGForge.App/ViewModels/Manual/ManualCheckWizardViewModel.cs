using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Media;
using BundlePaths = STIGForge.Core.Constants.BundlePaths;
using ControlStatusStrings = STIGForge.Core.Constants.ControlStatus;
using PackTypes = STIGForge.Core.Constants.PackTypes;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Evidence;

namespace STIGForge.App.ViewModels;

public partial class ManualCheckWizardViewModel : ObservableObject
{
  private readonly string _bundleRoot;
  private readonly List<ControlRecord> _controls;
  private readonly ManualAnswerService _answerService;
  private readonly ControlAnnotationService _annotationService;
  private readonly EvidenceAutopilot _evidenceAutopilot;
  private int _currentIndex = -1;

  [ObservableProperty] private WizardScreen _currentScreen = WizardScreen.Welcome;
  [ObservableProperty] private string _bundleInfo = string.Empty;
  [ObservableProperty] private int _totalControls;
  [ObservableProperty] private int _answeredControls;
  [ObservableProperty] private int _passCount;
  [ObservableProperty] private int _failCount;
  [ObservableProperty] private int _notApplicableCount;
  [ObservableProperty] private string _progressText = string.Empty;
  [ObservableProperty] private string _completionText = string.Empty;
  [ObservableProperty] private double _progressPercentage;
  [ObservableProperty] private string _currentStigGroup = string.Empty;

  // Current control display
  [ObservableProperty] private string _currentControlId = string.Empty;
  [ObservableProperty] private string _currentTitle = string.Empty;
  [ObservableProperty] private string _currentSeverity = string.Empty;
  [ObservableProperty] private Brush _severityColor = Brushes.Gray;
  [ObservableProperty] private string _currentCheckText = string.Empty;
  [ObservableProperty] private string _currentFixText = string.Empty;

  // Answer inputs
  [ObservableProperty] private bool _isPass;
  [ObservableProperty] private bool _isFail;
  [ObservableProperty] private bool _isNotApplicable;
  [ObservableProperty] private bool _isNotReviewed = true;
  [ObservableProperty] private string _answerReason = string.Empty;
  [ObservableProperty] private string _answerComment = string.Empty;
  [ObservableProperty] private string _controlNotes = string.Empty;
  [ObservableProperty] private string _evidenceStatus = string.Empty;

  // Screen visibility
  public bool ShowWelcome => CurrentScreen == WizardScreen.Welcome;
  public bool ShowReview => CurrentScreen == WizardScreen.Review;
  public bool ShowCompletion => CurrentScreen == WizardScreen.Completion;
  public bool HasUnansweredControls => _controls.Count > 0;
  public bool AllControlsAnswered => _controls.Count == 0;

  public ManualCheckWizardViewModel(string bundleRoot, List<ControlRecord> manualControls, ManualAnswerService answerService, ControlAnnotationService annotationService)
  {
    _bundleRoot = bundleRoot;
    _answerService = answerService;
    _annotationService = annotationService;
    _evidenceAutopilot = new EvidenceAutopilot(Path.Combine(bundleRoot, BundlePaths.EvidenceDirectory));
    
    // Get unanswered controls
    _controls = _answerService.GetUnansweredControls(bundleRoot, manualControls);
    
    LoadStats();
    UpdateBundleInfo();
  }

  [RelayCommand]
  private void StartWizard()
  {
    if (_controls.Count == 0) return;
    
    _currentIndex = 0;
    CurrentScreen = WizardScreen.Review;
    LoadCurrentControl();
    OnPropertyChanged(nameof(ShowWelcome));
    OnPropertyChanged(nameof(ShowReview));
    OnPropertyChanged(nameof(ShowCompletion));
  }

  [RelayCommand]
  private void PreviousControl()
  {
    if (_currentIndex > 0)
    {
      _currentIndex--;
      LoadCurrentControl();
    }
  }

  [RelayCommand]
  private void SkipControl()
  {
    if (_currentIndex < _controls.Count - 1)
    {
      _currentIndex++;
      LoadCurrentControl();
    }
    else
    {
      ShowCompletionScreen();
    }
  }

  [RelayCommand]
  private void SaveAndNext()
  {
    if (_currentIndex < 0 || _currentIndex >= _controls.Count)
      return;

    var selectedStatus = GetSelectedStatus();
    if (_answerService.RequiresReason(selectedStatus) && string.IsNullOrWhiteSpace(AnswerReason))
    {
      EvidenceStatus = "Reason is required for Fail and NotApplicable decisions.";
      return;
    }

    // Save current answer
    var control = _controls[_currentIndex];
    var answer = new ManualAnswer
    {
      RuleId = control.ExternalIds.RuleId,
      VulnId = control.ExternalIds.VulnId,
      Status = selectedStatus,
      Reason = string.IsNullOrWhiteSpace(AnswerReason) ? null : AnswerReason,
      Comment = string.IsNullOrWhiteSpace(AnswerComment) ? null : AnswerComment
    };

    _answerService.SaveAnswer(_bundleRoot, answer, requireReasonForDecision: true);

    var annotationRuleId = GetAnnotationRuleId(control);
    if (!string.IsNullOrWhiteSpace(ControlNotes) && !string.IsNullOrWhiteSpace(annotationRuleId))
      _annotationService.SaveNotes(_bundleRoot, annotationRuleId, control.ExternalIds.VulnId, ControlNotes);

    EvidenceStatus = string.Empty;

    // Remove from unanswered list
    _controls.RemoveAt(_currentIndex);

    // Update stats
    LoadStats();

    // Move to next or show completion
    if (_controls.Count == 0 || _currentIndex >= _controls.Count)
    {
      ShowCompletionScreen();
    }
    else
    {
      LoadCurrentControl();
    }
  }

  [RelayCommand]
  private async Task CollectEvidence()
  {
    if (_currentIndex < 0 || _currentIndex >= _controls.Count)
      return;

    try
    {
      var control = _controls[_currentIndex];
      EvidenceStatus = "Collecting evidence...";

      var result = await _evidenceAutopilot.CollectEvidenceAsync(control, CancellationToken.None);
      var controlDir = GetControlEvidenceDirectory(control);

      var status = "Collected " + result.EvidenceFiles.Count + " file(s).";
      if (result.Errors.Count > 0)
      {
        status += " Errors: " + result.Errors.Count + ".";
        if (result.EvidenceFiles.Count == 0)
          status += " " + string.Join("; ", result.Errors.Take(3));
      }

      EvidenceStatus = status + " Folder: " + controlDir;
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Evidence collection failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void ExportAnswers()
  {
    try
    {
      var sfd = new Microsoft.Win32.SaveFileDialog
      {
        Filter = "JSON Answer Files (*.json)|*.json|All Files (*.*)|*.*",
        Title = "Export answers to file",
        FileName = "manual_answers.json"
      };

      if (sfd.ShowDialog() != true) return;

      _answerService.ExportAnswerFile(_bundleRoot, sfd.FileName);
      EvidenceStatus = "Exported answers to " + sfd.FileName;
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Export failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void ImportAnswers()
  {
    try
    {
      var ofd = new Microsoft.Win32.OpenFileDialog
      {
        Filter = "JSON Answer Files (*.json)|*.json|All Files (*.*)|*.*",
        Title = "Import answers from file"
      };

      if (ofd.ShowDialog() != true) return;

      var count = _answerService.ImportAnswerFile(_bundleRoot, ofd.FileName, overwriteExisting: false);
      LoadStats();
      EvidenceStatus = $"Imported {count} answer(s) from {System.IO.Path.GetFileName(ofd.FileName)}";
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Import failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void CloseWizard()
  {
    // Will be handled by window close
  }

  private void LoadCurrentControl()
  {
    if (_currentIndex < 0 || _currentIndex >= _controls.Count) return;

    var control = _controls[_currentIndex];
    CurrentControlId = control.ExternalIds.VulnId ?? control.ExternalIds.RuleId ?? "Unknown";
    CurrentTitle = control.Title;
    CurrentSeverity = control.Severity;
    SeverityColor = GetSeverityBrush(control.Severity);
    CurrentCheckText = control.CheckText ?? "No check text available.";
    CurrentFixText = control.FixText ?? "No fix text available.";
    CurrentStigGroup = control.Revision?.PackName ?? "Unknown";

    // Reset answer inputs
    IsPass = false;
    IsFail = false;
    IsNotApplicable = false;
    IsNotReviewed = true;
    AnswerReason = string.Empty;
    AnswerComment = string.Empty;
    EvidenceStatus = string.Empty;
    var annotationRuleId = GetAnnotationRuleId(control);
    ControlNotes = string.IsNullOrWhiteSpace(annotationRuleId)
      ? string.Empty
      : _annotationService.GetNotes(_bundleRoot, annotationRuleId) ?? string.Empty;

    UpdateProgress();
  }

  private void LoadStats()
  {
    var allManualControls = LoadAllManualControls();
    var stats = _answerService.GetProgressStats(_bundleRoot, allManualControls);

    TotalControls = stats.TotalControls;
    AnsweredControls = stats.AnsweredControls;
    PassCount = stats.PassCount;
    FailCount = stats.FailCount;
    NotApplicableCount = stats.NotApplicableCount;

    CompletionText = $"{stats.PercentComplete:F1}% Complete";
    ProgressPercentage = stats.TotalControls > 0 
      ? (double)stats.AnsweredControls / stats.TotalControls * 100.0 
      : 0;
  }

  private List<ControlRecord> LoadAllManualControls()
  {
    var allControls = PackControlsReader.Load(_bundleRoot);

    // Filter out ADMX/GPO/Template packs to match BuildManualScopeControls() in MainViewModel
    return allControls
      .Where(c =>
      {
        var packName = (c.Revision.PackName ?? string.Empty).Trim();
        return packName.IndexOf(PackTypes.Admx, StringComparison.OrdinalIgnoreCase) < 0
          && packName.IndexOf(PackTypes.Gpo, StringComparison.OrdinalIgnoreCase) < 0
          && packName.IndexOf(PackTypes.Template, StringComparison.OrdinalIgnoreCase) < 0;
      })
      .ToList();
  }

  private void UpdateBundleInfo()
  {
    var manifestPath = Path.Combine(_bundleRoot, BundlePaths.ManifestDirectory, "manifest.json");
    if (!File.Exists(manifestPath))
    {
      BundleInfo = "Bundle information not available (manifest.json missing).";
      return;
    }

    try
    {
      var json = File.ReadAllText(manifestPath);
      using var doc = System.Text.Json.JsonDocument.Parse(json);

      if (!TryGetPropertyCaseInsensitive(doc.RootElement, "run", out var runElement))
      {
        BundleInfo = "Bundle information not available (manifest missing run section).";
        return;
      }

      string? systemName = null;
      string? profileName = null;

      if (TryGetPropertyCaseInsensitive(runElement, "systemName", out var systemElement))
        systemName = systemElement.GetString();
      if (TryGetPropertyCaseInsensitive(runElement, "profileName", out var profileElement))
        profileName = profileElement.GetString();

      BundleInfo = $"System: {systemName ?? "Unknown"}\nProfile: {profileName ?? "Unknown"}";
    }
    catch (Exception ex)
    {
      BundleInfo = "Bundle information not available.";
      System.Diagnostics.Debug.WriteLine("[ManualCheckWizard] failed to read manifest: " + ex.Message);
    }
  }

  private static bool TryGetPropertyCaseInsensitive(System.Text.Json.JsonElement element, string propertyName, out System.Text.Json.JsonElement value)
  {
    if (element.TryGetProperty(propertyName, out value))
      return true;

    foreach (var prop in element.EnumerateObject())
    {
      if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
      {
        value = prop.Value;
        return true;
      }
    }

    value = default;
    return false;
  }

  private void UpdateProgress()
  {
    ProgressText = $"Control {_currentIndex + 1} of {_controls.Count}";
    ProgressPercentage = _controls.Count > 0 
      ? (double)(_currentIndex + 1) / _controls.Count * 100.0 
      : 0;
  }

  private void ShowCompletionScreen()
  {
    CurrentScreen = WizardScreen.Completion;
    LoadStats();
    OnPropertyChanged(nameof(ShowWelcome));
    OnPropertyChanged(nameof(ShowReview));
    OnPropertyChanged(nameof(ShowCompletion));
  }

  private string GetSelectedStatus()
  {
    if (IsPass) return ControlStatusStrings.Pass;
    if (IsFail) return ControlStatusStrings.Fail;
    if (IsNotApplicable) return ControlStatusStrings.NotApplicable;
    return ControlStatusStrings.Open;
  }

  private string GetControlEvidenceDirectory(ControlRecord control)
  {
    var controlId = control.ExternalIds.VulnId ?? control.ExternalIds.RuleId ?? control.ControlId;
    var safeId = string.Join("_", controlId.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(_bundleRoot, BundlePaths.EvidenceDirectory, "by_control", safeId);
  }

  private static string GetAnnotationRuleId(ControlRecord control)
  {
    if (!string.IsNullOrWhiteSpace(control.ExternalIds.RuleId))
      return control.ExternalIds.RuleId;

    return control.ExternalIds.VulnId ?? string.Empty;
  }

  private static Brush GetSeverityBrush(string severity)
  {
    return severity?.ToLowerInvariant() switch
    {
      "high" => Brushes.Red,
      "medium" => Brushes.Orange,
      "low" => Brushes.Gold,
      _ => Brushes.Gray
    };
  }
}

public enum WizardScreen
{
  Welcome,
  Review,
  Completion
}

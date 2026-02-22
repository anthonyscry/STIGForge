using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Windows.Media;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Evidence;

namespace STIGForge.App.ViewModels;

public partial class ManualCheckWizardViewModel : ObservableObject
{
  private readonly string _bundleRoot;
  private readonly List<ControlRecord> _controls;
  private readonly ManualAnswerService _answerService;
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
  [ObservableProperty] private string _evidenceStatus = string.Empty;

  // Screen visibility
  public bool ShowWelcome => CurrentScreen == WizardScreen.Welcome;
  public bool ShowReview => CurrentScreen == WizardScreen.Review;
  public bool ShowCompletion => CurrentScreen == WizardScreen.Completion;
  public bool HasUnansweredControls => _controls.Count > 0;
  public bool AllControlsAnswered => _controls.Count == 0;

  public ManualCheckWizardViewModel(string bundleRoot, List<ControlRecord> manualControls)
  {
    _bundleRoot = bundleRoot;
    _answerService = new ManualAnswerService();
    _evidenceAutopilot = new EvidenceAutopilot(Path.Combine(bundleRoot, "Evidence"));
    
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
        status += " Errors: " + result.Errors.Count + ".";

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
      var dialog = new SaveFileDialog
      {
        Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        DefaultExt = "json",
        FileName = $"answers_export_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.json"
      };

      if (dialog.ShowDialog() != true)
        return;

      var export = _answerService.ExportAnswers(_bundleRoot);
      _answerService.WriteExportFile(dialog.FileName, export);

      var count = export.Answers?.Answers?.Count ?? 0;
      EvidenceStatus = $"Exported {count} answers to {dialog.FileName}";
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
      var dialog = new OpenFileDialog
      {
        Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        DefaultExt = "json"
      };

      if (dialog.ShowDialog() != true)
        return;

      var import = _answerService.ReadExportFile(dialog.FileName);
      var result = _answerService.ImportAnswers(_bundleRoot, import);
      LoadStats();

      var status = $"Imported {result.Imported} answers, skipped {result.Skipped} already resolved.";
      if (result.SkippedControls.Count > 0)
        status += " Skipped controls: " + string.Join(", ", result.SkippedControls);

      EvidenceStatus = status;
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

    // Reset answer inputs
    IsPass = false;
    IsFail = false;
    IsNotApplicable = false;
    IsNotReviewed = true;
    AnswerReason = string.Empty;
    AnswerComment = string.Empty;
    EvidenceStatus = string.Empty;

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
  }

  private List<ControlRecord> LoadAllManualControls()
  {
    var controlsPath = Path.Combine(_bundleRoot, "Manifest", "pack_controls.json");
    if (!File.Exists(controlsPath)) return new List<ControlRecord>();

    var json = File.ReadAllText(controlsPath);
    var allControls = System.Text.Json.JsonSerializer.Deserialize<List<ControlRecord>>(json,
      new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
      ?? new List<ControlRecord>();

    return allControls.Where(c => c.IsManual).ToList();
  }

  private void UpdateBundleInfo()
  {
    var manifestPath = Path.Combine(_bundleRoot, "Manifest", "manifest.json");
    if (File.Exists(manifestPath))
    {
      try
      {
        var json = File.ReadAllText(manifestPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var systemName = doc.RootElement.GetProperty("run").GetProperty("systemName").GetString();
        var profileName = doc.RootElement.GetProperty("run").GetProperty("profileName").GetString();
        BundleInfo = $"System: {systemName}\nProfile: {profileName}";
      }
      catch
      {
        BundleInfo = "Bundle information not available.";
      }
    }
  }

  private void UpdateProgress()
  {
    var remaining = _controls.Count;
    var completed = TotalControls - remaining;
    ProgressText = $"Control {completed + 1} of {TotalControls}";
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
    if (IsPass) return "Pass";
    if (IsFail) return "Fail";
    if (IsNotApplicable) return "NotApplicable";
    return "Open";
  }

  private string GetControlEvidenceDirectory(ControlRecord control)
  {
    var controlId = control.ExternalIds.VulnId ?? control.ExternalIds.RuleId ?? control.ControlId;
    var safeId = string.Join("_", controlId.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(_bundleRoot, "Evidence", "by_control", safeId);
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

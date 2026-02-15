using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text.Json;
using STIGForge.Core.Models;
using STIGForge.Evidence;

namespace STIGForge.App;

public partial class MainViewModel
{
  [RelayCommand]
  private void BrowseEvidenceFile()
  {
    var ofd = new Microsoft.Win32.OpenFileDialog
    {
      Filter = "All Files (*.*)|*.*",
      Title = "Select evidence file"
    };

    if (ofd.ShowDialog() != true) return;
    EvidenceFilePath = ofd.FileName;
  }

  [RelayCommand]
  private void SaveEvidence()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        EvidenceStatus = "Select a bundle first.";
        return;
      }

      if (string.IsNullOrWhiteSpace(EvidenceRuleId))
      {
        EvidenceStatus = "RuleId is required.";
        return;
      }

      var type = ParseEvidenceType(EvidenceType);
      var request = new EvidenceWriteRequest
      {
        BundleRoot = BundleRoot,
        RuleId = EvidenceRuleId,
        Type = type,
        ContentText = string.IsNullOrWhiteSpace(EvidenceText) ? null : EvidenceText,
        SourceFilePath = string.IsNullOrWhiteSpace(EvidenceFilePath) ? null : EvidenceFilePath
      };

      var result = _evidence.WriteEvidence(request);
      EvidenceStatus = "Saved: " + result.EvidencePath;
      LastOutputPath = result.EvidencePath;
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Evidence save failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void SaveManualAnswer()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        EvidenceStatus = "Select a bundle first.";
        return;
      }

      if (SelectedManualControl == null)
      {
        EvidenceStatus = "Select a manual control.";
        return;
      }

      var normalizedStatus = _manualAnswerService.NormalizeStatus(ManualStatus);
      _manualAnswerService.ValidateReasonRequirement(normalizedStatus, ManualReason);

      var answer = new ManualAnswer
      {
        RuleId = SelectedManualControl.Control.ExternalIds.RuleId,
        VulnId = SelectedManualControl.Control.ExternalIds.VulnId,
        Status = normalizedStatus,
        Reason = string.IsNullOrWhiteSpace(ManualReason) ? null : ManualReason,
        Comment = string.IsNullOrWhiteSpace(ManualComment) ? null : ManualComment
      };

      _manualAnswerService.SaveAnswer(
        BundleRoot,
        answer,
        requireReasonForDecision: true,
        profileId: SelectedProfile?.ProfileId,
        packId: SelectedPack?.PackId);

      var path = Path.Combine(BundleRoot, "Manual", "answers.json");
      SelectedManualControl.Status = normalizedStatus;
      SelectedManualControl.Reason = ManualReason;
      SelectedManualControl.Comment = ManualComment;
      ManualStatus = normalizedStatus;

      OnPropertyChanged(nameof(ManualControls));
      UpdateManualSummary();

      EvidenceStatus = "Answer saved.";
      LastOutputPath = path;
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Save failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void UseSelectedControlForEvidence()
  {
    if (SelectedManualControl == null)
    {
      EvidenceStatus = "Select a manual control first.";
      return;
    }

    var rule = SelectedManualControl.Control.ExternalIds.RuleId;
    var vuln = SelectedManualControl.Control.ExternalIds.VulnId;
    EvidenceRuleId = !string.IsNullOrWhiteSpace(rule) ? rule : (vuln ?? string.Empty);
    EvidenceStatus = string.IsNullOrWhiteSpace(EvidenceRuleId)
      ? "Selected control has no RuleId/VulnId."
      : "Evidence target set: " + EvidenceRuleId;
  }

  [RelayCommand]
  private async Task CollectSelectedControlEvidence()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        EvidenceStatus = "Select a bundle first.";
        return;
      }

      if (SelectedManualControl == null)
      {
        EvidenceStatus = "Select a manual control first.";
        return;
      }

      var autopilot = new EvidenceAutopilot(Path.Combine(BundleRoot, "Evidence"));
      var result = await autopilot.CollectEvidenceAsync(SelectedManualControl.Control, CancellationToken.None);
      var folder = GetSelectedManualEvidenceFolder(SelectedManualControl.Control, BundleRoot);

      var outcome = "Collected " + result.EvidenceFiles.Count + " evidence file(s).";
      if (result.Errors.Count > 0)
        outcome += " Errors: " + result.Errors.Count + ".";

      EvidenceStatus = outcome + " Folder: " + folder;
      LastOutputPath = folder;
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Auto-collection failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void OpenSelectedControlEvidenceFolder()
  {
    if (string.IsNullOrWhiteSpace(BundleRoot) || SelectedManualControl == null)
    {
      EvidenceStatus = "Select a bundle and manual control first.";
      return;
    }

    var folder = GetSelectedManualEvidenceFolder(SelectedManualControl.Control, BundleRoot);
    if (!Directory.Exists(folder))
    {
      EvidenceStatus = "No evidence folder yet for selected control.";
      return;
    }

    try
    {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = folder,
        UseShellExecute = true
      });
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Failed to open evidence folder: " + ex.Message;
    }
  }

  [RelayCommand]
  private void LaunchManualWizard()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        StatusText = "Select a bundle first.";
        return;
      }

      var manualControls = ManualControls.Select(m => m.Control).ToList();
      var viewModel = new ViewModels.ManualCheckWizardViewModel(BundleRoot, manualControls);
      var wizard = new Views.ManualCheckWizard(viewModel);
      
      wizard.Closed += (s, e) =>
      {
        // Refresh manual controls after wizard closes
        LoadManualControlsAsync();
      };
      
      wizard.ShowDialog();
    }
    catch (Exception ex)
    {
      StatusText = "Failed to launch wizard: " + ex.Message;
    }
  }

  partial void OnSelectedManualControlChanged(ManualControlItem? value)
  {
    if (value == null) return;
    ManualStatus = _manualAnswerService.NormalizeStatus(value.Status);
    ManualReason = value.Reason ?? string.Empty;
    ManualComment = value.Comment ?? string.Empty;
  }

  partial void OnManualFilterTextChanged(string value)
  {
    RefreshManualView();
  }

  partial void OnManualStatusFilterChanged(string value)
  {
    RefreshManualView();
  }

  partial void OnManualCatFilterChanged(string value)
  {
    RefreshManualView();
  }

  private async void LoadManualControlsAsync()
  {
    System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
      ManualControls.Clear();
    });
    if (string.IsNullOrWhiteSpace(BundleRoot)) return;

    var controlsPath = Path.Combine(BundleRoot, "Manifest", "pack_controls.json");
    if (!File.Exists(controlsPath)) return;

    var json = await Task.Run(() => File.ReadAllText(controlsPath));
    var controls = JsonSerializer.Deserialize<List<ControlRecord>>(json,
      new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ControlRecord>();

    var answers = LoadAnswerFile();

    var manualItems = new List<ManualControlItem>();
    foreach (var c in controls.Where(c => c.IsManual))
    {
      var item = new ManualControlItem(c);
      var ans = FindAnswer(answers, c);
      if (ans != null)
      {
        item.Status = _manualAnswerService.NormalizeStatus(ans.Status);
        item.Reason = ans.Reason;
        item.Comment = ans.Comment;
      }
      manualItems.Add(item);
    }

    System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
      foreach (var item in manualItems)
        ManualControls.Add(item);
      UpdateManualSummary();
      ManualControlsView.Refresh();
    });
  }

  private void ConfigureManualView()
  {
    var view = ManualControlsView;
    view.Filter = o =>
    {
      if (o is not ManualControlItem item) return false;

      var statusFilter = ManualStatusFilter ?? "All";
      if (!string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(item.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
        return false;

      var catFilter = ManualCatFilter ?? "All";
      if (!string.Equals(catFilter, "All", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(item.CatLevel, catFilter, StringComparison.OrdinalIgnoreCase))
        return false;

      var text = ManualFilterText?.Trim();
      if (string.IsNullOrWhiteSpace(text)) return true;

      return Contains(item.Control.ExternalIds.RuleId, text)
        || Contains(item.Control.ExternalIds.VulnId, text)
        || Contains(item.Control.Title, text)
        || Contains(item.StigGroup, text)
        || Contains(item.CatLevel, text)
        || Contains(item.Reason, text)
        || Contains(item.Comment, text);
    };

    view.SortDescriptions.Clear();
    view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(ManualControlItem.RuleId), System.ComponentModel.ListSortDirection.Ascending));

    RefreshManualView();
  }

  private void RefreshManualView()
  {
    ManualControlsView.Refresh();
    UpdateManualSummary();
  }

  private void UpdateManualSummary()
  {
    var total = ManualControls.Count;
    var pass = ManualControls.Count(x => string.Equals(x.Status, "Pass", StringComparison.OrdinalIgnoreCase));
    var fail = ManualControls.Count(x => string.Equals(x.Status, "Fail", StringComparison.OrdinalIgnoreCase));
    var na = ManualControls.Count(x => string.Equals(x.Status, "NotApplicable", StringComparison.OrdinalIgnoreCase));
    var open = ManualControls.Count(x => string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase));

    ManualSummary = $"Total: {total} | Pass: {pass} | Fail: {fail} | NA: {na} | Open: {open}";
  }

  private AnswerFile LoadAnswerFile()
  {
    return _manualAnswerService.LoadAnswerFile(BundleRoot);
  }

  private static ManualAnswer? FindAnswer(AnswerFile file, ControlRecord control)
  {
    return file.Answers.FirstOrDefault(a =>
      (!string.IsNullOrWhiteSpace(a.RuleId) && string.Equals(a.RuleId, control.ExternalIds.RuleId, StringComparison.OrdinalIgnoreCase)) ||
      (!string.IsNullOrWhiteSpace(a.VulnId) && string.Equals(a.VulnId, control.ExternalIds.VulnId, StringComparison.OrdinalIgnoreCase)));
  }

  private static EvidenceArtifactType ParseEvidenceType(string value)
  {
    return Enum.TryParse<EvidenceArtifactType>(value, true, out var t) ? t : EvidenceArtifactType.Other;
  }

  private static bool Contains(string? source, string value)
  {
    return !string.IsNullOrWhiteSpace(source)
      && source.Contains(value, StringComparison.OrdinalIgnoreCase);
  }

  private static string GetSelectedManualEvidenceFolder(ControlRecord control, string bundleRoot)
  {
    var controlId = control.ExternalIds.VulnId ?? control.ExternalIds.RuleId ?? control.ControlId;
    var safeId = string.Join("_", controlId.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(bundleRoot, "Evidence", "by_control", safeId);
  }
}

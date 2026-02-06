using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Data;
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

      var file = LoadAnswerFile();
      if (SelectedProfile != null) file.ProfileId = SelectedProfile.ProfileId;
      if (SelectedPack != null) file.PackId = SelectedPack.PackId;
      if (file.CreatedAt == default) file.CreatedAt = DateTimeOffset.Now;
      var answer = FindAnswer(file, SelectedManualControl.Control) ?? new ManualAnswer
      {
        RuleId = SelectedManualControl.Control.ExternalIds.RuleId,
        VulnId = SelectedManualControl.Control.ExternalIds.VulnId
      };

      answer.Status = ManualStatus;
      answer.Reason = string.IsNullOrWhiteSpace(ManualReason) ? null : ManualReason;
      answer.Comment = string.IsNullOrWhiteSpace(ManualComment) ? null : ManualComment;
      answer.UpdatedAt = DateTimeOffset.Now;

      if (!file.Answers.Contains(answer))
        file.Answers.Add(answer);

      var path = Path.Combine(BundleRoot, "Manual", "answers.json");
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
      File.WriteAllText(path, json, Encoding.UTF8);

      SelectedManualControl.Status = ManualStatus;
      SelectedManualControl.Reason = ManualReason;
      SelectedManualControl.Comment = ManualComment;
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
        LoadManualControls();
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
    ManualStatus = value.Status;
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

  private void LoadManualControls()
  {
    ManualControls.Clear();
    if (string.IsNullOrWhiteSpace(BundleRoot)) return;

    var controlsPath = Path.Combine(BundleRoot, "Manifest", "pack_controls.json");
    if (!File.Exists(controlsPath)) return;

    var json = File.ReadAllText(controlsPath);
    var controls = JsonSerializer.Deserialize<List<ControlRecord>>(json,
      new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ControlRecord>();

    var answers = LoadAnswerFile();

    foreach (var c in controls.Where(c => c.IsManual))
    {
      var item = new ManualControlItem(c);
      var ans = FindAnswer(answers, c);
      if (ans != null)
      {
        item.Status = ans.Status;
        item.Reason = ans.Reason;
        item.Comment = ans.Comment;
      }
      ManualControls.Add(item);
    }

    UpdateManualSummary();
    ManualControlsView.Refresh();
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

      var text = ManualFilterText?.Trim();
      if (string.IsNullOrWhiteSpace(text)) return true;

      return Contains(item.Control.ExternalIds.RuleId, text)
        || Contains(item.Control.ExternalIds.VulnId, text)
        || Contains(item.Control.Title, text)
        || Contains(item.Reason, text)
        || Contains(item.Comment, text);
    };

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
    var path = Path.Combine(BundleRoot, "Manual", "answers.json");
    if (!File.Exists(path)) return new AnswerFile();

    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<AnswerFile>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    }) ?? new AnswerFile();
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
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlStatusStrings = STIGForge.Core.Constants.ControlStatus;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.App.ViewModels;

public partial class BulkDispositionViewModel : ObservableObject
{
  private readonly string _bundleRoot;
  private readonly ManualAnswerService _answerService;
  private readonly ObservableCollection<MainViewModel.ManualControlItem> _controls;

  [ObservableProperty] private string _selectedStigGroup = string.Empty;
  [ObservableProperty] private string _selectedStatus = ControlStatusStrings.Pass;
  [ObservableProperty] private string _reason = string.Empty;
  [ObservableProperty] private string _resultMessage = string.Empty;
  [ObservableProperty] private int _matchingCount;

  public ObservableCollection<string> StigGroups { get; } = new();
  public List<string> Statuses { get; } =
  [
    ControlStatusStrings.Pass,
    ControlStatusStrings.Fail,
    ControlStatusStrings.NotApplicable,
    ControlStatusStrings.Open
  ];

  public bool DialogResult { get; private set; }
  public int AppliedCount { get; private set; }

  public BulkDispositionViewModel(
    string bundleRoot,
    ManualAnswerService answerService,
    ObservableCollection<MainViewModel.ManualControlItem> controls)
  {
    _bundleRoot = bundleRoot;
    _answerService = answerService;
    _controls = controls;

    var groups = controls
      .Select(c => c.StigGroup)
      .Where(g => !string.IsNullOrWhiteSpace(g))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(g => g, StringComparer.OrdinalIgnoreCase);

    foreach (var group in groups)
      StigGroups.Add(group);

    if (StigGroups.Count > 0)
      SelectedStigGroup = StigGroups[0];
  }

  partial void OnSelectedStigGroupChanged(string value)
  {
    MatchingCount = _controls.Count(c => string.Equals(c.StigGroup, value, StringComparison.OrdinalIgnoreCase));
  }

  [RelayCommand]
  private void Apply()
  {
    if (string.IsNullOrWhiteSpace(SelectedStigGroup))
      return;

    if (_answerService.RequiresReason(SelectedStatus) && string.IsNullOrWhiteSpace(Reason))
    {
      ResultMessage = "Reason is required for Fail and NotApplicable.";
      return;
    }

    var matching = _controls
      .Where(c => string.Equals(c.StigGroup, SelectedStigGroup, StringComparison.OrdinalIgnoreCase))
      .ToList();

    var count = 0;
    foreach (var control in matching)
    {
      var answer = new ManualAnswer
      {
        RuleId = control.RuleId,
        VulnId = control.VulnId,
        Status = SelectedStatus,
        Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason
      };

      _answerService.SaveAnswer(_bundleRoot, answer, requireReasonForDecision: true);
      control.Status = SelectedStatus;
      control.Reason = Reason;
      count++;
    }

    AppliedCount = count;
    DialogResult = true;
    ResultMessage = $"Applied {SelectedStatus} to {count} control(s).";
  }
}

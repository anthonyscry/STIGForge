using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.App;

/// <summary>
/// View model for the overlay editor - allows creating and editing overlays with control overrides.
/// </summary>
public partial class OverlayEditorViewModel : ObservableObject
{
    private readonly IOverlayRepository _overlayRepository;
    private readonly IControlRepository? _controlRepository;

    [ObservableProperty]
    private string _overlayId = string.Empty;

    [ObservableProperty]
    private string _overlayName = string.Empty;

    [ObservableProperty]
    private string _overlayStatus = string.Empty;

    [ObservableProperty]
    private SelectableRuleItem? _selectedRule;

    [ObservableProperty]
    private ControlStatus _selectedRuleStatus = ControlStatus.NotApplicable;

    [ObservableProperty]
    private string _selectedRuleReason = string.Empty;

    [ObservableProperty]
    private string _selectedRuleNotes = string.Empty;

    public ObservableCollection<PowerStigOverride> PowerStigOverrides { get; } = new();
    public ObservableCollection<ControlOverride> ControlOverrides { get; } = new();
    public ObservableCollection<SelectableRuleItem> AvailableRules { get; } = new();

    public OverlayEditorViewModel(IOverlayRepository overlayRepository, IControlRepository? controlRepository)
    {
        _overlayRepository = overlayRepository;
        _controlRepository = controlRepository;
    }

    public async Task LoadAvailableRulesAsync(IEnumerable<string> packIds, CancellationToken ct)
    {
        if (_controlRepository == null) return;

        AvailableRules.Clear();
        var allRules = new List<SelectableRuleItem>();

        foreach (var packId in packIds)
        {
            var controls = await _controlRepository.ListControlsAsync(packId, ct);
            foreach (var control in controls)
            {
                allRules.Add(new SelectableRuleItem
                {
                    ControlId = control.ControlId,
                    RuleId = control.ExternalIds?.RuleId ?? string.Empty,
                    VulnId = control.ExternalIds?.VulnId ?? string.Empty,
                    Title = control.Title,
                    Severity = control.Severity
                });
            }
        }

        // Sort case-insensitively by RuleId first, then VulnId
        var sorted = allRules.OrderBy(r => string.IsNullOrEmpty(r.RuleId) ? r.VulnId : r.RuleId, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in sorted)
            AvailableRules.Add(rule);
    }

    [RelayCommand]
    private void AddControlOverride()
    {
        if (SelectedRule == null) return;

        // Check for duplicates
        var ruleId = SelectedRule.RuleId;
        var vulnId = SelectedRule.VulnId;

        if (!string.IsNullOrEmpty(ruleId) && ControlOverrides.Any(o => o.RuleId == ruleId))
        {
            OverlayStatus = $"Rule {ruleId} is already in override list.";
            return;
        }
        if (!string.IsNullOrEmpty(vulnId) && string.IsNullOrEmpty(ruleId) && ControlOverrides.Any(o => o.VulnId == vulnId))
        {
            OverlayStatus = $"Vuln {vulnId} is already in override list.";
            return;
        }

        ControlOverrides.Add(new ControlOverride
        {
            RuleId = ruleId,
            VulnId = vulnId,
            StatusOverride = SelectedRuleStatus,
            NaReason = SelectedRuleReason,
            Notes = SelectedRuleNotes
        });

        // Reset selection
        SelectedRule = null;
        SelectedRuleStatus = ControlStatus.NotApplicable;
        SelectedRuleReason = string.Empty;
        SelectedRuleNotes = string.Empty;
    }

    [RelayCommand]
    private void RemoveControlOverride(ControlOverride? item)
    {
        if (item == null) return;
        ControlOverrides.Remove(item);
        OverlayStatus = "Removed control override.";
    }

    [RelayCommand]
    private async Task SaveOverlay()
    {
        if (string.IsNullOrEmpty(OverlayId))
            OverlayId = Guid.NewGuid().ToString();

        var overlay = new Overlay
        {
            OverlayId = OverlayId,
            Name = OverlayName,
            Overrides = ControlOverrides.ToList(),
            PowerStigOverrides = PowerStigOverrides.ToList()
        };

        await _overlayRepository.SaveAsync(overlay, CancellationToken.None);
        OverlayStatus = "Overlay saved successfully.";
    }
}

/// <summary>
/// Represents a rule available for selection in the overlay editor.
/// </summary>
public class SelectableRuleItem
{
    public string ControlId { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string VulnId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;

    public string DisplayText
    {
        get
        {
            var id = !string.IsNullOrEmpty(RuleId) ? RuleId : VulnId;
            if (string.IsNullOrEmpty(id))
                return Title;
            return $"[{Severity}] {id} - {Title}";
        }
    }
}

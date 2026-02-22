using CommunityToolkit.Mvvm.Input;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.App;
using Xunit;

namespace STIGForge.UnitTests.Views;

public sealed class OverlayEditorViewModelTests
{
  private readonly MockOverlayRepository _mockOverlayRepo = new();
  private readonly MockControlRepository _mockControlRepo = new();

  [Fact]
  public void Constructor_InitializesProperties()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);

    Assert.Empty(vm.PowerStigOverrides);
    Assert.Empty(vm.ControlOverrides);
    Assert.Empty(vm.AvailableRules);
    Assert.Null(vm.SelectedRule);
    Assert.Equal(ControlStatus.NotApplicable, vm.SelectedRuleStatus);
    Assert.Empty(vm.SelectedRuleReason);
    Assert.Empty(vm.SelectedRuleNotes);
  }

  [Fact]
  public void Constructor_NullControlRepository_DoesNotThrow()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, null);

    Assert.NotNull(vm);
  }

  [Fact]
  public async Task LoadAvailableRulesAsync_PopulatesRulesWithDeterministicSorting()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);
    var packId = "test-pack-1";

    // Setup mock controls with mixed RuleId/VulnId and different cases
    _mockControlRepo.ControlsByPack[packId] = new List<ControlRecord>
    {
      new() { ControlId = "C1", SourcePackId = packId, ExternalIds = new() { RuleId = "SV-100001", VulnId = "V-100001" }, Title = "Rule C", Severity = "high" },
      new() { ControlId = "C2", SourcePackId = packId, ExternalIds = new() { RuleId = "sv-100002", VulnId = "v-100002" }, Title = "Rule A", Severity = "medium" },
      new() { ControlId = "C3", SourcePackId = packId, ExternalIds = new() { RuleId = "SV-100003", VulnId = "V-100003" }, Title = "Rule B", Severity = "low" },
      new() { ControlId = "C4", SourcePackId = packId, ExternalIds = new() { VulnId = "V-100004" }, Title = "Vuln Only", Severity = "high" },
      new() { ControlId = "C5", SourcePackId = packId, ExternalIds = new() { RuleId = "SV-100005" }, Title = "Rule Only", Severity = "medium" }
    };

    await vm.LoadAvailableRulesAsync(new[] { packId }, CancellationToken.None);

    Assert.Equal(5, vm.AvailableRules.Count);

    // Verify case-insensitive sorting by RuleId, then VulnId
    Assert.Equal("sv-100002", vm.AvailableRules[0].RuleId);
    Assert.Equal("SV-100001", vm.AvailableRules[1].RuleId);
    Assert.Equal("SV-100003", vm.AvailableRules[2].RuleId);
    Assert.Equal("SV-100005", vm.AvailableRules[3].RuleId);
    Assert.Equal("V-100004", vm.AvailableRules[4].VulnId);
  }

  [Fact]
  public async Task LoadAvailableRulesAsync_MultiplePacks_MergesAndSorts()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);

    _mockControlRepo.ControlsByPack["pack1"] = new List<ControlRecord>
    {
      new() { ControlId = "C1", SourcePackId = "pack1", ExternalIds = new() { RuleId = "SV-100003" }, Title = "Pack1 Rule", Severity = "high" }
    };
    _mockControlRepo.ControlsByPack["pack2"] = new List<ControlRecord>
    {
      new() { ControlId = "C2", SourcePackId = "pack2", ExternalIds = new() { RuleId = "SV-100001" }, Title = "Pack2 Rule", Severity = "medium" }
    };

    await vm.LoadAvailableRulesAsync(new[] { "pack1", "pack2" }, CancellationToken.None);

    Assert.Equal(2, vm.AvailableRules.Count);
    Assert.Equal("SV-100001", vm.AvailableRules[0].RuleId);
    Assert.Equal("SV-100003", vm.AvailableRules[1].RuleId);
  }

  [Fact]
  public async Task LoadAvailableRulesAsync_NoControls_DoesNotThrow()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);

    await vm.LoadAvailableRulesAsync(Array.Empty<string>(), CancellationToken.None);

    Assert.Empty(vm.AvailableRules);
  }

  [Fact]
  public async Task LoadAvailableRulesAsync_NullControlRepository_DoesNotThrow()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, null);

    await vm.LoadAvailableRulesAsync(new[] { "pack1" }, CancellationToken.None);

    Assert.Empty(vm.AvailableRules);
  }

  [Fact]
  public void SelectableRuleItem_DisplayText_FormatsCorrectly()
  {
    var ruleWithRuleId = new SelectableRuleItem { RuleId = "SV-100001", VulnId = "V-100001", Title = "Test Rule", Severity = "high" };
    var ruleWithVulnIdOnly = new SelectableRuleItem { VulnId = "V-100002", Title = "Vuln Only", Severity = "medium" };
    var ruleWithNoIds = new SelectableRuleItem { Title = "No IDs", Severity = "low" };

    Assert.Equal("[high] SV-100001 - Test Rule", ruleWithRuleId.DisplayText);
    Assert.Equal("[medium] V-100002 - Vuln Only", ruleWithVulnIdOnly.DisplayText);
    Assert.Equal("No IDs", ruleWithNoIds.DisplayText);
  }

  [Fact]
  public async Task AddControlOverride_ValidRule_AddsToCollection()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);
    _mockControlRepo.ControlsByPack["pack1"] = new List<ControlRecord>
    {
      new() { ControlId = "C1", SourcePackId = "pack1", ExternalIds = new() { RuleId = "SV-100001", VulnId = "V-100001" }, Title = "Test", Severity = "high" }
    };
    await vm.LoadAvailableRulesAsync(new[] { "pack1" }, CancellationToken.None);

    vm.SelectedRule = vm.AvailableRules[0];
    vm.SelectedRuleStatus = ControlStatus.NotApplicable;
    vm.SelectedRuleReason = "Out of scope";
    vm.SelectedRuleNotes = "Test notes";

    vm.AddControlOverrideCommand.Execute(null);

    Assert.Single(vm.ControlOverrides);
    var item = vm.ControlOverrides[0];
    Assert.Equal("SV-100001", item.RuleId);
    Assert.Equal("V-100001", item.VulnId);
    Assert.Equal(ControlStatus.NotApplicable, item.StatusOverride);
    Assert.Equal("Out of scope", item.NaReason);
    Assert.Equal("Test notes", item.Notes);
    Assert.Null(vm.SelectedRule);
    Assert.Equal(ControlStatus.NotApplicable, vm.SelectedRuleStatus);
    Assert.Empty(vm.SelectedRuleReason);
    Assert.Empty(vm.SelectedRuleNotes);
  }

  [Fact]
  public async Task AddControlOverride_NullRule_DoesNotAdd()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);

    vm.SelectedRule = null;
    vm.AddControlOverrideCommand.Execute(null);

    Assert.Empty(vm.ControlOverrides);
  }

  [Fact]
  public async Task AddControlOverride_DuplicateRuleId_PreventsDuplicate()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);
    _mockControlRepo.ControlsByPack["pack1"] = new List<ControlRecord>
    {
      new() { ControlId = "C1", SourcePackId = "pack1", ExternalIds = new() { RuleId = "SV-100001" }, Title = "Test", Severity = "high" }
    };
    await vm.LoadAvailableRulesAsync(new[] { "pack1" }, CancellationToken.None);

    vm.SelectedRule = vm.AvailableRules[0];
    vm.AddControlOverrideCommand.Execute(null);

    vm.SelectedRule = vm.AvailableRules[0];
    vm.AddControlOverrideCommand.Execute(null);

    Assert.Single(vm.ControlOverrides);
    Assert.Contains("already in override list", vm.OverlayStatus);
  }

  [Fact]
  public async Task AddControlOverride_DuplicateVulnId_PreventsDuplicate()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);
    _mockControlRepo.ControlsByPack["pack1"] = new List<ControlRecord>
    {
      new() { ControlId = "C1", SourcePackId = "pack1", ExternalIds = new() { VulnId = "V-100001" }, Title = "Test", Severity = "high" }
    };
    await vm.LoadAvailableRulesAsync(new[] { "pack1" }, CancellationToken.None);

    vm.SelectedRule = vm.AvailableRules[0];
    vm.AddControlOverrideCommand.Execute(null);

    vm.SelectedRule = vm.AvailableRules[0];
    vm.AddControlOverrideCommand.Execute(null);

    Assert.Single(vm.ControlOverrides);
    Assert.Contains("already in override list", vm.OverlayStatus);
  }

  [Fact]
  public void RemoveControlOverride_ValidItem_RemovesFromCollection()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);
    var overrideItem = new ControlOverride { RuleId = "SV-100001", VulnId = "V-100001", StatusOverride = ControlStatus.NotApplicable };
    vm.ControlOverrides.Add(overrideItem);

    vm.RemoveControlOverrideCommand.Execute(overrideItem);

    Assert.Empty(vm.ControlOverrides);
    Assert.Contains("Removed control override", vm.OverlayStatus);
  }

  [Fact]
  public void RemoveControlOverride_NullItem_DoesNotThrow()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);

    vm.RemoveControlOverrideCommand.Execute(null);

    Assert.Empty(vm.ControlOverrides);
  }

  [Fact]
  public async Task SaveOverlayAsync_WithControlOverrides_PersistsToOverlay()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);
    vm.OverlayName = "Test Overlay";
    vm.ControlOverrides.Add(new ControlOverride
    {
      RuleId = "SV-100001",
      VulnId = "V-100001",
      StatusOverride = ControlStatus.NotApplicable,
      NaReason = "Out of scope",
      Notes = "Test notes"
    });
    vm.PowerStigOverrides.Add(new PowerStigOverride
    {
      RuleId = "SV-100002",
      SettingName = "Setting1",
      Value = "Value1"
    });

    await vm.SaveOverlayCommand.ExecuteAsync(null);

    Assert.Single(_mockOverlayRepo.SavedOverlays);
    var saved = _mockOverlayRepo.SavedOverlays[0];
    Assert.Equal("Test Overlay", saved.Name);
    Assert.Single(saved.Overrides);
    Assert.Equal("SV-100001", saved.Overrides[0].RuleId);
    Assert.Equal(ControlStatus.NotApplicable, saved.Overrides[0].StatusOverride);
    Assert.Single(saved.PowerStigOverrides);
    Assert.Equal("SV-100002", saved.PowerStigOverrides[0].RuleId);
    Assert.Contains("Overlay saved", vm.OverlayStatus);
  }

  [Fact]
  public async Task SaveOverlayAsync_WithEmptyId_GeneratesNewId()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);
    vm.OverlayName = "Test Overlay";

    await vm.SaveOverlayCommand.ExecuteAsync(null);

    var saved = _mockOverlayRepo.SavedOverlays[0];
    Assert.NotEmpty(saved.OverlayId);
    Assert.Equal(saved.OverlayId, vm.OverlayId);
  }

  [Fact]
  public async Task SaveOverlayAsync_WithExistingId_UsesProvidedId()
  {
    var vm = new OverlayEditorViewModel(_mockOverlayRepo, _mockControlRepo);
    vm.OverlayId = "existing-overlay-id";
    vm.OverlayName = "Test Overlay";

    await vm.SaveOverlayCommand.ExecuteAsync(null);

    var saved = _mockOverlayRepo.SavedOverlays[0];
    Assert.Equal("existing-overlay-id", saved.OverlayId);
  }

  // Mock implementations
  private class MockOverlayRepository : IOverlayRepository
  {
    public List<Overlay> SavedOverlays = new();

    public Task SaveAsync(Overlay overlay, CancellationToken ct)
    {
      SavedOverlays.Add(overlay);
      return Task.CompletedTask;
    }

    public Task<Overlay?> GetAsync(string overlayId, CancellationToken ct) => Task.FromResult<Overlay?>(null);
    public Task<IReadOnlyList<Overlay>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Overlay>>(Array.Empty<Overlay>());
  }

  private class MockControlRepository : IControlRepository
  {
    public Dictionary<string, List<ControlRecord>> ControlsByPack = new();

    public Task SaveControlsAsync(string packId, IReadOnlyList<ControlRecord> controls, CancellationToken ct)
      => Task.CompletedTask;

    public Task<IReadOnlyList<ControlRecord>> ListControlsAsync(string packId, CancellationToken ct)
      => Task.FromResult<IReadOnlyList<ControlRecord>>(ControlsByPack.GetValueOrDefault(packId, new List<ControlRecord>()));

    public Task<bool> VerifySchemaAsync(CancellationToken ct) => Task.FromResult(true);
  }
}

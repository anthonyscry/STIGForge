using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Services;

namespace STIGForge.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
  private readonly IMainSharedState _shared;
  private readonly MainViewModel _main;

  public DashboardViewModel(
    IMainSharedState shared,
    IControlRepository controls,
    IOverlayRepository overlays,
    IBundleMissionSummaryService bundleMissionSummary,
    IPathBuilder paths,
    IAuditTrailService? audit,
    ILogger<DashboardViewModel>? logger = null)
  {
    _shared = shared;
    _main = (MainViewModel)shared;
  }

  public string BundleRoot
  {
    get => _shared.BundleRoot;
    set => _shared.BundleRoot = value;
  }

  public string StatusText
  {
    get => _shared.StatusText;
    set => _shared.StatusText = value;
  }

  public bool IsBusy
  {
    get => _shared.IsBusy;
    set => _shared.IsBusy = value;
  }

  [RelayCommand]
  private Task ComparePacks() => _main.ComparePacksCommand.ExecuteAsync(null);

  [RelayCommand]
  private Task RebaseOverlay() => _main.RebaseOverlayCommand.ExecuteAsync(null);

  [RelayCommand]
  private void DashRefresh() => _main.DashRefreshCommand.Execute(null);

  [RelayCommand]
  private void ShowAbout() => _main.ShowAboutCommand.Execute(null);

  [RelayCommand]
  private void ShowHelp() => _main.ShowHelpCommand.Execute(null);

  [RelayCommand]
  private void ToggleTheme() => _main.ToggleThemeCommand.Execute(null);

  [RelayCommand]
  private void BrowseBundle() => _main.BrowseBundleCommand.Execute(null);

  [RelayCommand]
  private void BrowseEvaluateStig() => _main.BrowseEvaluateStigCommand.Execute(null);

  [RelayCommand]
  private void BrowseScapCommand() => _main.BrowseScapCommandCommand.Execute(null);

  [RelayCommand]
  private void BrowsePowerStigModule() => _main.BrowsePowerStigModuleCommand.Execute(null);

  [RelayCommand]
  private void BrowsePowerStigData() => _main.BrowsePowerStigDataCommand.Execute(null);

  [RelayCommand]
  private Task ActivateToolkitAsync() => _main.ActivateToolkitCommand.ExecuteAsync(null);

  [RelayCommand]
  private void OpenToolkitRoot() => _main.OpenToolkitRootCommand.Execute(null);

  [RelayCommand]
  private void OpenBundleFolder() => _main.OpenBundleFolderCommand.Execute(null);

  [RelayCommand]
  private void OpenLastOutput() => _main.OpenLastOutputCommand.Execute(null);

  [RelayCommand]
  private void OpenAutomationGate() => _main.OpenAutomationGateCommand.Execute(null);

  [RelayCommand]
  private void UseRecentBundle() => _main.UseRecentBundleCommand.Execute(null);

  [RelayCommand]
  private void DeleteSelectedRecentBundle() => _main.DeleteSelectedRecentBundleCommand.Execute(null);

  [RelayCommand]
  private void DeleteBundle() => _main.DeleteBundleCommand.Execute(null);

  [RelayCommand]
  private void RefreshOverlap() => _main.RefreshOverlapCommand.Execute(null);
}

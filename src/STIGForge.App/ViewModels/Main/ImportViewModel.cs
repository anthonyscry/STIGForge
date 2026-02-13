using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;

namespace STIGForge.App.ViewModels;

public partial class ImportViewModel : ObservableObject
{
  private readonly IMainSharedState _shared;
  private readonly MainViewModel _main;

  public ImportViewModel(
    IMainSharedState shared,
    ContentPackImporter importer,
    IContentPackRepository packs,
    IProfileRepository profiles,
    IControlRepository controls,
    IOverlayRepository overlays,
    BundleBuilder builder,
    IPathBuilder paths)
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
  private Task ImportContentPackAsync() => _main.ImportContentPackCommand.ExecuteAsync(null);

  [RelayCommand]
  private Task ImportScapBenchmarkAsync() => _main.ImportScapBenchmarkCommand.ExecuteAsync(null);

  [RelayCommand]
  private Task ImportGpoPackageAsync() => _main.ImportGpoPackageCommand.ExecuteAsync(null);

  [RelayCommand]
  private void RefreshImportLibrary() => _main.RefreshImportLibraryCommand.Execute(null);

  [RelayCommand]
  private void OpenSelectedLibraryItem() => _main.OpenSelectedLibraryItemCommand.Execute(null);

  [RelayCommand]
  private Task BuildBundleAsync() => _main.BuildBundleCommand.ExecuteAsync(null);

  [RelayCommand]
  private Task SaveProfileAsync() => _main.SaveProfileCommand.ExecuteAsync(null);

  [RelayCommand]
  private Task RefreshOverlaysAsync() => _main.RefreshOverlaysCommand.ExecuteAsync(null);

  [RelayCommand]
  private Task DeleteSelectedPacks() => _main.DeleteSelectedPacksCommand.ExecuteAsync(null);

  [RelayCommand]
  private Task DeleteSelectedProfile() => _main.DeleteSelectedProfileCommand.ExecuteAsync(null);

  [RelayCommand]
  private void OpenOverlayEditor() => _main.OpenOverlayEditorCommand.Execute(null);

  [RelayCommand]
  private Task ExportPowerStigMapAsync() => _main.ExportPowerStigMapCommand.ExecuteAsync(null);

  [RelayCommand]
  private void OpenPackFolder() => _main.OpenPackFolderCommand.Execute(null);

  [RelayCommand]
  private Task OpenContentPicker() => _main.OpenContentPickerCommand.ExecuteAsync(null);

  [RelayCommand]
  private Task ScanMachineApplicabilityAsync() => _main.ScanMachineApplicabilityCommand.ExecuteAsync(null);
}

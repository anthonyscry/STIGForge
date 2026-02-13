using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Core.Services;
using STIGForge.Evidence;

namespace STIGForge.App.ViewModels;

public partial class ManualReviewViewModel : ObservableObject
{
  private readonly IMainSharedState _shared;
  private readonly MainViewModel _main;

  public ManualReviewViewModel(
    IMainSharedState shared,
    ManualAnswerService manualAnswerService,
    ControlAnnotationService annotationService,
    EvidenceCollector evidenceCollector)
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

  public string LastOutputPath
  {
    get => _shared.LastOutputPath;
    set => _shared.LastOutputPath = value;
  }

  [RelayCommand]
  private void BrowseEvidenceFile() => _main.BrowseEvidenceFileCommand.Execute(null);

  [RelayCommand]
  private void SaveEvidence() => _main.SaveEvidenceCommand.Execute(null);

  [RelayCommand]
  private void SaveManualAnswer() => _main.SaveManualAnswerCommand.Execute(null);

  [RelayCommand]
  private void ImportManualCsv() => _main.ImportManualCsvCommand.Execute(null);

  [RelayCommand]
  private void UndoAnswer() => _main.UndoAnswerCommand.Execute(null);

  [RelayCommand]
  private void UseSelectedControlForEvidence() => _main.UseSelectedControlForEvidenceCommand.Execute(null);

  [RelayCommand]
  private Task CollectSelectedControlEvidence() => _main.CollectSelectedControlEvidenceCommand.ExecuteAsync(null);

  [RelayCommand]
  private void OpenSelectedControlEvidenceFolder() => _main.OpenSelectedControlEvidenceFolderCommand.Execute(null);

  [RelayCommand]
  private void LaunchManualWizard() => _main.LaunchManualWizardCommand.Execute(null);

  [RelayCommand]
  private void LaunchBulkDisposition() => _main.LaunchBulkDispositionCommand.Execute(null);

  [RelayCommand]
  private void ExportManualCsv() => _main.ExportManualCsvCommand.Execute(null);

  [RelayCommand]
  private void ExportManualHtml() => _main.ExportManualHtmlCommand.Execute(null);

  [RelayCommand]
  private void SaveControlNotes() => _main.SaveControlNotesCommand.Execute(null);
}

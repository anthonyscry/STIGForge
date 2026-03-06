using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Content.Import;

namespace STIGForge.App;

public partial class WorkflowViewModel
{
    private async Task<bool> RunImportAsync()
    {
        StatusText = "Scanning import folder...";

        if (_importScanner == null || string.IsNullOrWhiteSpace(ImportFolderPath))
        {
            StatusText = "Import scanner not configured or no import folder";
            ImportedPacks = new ObservableCollection<ImportedPackViewModel>();
            ImportedItemsCount = 0;
            ImportWarnings = new List<string>();
            ImportWarningCount = 0;
            return false;
        }

        try
        {
            var result = await _importScanner.ScanAsync(ImportFolderPath, _cts.Token);
            _lastImportCandidates = result.Candidates;

            var packs = result.Candidates
                .GroupBy(c => c.FileName)
                .Select(g => new ImportedPackViewModel
                {
                    PackName = g.Key,
                    Files = g.SelectMany(c => c.ContentFileNames).Distinct().OrderBy(f => f).ToList()
                }).ToList();

            ImportedPacks = new ObservableCollection<ImportedPackViewModel>(packs);
            ImportedItemsCount = ImportedPacks.Count;

            var warnings = (result.Warnings ?? [])
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ImportWarnings = warnings;
            ImportWarningCount = warnings.Count;

            if (ImportedItemsCount == 0)
            {
                StatusText = ImportWarningCount > 0
                    ? $"No content packs found ({ImportWarningCount} warning(s))"
                    : "No content packs found in import folder";
            }
            else if (ImportWarningCount > 0)
            {
                StatusText = $"Found {ImportedItemsCount} content packs with {ImportWarningCount} warning(s)";
            }
            else
            {
                StatusText = $"Found {ImportedItemsCount} content packs";
            }

            if (ImportedItemsCount > 0 && !string.IsNullOrWhiteSpace(OutputFolderPath))
            {
                var staged = await Task.Run(() => StageApplyArtifacts(_lastImportCandidates, OutputFolderPath));
                if (staged > 0)
                    StatusText += $" ({staged} apply artifact(s) staged)";
            }

            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
            ImportedPacks = new ObservableCollection<ImportedPackViewModel>();
            ImportedItemsCount = 0;
            ImportWarnings = new List<string> { ex.Message };
            ImportWarningCount = 1;
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunImport))]
    private async Task RunImportStepAsync()
    {
        IsBusy = true;
        ImportState = StepState.Running;
        ImportError = string.Empty;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var success = await RunImportAsync();
            if (success)
            {
                ImportState = StepState.Complete;
                if (ScanState == StepState.Locked)
                    ScanState = StepState.Ready;
            }
            else
            {
                ImportState = StepState.Error;
                ImportError = StatusText;
            }
        }
        finally
        {
            stopwatch.Stop();
            LastImportDurationMs = stopwatch.ElapsedMilliseconds;
            IsBusy = false;
        }
    }
}

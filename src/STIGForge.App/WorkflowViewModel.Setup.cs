using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using STIGForge.App.Views;

namespace STIGForge.App;

public partial class WorkflowViewModel
{
    private void LoadSettings()
    {
        var settings = WorkflowSettings.Load();
        ImportFolderPath = settings.ImportFolderPath;
        EvaluateStigToolPath = settings.EvaluateStigToolPath;
        EvaluateAfPath = settings.EvaluateAfPath;
        EvaluateSelectStig = settings.EvaluateSelectStig;
        EvaluateAdditionalArgs = settings.EvaluateAdditionalArgs;
        SccToolPath = settings.SccToolPath;
        SccArguments = settings.SccArguments;
        SccWorkingDirectory = settings.SccWorkingDirectory;
        SccTimeoutSeconds = settings.SccTimeoutSeconds;
        OutputFolderPath = settings.OutputFolderPath;
        MachineTarget = settings.MachineTarget;
        RequireElevationForScan = settings.RequireElevationForScan;
        ExportCkl = settings.ExportCkl;
        ExportCsv = settings.ExportCsv;
        ExportXccdf = settings.ExportXccdf;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        WorkflowSettings.Save(new WorkflowSettings
        {
            ImportFolderPath = ImportFolderPath,
            EvaluateStigToolPath = EvaluateStigToolPath,
            EvaluateAfPath = EvaluateAfPath,
            EvaluateSelectStig = EvaluateSelectStig,
            EvaluateAdditionalArgs = EvaluateAdditionalArgs,
            SccToolPath = SccToolPath,
            SccArguments = SccArguments,
            SccWorkingDirectory = SccWorkingDirectory,
            SccTimeoutSeconds = SccTimeoutSeconds,
            OutputFolderPath = OutputFolderPath,
            MachineTarget = MachineTarget,
            RequireElevationForScan = RequireElevationForScan,
            ExportCkl = ExportCkl,
            ExportCsv = ExportCsv,
            ExportXccdf = ExportXccdf
        });
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var settingsWindow = new SettingsWindow
        {
            DataContext = this,
            Owner = Application.Current.MainWindow
        };

        if (settingsWindow.ShowDialog() != true)
            LoadSettings();
    }

    [RelayCommand]
    private void AutoScanSetupPaths()
    {
        var detected = new List<string>();
        var scanRoot = _autoScanRootResolver();

        if (string.IsNullOrWhiteSpace(scanRoot))
        {
            StatusText = "Auto scan did not find new setup paths";
            return;
        }

        if (string.IsNullOrWhiteSpace(ImportFolderPath))
        {
            var importCandidate = Path.Combine(scanRoot, "import");
            if (Directory.Exists(importCandidate))
            {
                ImportFolderPath = importCandidate;
                detected.Add("Import folder");
            }
        }

        if (string.IsNullOrWhiteSpace(EvaluateStigToolPath))
        {
            var evaluateCandidate = FindFirstResolvedPath(GetEvaluateStigCandidates(scanRoot), ResolveEvaluateStigToolRoot);
            if (!string.IsNullOrWhiteSpace(evaluateCandidate))
            {
                EvaluateStigToolPath = evaluateCandidate;
                detected.Add("Evaluate-STIG path");
            }
        }

        if (string.IsNullOrWhiteSpace(SccToolPath))
        {
            var sccCandidate = FindFirstResolvedPath(GetSccCandidates(scanRoot), ResolveSccCommandPath);
            if (!string.IsNullOrWhiteSpace(sccCandidate))
            {
                SccToolPath = sccCandidate;
                detected.Add("SCC path");
            }
        }

        if (string.IsNullOrWhiteSpace(OutputFolderPath))
        {
            var outputCandidate = Path.Combine(scanRoot, ".stigforge", "scans");
            Directory.CreateDirectory(outputCandidate);
            OutputFolderPath = outputCandidate;
            detected.Add("Output folder");
        }

        StatusText = detected.Count > 0
            ? "Auto-detected setup paths: " + string.Join(", ", detected)
            : "Auto scan did not find new setup paths";
    }

    private static string? ResolveAutoScanRoot()
    {
        var workspaceRoot = FindWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            return workspaceRoot;

        // Check parent of BaseDirectory (portable deployments: C:\STIGForge\App → C:\STIGForge)
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        if (baseDir.Parent != null && Directory.Exists(Path.Combine(baseDir.Parent.FullName, "import")))
            return baseDir.Parent.FullName;

        if (Directory.Exists(AppContext.BaseDirectory))
            return AppContext.BaseDirectory;

        return null;
    }

    private static string? FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }

    private static string? FindFirstResolvedPath(IEnumerable<string> candidates, Func<string, string?> resolver)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var resolved = resolver(candidate);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        return null;
    }

    private static IEnumerable<string> GetEvaluateStigCandidates(string scanRoot)
    {
        yield return Path.Combine(scanRoot, "tools", "Evaluate-STIG", "Evaluate-STIG");
        yield return Path.Combine(scanRoot, "tools", "Evaluate-STIG");
        yield return Path.Combine(scanRoot, "Evaluate-STIG", "Evaluate-STIG");
        yield return Path.Combine(scanRoot, "Evaluate-STIG");
        // Common standalone install paths
        yield return @"C:\Tools\Evaluate-STIG\Evaluate-STIG";
        yield return @"C:\Tools\Evaluate-STIG";
        yield return @"C:\Evaluate-STIG\Evaluate-STIG";
        yield return @"C:\Evaluate-STIG";
    }

    private static IEnumerable<string> GetSccCandidates(string scanRoot)
    {
        yield return Path.Combine(scanRoot, "tools", "SCC", "cscc.exe");
        yield return Path.Combine(scanRoot, "tools", "SCC", "cscc-remote.exe");
        yield return Path.Combine(scanRoot, "tools", "SCC");
        yield return Path.Combine(scanRoot, "SCC", "cscc.exe");
        yield return Path.Combine(scanRoot, "SCC", "cscc-remote.exe");
        yield return Path.Combine(scanRoot, "SCC");
        // Common standalone install paths
        yield return @"C:\Tools\SCC\cscc.exe";
        yield return @"C:\Tools\SCC";
        yield return @"C:\Program Files\SCC\cscc.exe";
        yield return @"C:\Program Files (x86)\SCC\cscc.exe";
    }

    private static string? ResolveEvaluateStigToolRoot(string? candidate)
    {
        var path = candidate?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (File.Exists(path))
        {
            if (string.Equals(Path.GetFileName(path), "Evaluate-STIG.ps1", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(path);

            return null;
        }

        if (!Directory.Exists(path))
            return null;

        var directScript = Path.Combine(path, "Evaluate-STIG.ps1");
        if (File.Exists(directScript))
            return path;

        try
        {
            var match = Directory.EnumerateFiles(path, "Evaluate-STIG.ps1", SearchOption.AllDirectories)
                .OrderBy(CountPathSeparators)
                .ThenBy(static p => p.Length)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(match))
                return Path.GetDirectoryName(match);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static string? ResolveSccCommandPath(string? candidate)
    {
        var path = candidate?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (File.Exists(path))
        {
            var fileName = Path.GetFileName(path);
            return IsSupportedSccCliFileName(fileName)
                ? Path.GetFullPath(path)
                : null;
        }

        if (!Directory.Exists(path))
            return null;

        foreach (var fileName in SupportedSccCliFileNames)
        {
            var directCandidate = Path.Combine(path, fileName);
            if (File.Exists(directCandidate))
                return directCandidate;
        }

        try
        {
            var match = Directory.EnumerateFiles(path, "*.exe", SearchOption.AllDirectories)
                .Where(static p =>
                {
                    var fileName = Path.GetFileName(p);
                    return IsSupportedSccCliFileName(fileName);
                })
                .OrderBy(CountPathSeparators)
                .ThenBy(static p => p.Length)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(match))
                return match;
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static readonly string[] SupportedSccCliFileNames =
    [
        "cscc.exe",
        "cscc-remote.exe",
        "cscc",
        "cscc-remote"
    ];

    private static bool IsSupportedSccCliFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        return SupportedSccCliFileNames.Any(name => string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeUnsupportedSccGuiPath(string? candidate)
    {
        var path = candidate?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (File.Exists(path))
            return string.Equals(Path.GetFileName(path), "scc.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path), "scc", StringComparison.OrdinalIgnoreCase);

        if (!Directory.Exists(path))
            return false;

        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Any(p => string.Equals(Path.GetFileName(p), "scc.exe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(p), "scc", StringComparison.OrdinalIgnoreCase));
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseImportFolder))]
    private void BrowseImportFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Import Folder" };
        if (dialog.ShowDialog() == true)
            ImportFolderPath = dialog.FolderName;
    }

    [RelayCommand(CanExecute = nameof(CanBrowseEvaluateStig))]
    private void BrowseEvaluateStig()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Evaluate-STIG Folder" };
        if (dialog.ShowDialog() == true)
            EvaluateStigToolPath = dialog.FolderName;
    }

    [RelayCommand(CanExecute = nameof(CanBrowseScc))]
    private void BrowseScc()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select SCC Executable",
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
            SccToolPath = dialog.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanBrowseOutputFolder))]
    private void BrowseOutputFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Output Folder" };
        if (dialog.ShowDialog() == true)
            OutputFolderPath = dialog.FolderName;
    }

    public bool CanBrowseImportFolder => !IsBusy;
    public bool CanBrowseEvaluateStig => !IsBusy;
    public bool CanBrowseScc => !IsBusy;
    public bool CanBrowseOutputFolder => !IsBusy;

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrWhiteSpace(OutputFolderPath) && Directory.Exists(OutputFolderPath))
            System.Diagnostics.Process.Start("explorer.exe", OutputFolderPath);
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "STIGForge");
        var dialog = new AboutDialog(dataRoot, 0, 0, 0)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void ShowHelp()
    {
        MessageBox.Show(
            "Open Settings to configure paths (or use Auto Scan Setup Paths), then run Import, Scan, Harden, and Verify in order or use Auto Workflow. You can also skip Harden when needed.",
            "Help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}

using STIGForge.App;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Apply;
using System.IO.Compression;
using System.Text.Json;

namespace STIGForge.UnitTests.App;

public class WorkflowViewModelTests
{
    private static readonly object SettingsFileLock = new();

    [Fact]
    public void InitialStep_IsSetup()
    {
        var vm = new WorkflowViewModel();
        Assert.Equal(WorkflowStep.Setup, vm.CurrentStep);
    }

    [Fact]
    public void FailureCardState_DefaultsToNull()
    {
        var vm = new WorkflowViewModel();
        Assert.Null(vm.CurrentFailureCard);
        Assert.Equal(string.Empty, vm.FailureCardLiveRegionHint);
    }

    [Fact]
    public void CanGoBack_IsFalse_OnSetupStep()
    {
        var vm = new WorkflowViewModel();
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public void CanGoNext_IsTrue_WhenSetupValid()
    {
        var vm = new WorkflowViewModel
        {
            ImportFolderPath = @"C:\test\import",
            EvaluateStigToolPath = @"C:\test\tool",
            OutputFolderPath = @"C:\test\output"
        };
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public void InitialStepStates_ImportReady_OthersLocked()
    {
        var vm = new WorkflowViewModel();
        Assert.Equal(StepState.Ready, vm.ImportState);
        Assert.Equal(StepState.Locked, vm.ScanState);
        Assert.Equal(StepState.Locked, vm.HardenState);
        Assert.Equal(StepState.Locked, vm.VerifyState);
    }

    [Fact]
    public void RestartWorkflow_ResetsStepStates()
    {
        var vm = new WorkflowViewModel();
        vm.ImportState = StepState.Complete;
        vm.ScanState = StepState.Complete;
        vm.HardenState = StepState.Error;
        vm.VerifyState = StepState.Running;

        vm.RestartWorkflowCommand.Execute(null);

        Assert.Equal(StepState.Ready, vm.ImportState);
        Assert.Equal(StepState.Locked, vm.ScanState);
        Assert.Equal(StepState.Locked, vm.HardenState);
        Assert.Equal(StepState.Locked, vm.VerifyState);
    }

    [Fact]
    public void ExportFormats_DefaultToExpected()
    {
        var vm = new WorkflowViewModel();
        Assert.True(vm.ExportCkl);
        Assert.False(vm.ExportCsv);
        Assert.False(vm.ExportXccdf);
    }

    [Fact]
    public void CanRunImport_WhenImportReady_IsTrue()
    {
        var vm = new WorkflowViewModel();
        vm.ImportState = StepState.Ready;
        Assert.True(vm.RunImportStepCommand.CanExecute(null));
    }

    [Fact]
    public void CanRunScan_WhenScanLocked_IsFalse()
    {
        var vm = new WorkflowViewModel();
        vm.ScanState = StepState.Locked;
        Assert.False(vm.RunScanStepCommand.CanExecute(null));
    }

    [Fact]
    public void CanRunAutoWorkflow_WhenNoStepRunning_IsTrue()
    {
        var vm = new WorkflowViewModel();
        Assert.True(vm.RunAutoWorkflowCommand.CanExecute(null));
    }

    [Fact]
    public void CanRunAutoWorkflow_WhenStepRunning_IsFalse()
    {
        var vm = new WorkflowViewModel();
        vm.ImportState = StepState.Running;
        Assert.False(vm.RunAutoWorkflowCommand.CanExecute(null));
    }

    [Fact]
    public void SaveSettingsCommand_Exists()
    {
        var vm = new WorkflowViewModel();
        Assert.NotNull(vm.SaveSettingsCommand);
    }

    [Fact]
    public void SaveSettingsAndLoadSettings_RoundTrip_AdvancedEvaluateOptions()
    {
        lock (SettingsFileLock)
        {
            var settingsPath = WorkflowSettings.DefaultPath;
            var hadOriginal = File.Exists(settingsPath);
            var originalJson = hadOriginal ? File.ReadAllText(settingsPath) : null;

            try
            {
                var vm = new WorkflowViewModel
                {
                    EvaluateAfPath = @"C:\test\evaluate\af",
                    EvaluateSelectStig = "U_MS_Windows_11_STIG",
                    EvaluateAdditionalArgs = "-ThrottleLimit 4 -SkipSignatureCheck",
                    RequireElevationForScan = false
                };

                vm.SaveSettingsCommand.Execute(null);

                var reloaded = new WorkflowViewModel();
                Assert.Equal(vm.EvaluateAfPath, reloaded.EvaluateAfPath);
                Assert.Equal(vm.EvaluateSelectStig, reloaded.EvaluateSelectStig);
                Assert.Equal(vm.EvaluateAdditionalArgs, reloaded.EvaluateAdditionalArgs);
                Assert.Equal(vm.RequireElevationForScan, reloaded.RequireElevationForScan);
            }
            finally
            {
                if (hadOriginal)
                {
                    var directory = Path.GetDirectoryName(settingsPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllText(settingsPath, originalJson!);
                }
                else if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }
            }
        }
    }

    [Fact]
    public void ShowSettingsCommand_IsGenerated()
    {
        var vm = new WorkflowViewModel();
        Assert.NotNull(vm.ShowSettingsCommand);
        Assert.True(vm.ShowSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void ShowHelpCommand_IsGenerated()
    {
        var vm = new WorkflowViewModel();
        Assert.NotNull(vm.ShowHelpCommand);
        Assert.True(vm.ShowHelpCommand.CanExecute(null));
    }

    [Fact]
    public void AutoScanSetupPathsCommand_WhenScanRootHasKnownFolders_PopulatesEmptyPathsFromRootOnly()
    {
        var scanRoot = Directory.CreateTempSubdirectory();

        try
        {
            var importPath = Path.Combine(scanRoot.FullName, "import");
            var evaluatePath = Path.Combine(scanRoot.FullName, "tools", "Evaluate-STIG");
            var resolvedEvaluatePath = Path.Combine(evaluatePath, "Evaluate-STIG");
            var sccPath = Path.Combine(scanRoot.FullName, "tools", "SCC");
            var resolvedSccPath = Path.Combine(sccPath, "cscc.exe");

            Directory.CreateDirectory(importPath);
            Directory.CreateDirectory(resolvedEvaluatePath);
            File.WriteAllText(Path.Combine(resolvedEvaluatePath, "Evaluate-STIG.ps1"), "# test script");
            Directory.CreateDirectory(sccPath);
            File.WriteAllText(resolvedSccPath, "stub");

            var vm = new WorkflowViewModel(autoScanRootResolver: () => scanRoot.FullName)
            {
                ImportFolderPath = string.Empty,
                EvaluateStigToolPath = string.Empty,
                SccToolPath = string.Empty,
                OutputFolderPath = string.Empty
            };

            vm.AutoScanSetupPathsCommand.Execute(null);

            var expectedOutputPath = Path.Combine(scanRoot.FullName, ".stigforge", "scans");
            Assert.Equal(importPath, vm.ImportFolderPath);
            Assert.Equal(resolvedEvaluatePath, vm.EvaluateStigToolPath);
            Assert.Equal(resolvedSccPath, vm.SccToolPath);
            Assert.Equal(expectedOutputPath, vm.OutputFolderPath);
            Assert.True(Directory.Exists(expectedOutputPath));
        }
        finally
        {
            scanRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void AutoScanSetupPathsCommand_WhenPathsAlreadyConfigured_DoesNotOverwriteValues()
    {
        var scanRoot = Directory.CreateTempSubdirectory();

        try
        {
            Directory.CreateDirectory(Path.Combine(scanRoot.FullName, "import"));
            Directory.CreateDirectory(Path.Combine(scanRoot.FullName, "tools", "Evaluate-STIG"));
            Directory.CreateDirectory(Path.Combine(scanRoot.FullName, "tools", "SCC"));

            var vm = new WorkflowViewModel(autoScanRootResolver: () => scanRoot.FullName)
            {
                ImportFolderPath = @"C:\preconfigured\import",
                EvaluateStigToolPath = @"C:\preconfigured\evaluate",
                SccToolPath = @"C:\preconfigured\scc",
                OutputFolderPath = @"C:\preconfigured\output"
            };

            vm.AutoScanSetupPathsCommand.Execute(null);

            Assert.Equal(@"C:\preconfigured\import", vm.ImportFolderPath);
            Assert.Equal(@"C:\preconfigured\evaluate", vm.EvaluateStigToolPath);
            Assert.Equal(@"C:\preconfigured\scc", vm.SccToolPath);
            Assert.Equal(@"C:\preconfigured\output", vm.OutputFolderPath);
            Assert.Equal("Auto scan did not find new setup paths", vm.StatusText);
        }
        finally
        {
            scanRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunImportStepCommand_WhenPreconditionsMissing_SetsErrorState()
    {
        var vm = new WorkflowViewModel
        {
            ImportFolderPath = @"C:\missing\import"
        };

        await vm.RunImportStepCommand.ExecuteAsync(null);

        Assert.Equal(StepState.Error, vm.ImportState);
        Assert.Equal("Import scanner not configured or no import folder", vm.ImportError);
    }

    [Fact]
    public async Task RunImportStepCommand_WhenPreconditionsMissing_ClearsPreviouslyImportedItems()
    {
        var vm = new WorkflowViewModel
        {
            ImportFolderPath = @"C:\missing\import",
            ImportedPacks = new System.Collections.ObjectModel.ObservableCollection<ImportedPackViewModel> { new ImportedPackViewModel { PackName = "old-content.zip" } },
            ImportedItemsCount = 1
        };

        await vm.RunImportStepCommand.ExecuteAsync(null);

        Assert.Equal(StepState.Error, vm.ImportState);
        Assert.Empty(vm.ImportedPacks);
        Assert.Equal(0, vm.ImportedItemsCount);
    }

    [Fact]
    public async Task RunImportStepCommand_WhenScannerReturnsWarnings_StoresWarnings()
    {
        var importFolder = Directory.CreateTempSubdirectory().FullName;

        try
        {
            File.WriteAllText(Path.Combine(importFolder, "invalid.zip"), "not a zip file");

            var vm = new WorkflowViewModel(
                importScanner: new ImportInboxScanner(new FixedHashingService()))
            {
                ImportFolderPath = importFolder,
                ImportState = StepState.Ready
            };

            await vm.RunImportStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Complete, vm.ImportState);
            Assert.Equal(0, vm.ImportedItemsCount);
            Assert.NotEmpty(vm.ImportWarnings);
            Assert.True(vm.ImportWarningCount > 0);
        }
        finally
        {
            Directory.Delete(importFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenEvaluateExitCodeFive_SetsElevationFailureCard()
    {
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 0,
            ToolRuns =
            [
                new VerificationToolRunResult
                {
                    Tool = "Evaluate-STIG",
                    Executed = true,
                    ExitCode = 5,
                    Error = "You must run this from an elevated PowerShell session"
                }
            ]
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                ScanState = StepState.Ready,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.NotNull(vm.CurrentFailureCard);
            Assert.Equal(WorkflowRootCauseCode.ElevationRequired, vm.CurrentFailureCard!.RootCauseCode);
            Assert.Contains("administrator", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rerun", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenNoCklDiagnostic_SetsNoCklFailureCard()
    {
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 0,
            Diagnostics = ["No CKL results were found under output root."]
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                ScanState = StepState.Ready,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.NotNull(vm.CurrentFailureCard);
            Assert.Equal(WorkflowRootCauseCode.NoCklOutput, vm.CurrentFailureCard!.RootCauseCode);
            Assert.Contains("output", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ckl", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
            Assert.True(vm.CurrentFailureCard.ShowOpenOutputFolderAction);
            Assert.True(vm.CurrentFailureCard.ShowRetryScanAction);
            Assert.False(vm.CurrentFailureCard.ShowRetryVerifyAction);
            Assert.Contains("Recovery guidance updated", vm.FailureCardLiveRegionHint, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenEvaluatePathInvalid_SetsPathFailureCard()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: new TrackingVerificationWorkflowService(new VerificationWorkflowResult
                {
                    ConsolidatedResultCount = 3
                }),
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                ScanState = StepState.Ready,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = @"C:\missing\Evaluate-STIG"
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.NotNull(vm.CurrentFailureCard);
            Assert.Equal(WorkflowRootCauseCode.EvaluatePathInvalid, vm.CurrentFailureCard!.RootCauseCode);
            Assert.Contains("path", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("settings", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
            Assert.True(vm.CurrentFailureCard.ShowOpenSettingsAction);
            Assert.True(vm.CurrentFailureCard.ShowRetryScanAction);
            Assert.False(vm.CurrentFailureCard.ShowRetryVerifyAction);
        }
        finally
        {
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenAdvancedEvaluateOptionsConfigured_PreservesQuotedValuesAndCoreArgs()
    {
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputRoot = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Path.Combine(outputRoot, "scan output");
        Directory.CreateDirectory(outputFolder);
        CreateEvaluateStigScript(evaluateTool);
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 3
        });

        try
        {
            var vm = new WorkflowViewModel(importScanner: null, verifyService: verifyService, isElevatedResolver: () => true)
            {
                ScanState = StepState.Ready,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool,
                EvaluateAfPath = @"C:\Evaluate-STIG\Answer Files",
                EvaluateSelectStig = "Win11,Chrome",
                EvaluateAdditionalArgs = "-AllowDeprecated"
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            var args = verifyService.LastRequest!.EvaluateStig.Arguments;
            Assert.Contains("-AFPath \"C:\\Evaluate-STIG\\Answer Files\"", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-SelectSTIG \"Win11,Chrome\"", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-AllowDeprecated", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-Output CKL", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"-OutputPath \"{outputFolder}\"", args, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenAdvancedEvaluateOptionsConfigured_PropagatesQuotedArguments()
    {
        var outputRoot = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Path.Combine(outputRoot, "verify output");
        Directory.CreateDirectory(outputFolder);
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 1
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                isElevatedResolver: () => true)
            {
                VerifyState = StepState.Ready,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool,
                EvaluateAfPath = @"C:\Evaluate-STIG\Answer Files",
                EvaluateSelectStig = "Win11,Chrome",
                EvaluateAdditionalArgs = "-AllowDeprecated",
                MachineTarget = "server01"
            };

            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            var args = verifyService.LastRequest!.EvaluateStig.Arguments;
            Assert.Contains("-AFPath \"C:\\Evaluate-STIG\\Answer Files\"", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-SelectSTIG \"Win11,Chrome\"", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-AllowDeprecated", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-Output CKL", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"-OutputPath \"{outputFolder}\"", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-ComputerName \"server01\"", args, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenServiceThrows_SetsErrorAndKeepsHardenLocked()
    {
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: new ThrowingVerificationWorkflowService("scan exploded"),
                isElevatedResolver: () => true)
            {
                ScanState = StepState.Ready,
                HardenState = StepState.Locked,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.ScanState);
            Assert.Equal("Scan failed: scan exploded", vm.ScanError);
            Assert.Equal(StepState.Locked, vm.HardenState);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAutoWorkflowCommand_WhenScanFails_StopsBeforeHardenAndVerify()
    {
        var importFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: new ImportInboxScanner(new FixedHashingService()),
                verifyService: new ThrowingVerificationWorkflowService("scan exploded"),
                isElevatedResolver: () => true)
            {
                ImportFolderPath = importFolder,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            CreateMinimalStigZip(importFolder, "test1.zip");

            await vm.RunAutoWorkflowCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Complete, vm.ImportState);
            Assert.Equal(StepState.Error, vm.ScanState);
            Assert.Equal(StepState.Locked, vm.HardenState);
            Assert.Equal(StepState.Locked, vm.VerifyState);
            Assert.Equal("Scan failed: scan exploded", vm.ScanError);
        }
        finally
        {
            Directory.Delete(importFolder, recursive: true);
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunHardenStepCommand_WhenApplyRunnerNotConfigured_SetsErrorState()
    {
        var tempFolder = Directory.CreateTempSubdirectory();

        try
        {
            var vm = new WorkflowViewModel
            {
                HardenState = StepState.Ready,
                OutputFolderPath = tempFolder.FullName
            };

            await vm.RunHardenStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.HardenState);
            Assert.Equal("Apply runner not configured", vm.HardenError);
        }
        finally
        {
            tempFolder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunHardenStepCommand_WhenOutputFolderMissing_SetsErrorState()
    {
        var vm = new WorkflowViewModel(
            runApply: (_, __) => Task.FromResult(new ApplyResult
            {
                IsMissionComplete = true
            }))
        {
            HardenState = StepState.Ready,
            OutputFolderPath = @"C:\does-not-exist"
        };

        await vm.RunHardenStepCommand.ExecuteAsync(null);

        Assert.Equal(StepState.Error, vm.HardenState);
        Assert.Equal("Hardening cannot run: output folder does not exist", vm.HardenError);
    }

    [Fact]
    public async Task RunHardenStepCommand_WhenApplyRunnerThrows_SetsErrorState()
    {
        var tempFolder = Directory.CreateTempSubdirectory();

        try
        {
            var vm = new WorkflowViewModel(
                runApply: (_, __) => throw new InvalidOperationException("apply exploded"))
            {
                HardenState = StepState.Ready,
                OutputFolderPath = tempFolder.FullName
            };

            await vm.RunHardenStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.HardenState);
            Assert.Equal("Hardening failed: apply exploded", vm.HardenError);
        }
        finally
        {
            tempFolder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunHardenStepCommand_WhenApplyRunnerReturnsBlockingFailure_SetsErrorState()
    {
        var tempFolder = Directory.CreateTempSubdirectory();

        try
        {
            var vm = new WorkflowViewModel(
                runApply: (_, __) => Task.FromResult(new ApplyResult
                {
                    IsMissionComplete = true,
                    BlockingFailures = new[] { "step failed" }
                }))
            {
                HardenState = StepState.Ready,
                OutputFolderPath = tempFolder.FullName
            };

            await vm.RunHardenStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.HardenState);
            Assert.Equal("Hardening failed: step failed", vm.HardenError);
        }
        finally
        {
            tempFolder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunAutoWorkflowCommand_WhenHardenFails_StopsBeforeVerify()
    {
        var importFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 2
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: new ImportInboxScanner(new FixedHashingService()),
                verifyService: verifyService,
                runApply: (_, __) => Task.FromResult(new ApplyResult
                {
                    IsMissionComplete = false
                }),
                isElevatedResolver: () => true)
            {
                ImportFolderPath = importFolder,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            CreateMinimalStigZip(importFolder, "test1.zip");

            await vm.RunAutoWorkflowCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Complete, vm.ImportState);
            Assert.Equal(StepState.Complete, vm.ScanState);
            Assert.Equal(StepState.Error, vm.HardenState);
            Assert.Equal("Hardening did not complete successfully", vm.HardenError);
            Assert.Equal(StepState.Locked, vm.VerifyState);
            Assert.Equal(1, verifyService.CallCount);
        }
        finally
        {
            Directory.Delete(importFolder, recursive: true);
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunHardenStepCommand_WhenApplyRunnerSucceeds_SetsCompleteState()
    {
        var tempFolder = Directory.CreateTempSubdirectory();

        try
        {
            var vm = new WorkflowViewModel(
                runApply: (_, __) => Task.FromResult(new ApplyResult
                {
                    IsMissionComplete = true,
                    Steps = new[]
                    {
                        new ApplyStepOutcome { ExitCode = 0 },
                        new ApplyStepOutcome { ExitCode = 1 }
                    }
                }))
            {
                HardenState = StepState.Ready,
                OutputFolderPath = tempFolder.FullName,
                VerifyState = StepState.Locked
            };

            await vm.RunHardenStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Complete, vm.HardenState);
            Assert.Equal(1, vm.AppliedFixesCount);
            Assert.Equal(StepState.Ready, vm.VerifyState);
        }
        finally
        {
            tempFolder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunHardenStepCommand_WhenApplyResultContainsNullCollections_StillCompletes()
    {
        var tempFolder = Directory.CreateTempSubdirectory();

        try
        {
            var vm = new WorkflowViewModel(
                runApply: (_, __) => Task.FromResult(new ApplyResult
                {
                    IsMissionComplete = true,
                    Steps = null!,
                    BlockingFailures = null!
                }))
            {
                HardenState = StepState.Ready,
                OutputFolderPath = tempFolder.FullName,
                VerifyState = StepState.Locked
            };

            await vm.RunHardenStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Complete, vm.HardenState);
            Assert.Equal(0, vm.AppliedFixesCount);
            Assert.Equal(StepState.Ready, vm.VerifyState);
        }
        finally
        {
            tempFolder.Delete(recursive: true);
        }
    }

    [Fact]
    public void SkipHardenStepCommand_WhenHardenLocked_IsNotExecutable()
    {
        var vm = new WorkflowViewModel
        {
            HardenState = StepState.Locked
        };

        Assert.False(vm.SkipHardenStepCommand.CanExecute(null));
    }

    [Fact]
    public void SkipHardenStepCommand_WhenHardenReady_SetsCompleteAndUnlocksVerify()
    {
        var vm = new WorkflowViewModel
        {
            HardenState = StepState.Ready,
            VerifyState = StepState.Locked,
            HardenError = "previous failure"
        };

        vm.SkipHardenStepCommand.Execute(null);

        Assert.Equal(StepState.Complete, vm.HardenState);
        Assert.Equal(StepState.Ready, vm.VerifyState);
        Assert.Equal(string.Empty, vm.HardenError);
        Assert.Equal("Hardening skipped by operator", vm.StatusText);
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenServiceThrows_SetsErrorState()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: new ThrowingVerificationWorkflowService("verify exploded"),
                isElevatedResolver: () => true)
            {
                VerifyState = StepState.Ready,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.VerifyState);
            Assert.Equal("Verification failed: verify exploded", vm.VerifyError);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenSccGuiBinaryConfigured_SetsErrorAndSkipsVerificationService()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var sccFolder = Directory.CreateTempSubdirectory().FullName;
        var sccGuiPath = Path.Combine(sccFolder, "scc.exe");
        File.WriteAllText(sccGuiPath, "stub");
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 5
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                isElevatedResolver: () => true)
            {
                VerifyState = StepState.Ready,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool,
                SccToolPath = sccGuiPath
            };

            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.VerifyState);
            Assert.Contains("scc.exe", vm.VerifyError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, verifyService.CallCount);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
            Directory.Delete(sccFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenImportedLibraryEmpty_SetsErrorAndSkipsVerifyService()
    {
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 10
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService)
            {
                ScanState = StepState.Ready,
                HardenState = StepState.Locked,
                ImportedItemsCount = 0,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.ScanState);
            Assert.Equal("No imported content detected. Run Import and confirm items in Imported Library.", vm.ScanError);
            Assert.Equal(0, verifyService.CallCount);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenProcessNotElevated_BlocksWithAdminGuidance()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 3
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => false)
            {
                ScanState = StepState.Ready,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.ScanState);
            Assert.Contains("administrator", vm.ScanError, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(vm.CurrentFailureCard);
            Assert.Equal(WorkflowRootCauseCode.ElevationRequired, vm.CurrentFailureCard!.RootCauseCode);
            Assert.Equal(0, verifyService.CallCount);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenElevationNotRequired_AllowsNonElevatedProcess()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 3
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => false)
            {
                ScanState = StepState.Ready,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool,
                RequireElevationForScan = false
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Complete, vm.ScanState);
            Assert.Equal(1, verifyService.CallCount);
            Assert.DoesNotContain("administrator", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenEvaluateExitCodeIsFive_ShowsElevationSpecificGuidance()
    {
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 0,
            ToolRuns =
            [
                new VerificationToolRunResult
                {
                    Tool = "Evaluate-STIG",
                    Executed = true,
                    ExitCode = 5,
                    Error = "You must run this from an elevated PowerShell session"
                }
            ]
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                ScanState = StepState.Ready,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.ScanState);
            Assert.Contains("administrator", vm.ScanError, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("exit code 5", vm.ScanError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenFailureThenSuccess_ClearsFailureCard()
    {
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        var verifyService = new SequenceVerificationWorkflowService(
            new VerificationWorkflowResult
            {
                ConsolidatedResultCount = 0,
                Diagnostics = ["No CKL results were found under output root."]
            },
            new VerificationWorkflowResult
            {
                ConsolidatedResultCount = 2
            });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                ScanState = StepState.Ready,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.NotNull(vm.CurrentFailureCard);
            Assert.Equal(WorkflowRootCauseCode.NoCklOutput, vm.CurrentFailureCard!.RootCauseCode);

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Complete, vm.ScanState);
            Assert.Null(vm.CurrentFailureCard);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenFailureThenDifferentFailure_ReplacesFailureCard()
    {
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        var verifyService = new SequenceVerificationWorkflowService(
            new VerificationWorkflowResult
            {
                ConsolidatedResultCount = 0,
                Diagnostics = ["No CKL results were found under output root."]
            },
            new VerificationWorkflowResult
            {
                ConsolidatedResultCount = 0,
                ToolRuns =
                [
                    new VerificationToolRunResult
                    {
                        Tool = "Evaluate-STIG",
                        Executed = true,
                        ExitCode = 23,
                        Error = "Generic failure"
                    }
                ]
            });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                ScanState = StepState.Ready,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);
            var firstCard = vm.CurrentFailureCard;
            Assert.NotNull(firstCard);
            Assert.Equal(WorkflowRootCauseCode.NoCklOutput, firstCard!.RootCauseCode);

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.NotNull(vm.CurrentFailureCard);
            Assert.NotSame(firstCard, vm.CurrentFailureCard);
            Assert.Equal(WorkflowRootCauseCode.ToolExitNonZero, vm.CurrentFailureCard!.RootCauseCode);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunScanStepCommand_WhenNoCklDiagnosticPresent_UsesNoCklProducedClassification()
    {
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 0,
            Diagnostics = ["No CKL results were found under output root."]
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                ScanState = StepState.Ready,
                ImportedItemsCount = 1,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunScanStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.ScanState);
            Assert.Contains("no ckl", vm.ScanError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenProcessNotElevated_BlocksWithAdminGuidance()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 1
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => false)
            {
                ScanState = StepState.Complete,
                HardenState = StepState.Complete,
                VerifyState = StepState.Ready,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.VerifyState);
            Assert.Contains("administrator", vm.VerifyError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, verifyService.CallCount);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenElevationNotRequired_AllowsNonElevatedProcess()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 1
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => false)
            {
                ScanState = StepState.Complete,
                HardenState = StepState.Complete,
                VerifyState = StepState.Ready,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool,
                RequireElevationForScan = false
            };

            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Complete, vm.VerifyState);
            Assert.Equal(1, verifyService.CallCount);
            Assert.DoesNotContain("administrator", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenEvaluatePathInvalid_SetsVerifyPathFailureCard()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: new TrackingVerificationWorkflowService(new VerificationWorkflowResult
                {
                    ConsolidatedResultCount = 1
                }),
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                ScanState = StepState.Complete,
                HardenState = StepState.Complete,
                VerifyState = StepState.Ready,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = @"C:\missing\Evaluate-STIG"
            };

            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            Assert.NotNull(vm.CurrentFailureCard);
            Assert.Equal(WorkflowRootCauseCode.EvaluatePathInvalid, vm.CurrentFailureCard!.RootCauseCode);
            Assert.Contains("path", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("settings", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("verify", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenZeroFindingsAndEvaluateExitCodeIsFive_ClassifiesAsAdminFailure()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 0,
            ToolRuns =
            [
                new VerificationToolRunResult
                {
                    Tool = "Evaluate-STIG",
                    Executed = true,
                    ExitCode = 5,
                    Error = "You must run this from an elevated PowerShell session"
                }
            ]
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                VerifyState = StepState.Ready,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.VerifyState);
            Assert.Contains("exit code 5", vm.VerifyError, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("administrator", vm.VerifyError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenZeroFindingsAndNoCklDiagnosticPresent_ClassifiesAsNoCklFailure()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 0,
            Diagnostics = ["No CKL results were found under output root."]
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                VerifyState = StepState.Ready,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            Assert.Equal(StepState.Error, vm.VerifyState);
            Assert.Contains("no ckl output", vm.VerifyError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenEvaluateExitCodeFive_SetsElevationFailureCard()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 0,
            ToolRuns =
            [
                new VerificationToolRunResult
                {
                    Tool = "Evaluate-STIG",
                    Executed = true,
                    ExitCode = 5,
                    Error = "You must run this from an elevated PowerShell session"
                }
            ]
        });

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: verifyService,
                runApply: null,
                autoScanRootResolver: null,
                isElevatedResolver: () => true)
            {
                VerifyState = StepState.Ready,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            Assert.NotNull(vm.CurrentFailureCard);
            Assert.Equal(WorkflowRootCauseCode.ElevationRequired, vm.CurrentFailureCard!.RootCauseCode);
            Assert.Contains("administrator", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rerun", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
            Assert.False(vm.CurrentFailureCard.ShowRetryScanAction);
            Assert.True(vm.CurrentFailureCard.ShowRetryVerifyAction);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenUnknownError_SetsUnknownFailureCard()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        try
        {
            var vm = new WorkflowViewModel(
                importScanner: null,
                verifyService: new ThrowingVerificationWorkflowService("verify exploded"),
                isElevatedResolver: () => true)
            {
                VerifyState = StepState.Ready,
                OutputFolderPath = outputFolder,
                EvaluateStigToolPath = evaluateTool
            };

            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            Assert.NotNull(vm.CurrentFailureCard);
            Assert.Equal(WorkflowRootCauseCode.UnknownFailure, vm.CurrentFailureCard!.RootCauseCode);
            Assert.Contains("diagnostics", vm.CurrentFailureCard.NextStep, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenSuccessful_WritesMissionJson()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);
        var consolidatedJson = Path.Combine(outputFolder, "consolidated-results.json");
        var consolidatedCsv = Path.Combine(outputFolder, "consolidated-results.csv");
        var coverageJson = Path.Combine(outputFolder, "coverage_summary.json");
        var coverageCsv = Path.Combine(outputFolder, "coverage_summary.csv");

        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var finishedAt = DateTimeOffset.UtcNow;

        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            ConsolidatedJsonPath = consolidatedJson,
            ConsolidatedCsvPath = consolidatedCsv,
            CoverageSummaryJsonPath = coverageJson,
            CoverageSummaryCsvPath = coverageCsv,
            ConsolidatedResultCount = 3,
            Diagnostics = new[] { "verify-diagnostic" }
        });

        var vm = new WorkflowViewModel(
            importScanner: null,
            verifyService: verifyService,
            isElevatedResolver: () => true)
        {
            VerifyState = StepState.Ready,
            OutputFolderPath = outputFolder,
            EvaluateStigToolPath = evaluateTool
        };

        try
        {
            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            var missionPath = Path.Combine(outputFolder, "mission.json");
            Assert.True(File.Exists(missionPath));

            var missionJson = await File.ReadAllTextAsync(missionPath);
            var mission = JsonSerializer.Deserialize<LocalWorkflowMission>(missionJson);

            Assert.NotNull(mission);
            Assert.Equal(missionPath, mission!.StageMetadata.MissionJsonPath);
            Assert.Equal(consolidatedJson, mission.StageMetadata.ConsolidatedJsonPath);
            Assert.Equal(consolidatedCsv, mission.StageMetadata.ConsolidatedCsvPath);
            Assert.Equal(coverageJson, mission.StageMetadata.CoverageSummaryJsonPath);
            Assert.Equal(coverageCsv, mission.StageMetadata.CoverageSummaryCsvPath);
            Assert.Equal(startedAt, mission.StageMetadata.StartedAt);
            Assert.Equal(finishedAt, mission.StageMetadata.FinishedAt);
            Assert.Contains("verify-diagnostic", mission.Diagnostics);
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WritesMissionWithToolRunDiagnostics()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 1,
            Diagnostics = new[] { "verify-diagnostic" },
            ToolRuns =
            [
                new VerificationToolRunResult
                {
                    Tool = "Evaluate-STIG",
                    Executed = true,
                    ExitCode = 5,
                    Error = "Access denied"
                }
            ]
        });

        var vm = new WorkflowViewModel(
            importScanner: null,
            verifyService: verifyService,
            isElevatedResolver: () => true)
        {
            VerifyState = StepState.Ready,
            OutputFolderPath = outputFolder,
            EvaluateStigToolPath = evaluateTool
        };

        try
        {
            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            var missionPath = Path.Combine(outputFolder, "mission.json");
            var missionJson = await File.ReadAllTextAsync(missionPath);
            var mission = JsonSerializer.Deserialize<LocalWorkflowMission>(missionJson);

            Assert.NotNull(mission);
            Assert.Contains("verify-diagnostic", mission!.Diagnostics);
            Assert.Contains(mission.Diagnostics, d => d.Contains("Evaluate-STIG", StringComparison.OrdinalIgnoreCase)
                && d.Contains("ExitCode=5", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenZeroFindingsFailure_WritesMissionWithRootCauseDiagnostic()
    {
        var outputFolder = Directory.CreateTempSubdirectory().FullName;
        var evaluateTool = Directory.CreateTempSubdirectory().FullName;
        CreateEvaluateStigScript(evaluateTool);

        var verifyService = new TrackingVerificationWorkflowService(new VerificationWorkflowResult
        {
            ConsolidatedResultCount = 0,
            Diagnostics = ["No CKL results were found under output root."]
        });

        var vm = new WorkflowViewModel(
            importScanner: null,
            verifyService: verifyService,
            isElevatedResolver: () => true)
        {
            VerifyState = StepState.Ready,
            OutputFolderPath = outputFolder,
            EvaluateStigToolPath = evaluateTool
        };

        try
        {
            await vm.RunVerifyStepCommand.ExecuteAsync(null);

            var missionPath = Path.Combine(outputFolder, "mission.json");
            var missionJson = await File.ReadAllTextAsync(missionPath);
            var mission = JsonSerializer.Deserialize<LocalWorkflowMission>(missionJson);

            Assert.NotNull(mission);
            Assert.Contains(mission!.Diagnostics, d => d.Contains("RootCause=NoCklOutput", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(mission.Diagnostics, d => d.Contains("Stage=Verify", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(evaluateTool, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    private static void CreateMinimalStigZip(string folder, string fileName)
    {
        var zipPath = Path.Combine(folder, fileName);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("sample-xccdf.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("<Benchmark xmlns=\"http://checklists.nist.gov/xccdf/1.2\" />");
    }

    private static void CreateEvaluateStigScript(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Evaluate-STIG.ps1"), "Write-Host 'ok'");
    }

    private sealed class ThrowingVerificationWorkflowService : IVerificationWorkflowService
    {
        private readonly string _message;

        public ThrowingVerificationWorkflowService(string message)
        {
            _message = message;
        }

        public Task<VerificationWorkflowResult> RunAsync(VerificationWorkflowRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException(_message);
        }
    }

    private sealed class TrackingVerificationWorkflowService : IVerificationWorkflowService
    {
        private readonly VerificationWorkflowResult _result;

        public TrackingVerificationWorkflowService(VerificationWorkflowResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public VerificationWorkflowRequest? LastRequest { get; private set; }

        public Task<VerificationWorkflowResult> RunAsync(VerificationWorkflowRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class SequenceVerificationWorkflowService : IVerificationWorkflowService
    {
        private readonly Queue<VerificationWorkflowResult> _results;

        public SequenceVerificationWorkflowService(params VerificationWorkflowResult[] results)
        {
            _results = new Queue<VerificationWorkflowResult>(results);
        }

        public Task<VerificationWorkflowResult> RunAsync(VerificationWorkflowRequest request, CancellationToken ct)
        {
            if (_results.Count == 0)
                throw new InvalidOperationException("No queued verification result available.");

            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FixedHashingService : IHashingService
    {
        public Task<string> Sha256FileAsync(string path, CancellationToken ct)
        {
            return Task.FromResult("00");
        }

        public Task<string> Sha256TextAsync(string content, CancellationToken ct)
        {
            return Task.FromResult("00");
        }
    }
}

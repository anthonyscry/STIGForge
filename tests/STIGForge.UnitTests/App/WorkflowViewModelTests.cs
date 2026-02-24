using STIGForge.App;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.App;

public class WorkflowViewModelTests
{
    [Fact]
    public void InitialStep_IsSetup()
    {
        var vm = new WorkflowViewModel();
        Assert.Equal(WorkflowStep.Setup, vm.CurrentStep);
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
    public async Task RunScanStepCommand_WhenServiceThrows_SetsErrorAndKeepsHardenLocked()
    {
        var vm = new WorkflowViewModel(
            importScanner: null,
            verifyService: new ThrowingVerificationWorkflowService("scan exploded"))
        {
            ScanState = StepState.Ready,
            HardenState = StepState.Locked,
            OutputFolderPath = @"C:\output",
            EvaluateStigToolPath = @"C:\tools\Evaluate-STIG"
        };

        await vm.RunScanStepCommand.ExecuteAsync(null);

        Assert.Equal(StepState.Error, vm.ScanState);
        Assert.Equal("Scan failed: scan exploded", vm.ScanError);
        Assert.Equal(StepState.Locked, vm.HardenState);
    }

    [Fact]
    public async Task RunAutoWorkflowCommand_WhenScanFails_StopsBeforeHardenAndVerify()
    {
        var importFolder = Directory.CreateTempSubdirectory().FullName;
        var vm = new WorkflowViewModel(
            importScanner: new ImportInboxScanner(new FixedHashingService()),
            verifyService: new ThrowingVerificationWorkflowService("scan exploded"))
        {
            ImportFolderPath = importFolder,
            OutputFolderPath = @"C:\output",
            EvaluateStigToolPath = @"C:\tools\Evaluate-STIG"
        };

        try
        {
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
        }
    }

    [Fact]
    public async Task RunVerifyStepCommand_WhenServiceThrows_SetsErrorState()
    {
        var vm = new WorkflowViewModel(
            importScanner: null,
            verifyService: new ThrowingVerificationWorkflowService("verify exploded"))
        {
            VerifyState = StepState.Ready,
            OutputFolderPath = @"C:\output",
            EvaluateStigToolPath = @"C:\tools\Evaluate-STIG"
        };

        await vm.RunVerifyStepCommand.ExecuteAsync(null);

        Assert.Equal(StepState.Error, vm.VerifyState);
        Assert.Equal("Verification failed: verify exploded", vm.VerifyError);
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

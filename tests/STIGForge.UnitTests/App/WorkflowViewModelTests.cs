using STIGForge.App;

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
}

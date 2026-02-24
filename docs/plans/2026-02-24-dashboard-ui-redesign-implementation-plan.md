# Dashboard UI Redesign Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Transform the STIGForge WPF app from a 6-step wizard to a 4-panel dashboard with modal settings.

**Architecture:** Replace WorkflowWizardView with DashboardView containing 4 StepPanel controls. Add SettingsWindow as a modal dialog. Extend WorkflowViewModel with step states, auto-workflow command, and export format settings. Update WorkflowSettings with export format properties.

**Tech Stack:** WPF, MVVM (CommunityToolkit.Mvvm), C# 12, .NET 8

---

## Task 1: Add Export Format Properties to WorkflowSettings

**Files:**
- Modify: `src/STIGForge.App/WorkflowSettings.cs:7-14`
- Test: `tests/STIGForge.UnitTests/App/WorkflowSettingsTests.cs`

**Step 1: Write the failing test**

Add to `tests/STIGForge.UnitTests/App/WorkflowSettingsTests.cs`:

```csharp
[Fact]
public void SaveAndLoad_RoundTrips_ExportFormats()
{
    var tempPath = Path.Combine(Path.GetTempPath(), $"stigforge-test-{Guid.NewGuid()}.json");
    try
    {
        var settings = new WorkflowSettings
        {
            ExportCkl = true,
            ExportCsv = false,
            ExportXccdf = true
        };

        WorkflowSettings.Save(settings, tempPath);
        var loaded = WorkflowSettings.Load(tempPath);

        Assert.True(loaded.ExportCkl);
        Assert.False(loaded.ExportCsv);
        Assert.True(loaded.ExportXccdf);
    }
    finally
    {
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowSettingsTests.SaveAndLoad_RoundTrips_ExportFormats" -v n`

Expected: FAIL - properties don't exist

**Step 3: Write minimal implementation**

Add to `src/STIGForge.App/WorkflowSettings.cs` after line 13 (after MachineTarget property):

```csharp
public bool ExportCkl { get; set; } = true;
public bool ExportCsv { get; set; }
public bool ExportXccdf { get; set; }
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowSettingsTests.SaveAndLoad_RoundTrips_ExportFormats" -v n`

Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowSettings.cs tests/STIGForge.UnitTests/App/WorkflowSettingsTests.cs
git commit -m "feat(settings): add export format properties (CKL, CSV, XCCDF)"
```

---

## Task 2: Add StepState Enum and Step State Properties to ViewModel

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs:11-19`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write the failing test**

Add to `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`:

```csharp
[Fact]
public void InitialStepStates_ImportReady_OthersLocked()
{
    var vm = new WorkflowViewModel();
    Assert.Equal(StepState.Ready, vm.ImportState);
    Assert.Equal(StepState.Locked, vm.ScanState);
    Assert.Equal(StepState.Locked, vm.HardenState);
    Assert.Equal(StepState.Locked, vm.VerifyState);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.InitialStepStates_ImportReady_OthersLocked" -v n`

Expected: FAIL - StepState enum and properties don't exist

**Step 3: Write minimal implementation**

Add StepState enum after WorkflowStep enum in `src/STIGForge.App/WorkflowViewModel.cs` (after line 19):

```csharp
public enum StepState
{
    Locked,
    Ready,
    Running,
    Complete,
    Error
}
```

Add properties to WorkflowViewModel class (after line 93, after VerifyFindingsCount):

```csharp
[ObservableProperty]
private StepState _importState = StepState.Ready;

[ObservableProperty]
private StepState _scanState = StepState.Locked;

[ObservableProperty]
private StepState _hardenState = StepState.Locked;

[ObservableProperty]
private StepState _verifyState = StepState.Locked;

[ObservableProperty]
private string _importError = string.Empty;

[ObservableProperty]
private string _scanError = string.Empty;

[ObservableProperty]
private string _hardenError = string.Empty;

[ObservableProperty]
private string _verifyError = string.Empty;
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.InitialStepStates_ImportReady_OthersLocked" -v n`

Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(vm): add StepState enum and step state properties"
```

---

## Task 3: Add Export Format Properties to ViewModel

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write the failing test**

Add to `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`:

```csharp
[Fact]
public void ExportFormats_DefaultToExpected()
{
    var vm = new WorkflowViewModel();
    Assert.True(vm.ExportCkl);
    Assert.False(vm.ExportCsv);
    Assert.False(vm.ExportXccdf);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.ExportFormats_DefaultToExpected" -v n`

Expected: FAIL - properties don't exist

**Step 3: Write minimal implementation**

Add properties to WorkflowViewModel class (after the step error properties):

```csharp
[ObservableProperty]
private bool _exportCkl = true;

[ObservableProperty]
private bool _exportCsv;

[ObservableProperty]
private bool _exportXccdf;
```

Update LoadSettings method to load export formats:

```csharp
private void LoadSettings()
{
    var settings = WorkflowSettings.Load();
    ImportFolderPath = settings.ImportFolderPath;
    EvaluateStigToolPath = settings.EvaluateStigToolPath;
    SccToolPath = settings.SccToolPath;
    OutputFolderPath = settings.OutputFolderPath;
    MachineTarget = settings.MachineTarget;
    ExportCkl = settings.ExportCkl;
    ExportCsv = settings.ExportCsv;
    ExportXccdf = settings.ExportXccdf;
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.ExportFormats_DefaultToExpected" -v n`

Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(vm): add export format properties and load from settings"
```

---

## Task 4: Add Individual Step Run Commands

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write the failing test**

Add to `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`:

```csharp
[Fact]
public void CanRunImport_WhenImportReady_IsTrue()
{
    var vm = new WorkflowViewModel();
    vm.ImportState = StepState.Ready;
    Assert.True(vm.RunImportCommand.CanExecute(null));
}

[Fact]
public void CanRunScan_WhenScanLocked_IsFalse()
{
    var vm = new WorkflowViewModel();
    vm.ScanState = StepState.Locked;
    Assert.False(vm.RunScanCommand.CanExecute(null));
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.CanRunImport" -v n`

Expected: FAIL - commands don't exist

**Step 3: Write minimal implementation**

Add CanExecute properties and commands to WorkflowViewModel:

```csharp
public bool CanRunImport => ImportState == StepState.Ready || ImportState == StepState.Complete || ImportState == StepState.Error;
public bool CanRunScan => ScanState == StepState.Ready || ScanState == StepState.Complete || ScanState == StepState.Error;
public bool CanRunHarden => HardenState == StepState.Ready || HardenState == StepState.Complete || HardenState == StepState.Error;
public bool CanRunVerify => VerifyState == StepState.Ready || VerifyState == StepState.Complete || VerifyState == StepState.Error;

[RelayCommand(CanExecute = nameof(CanRunImport))]
private async Task RunImportStepAsync()
{
    ImportState = StepState.Running;
    ImportError = string.Empty;
    try
    {
        await RunImportAsync();
        ImportState = StepState.Complete;
        if (ScanState == StepState.Locked) ScanState = StepState.Ready;
    }
    catch (Exception ex)
    {
        ImportState = StepState.Error;
        ImportError = ex.Message;
    }
    NotifyStepCommandsCanExecuteChanged();
}

[RelayCommand(CanExecute = nameof(CanRunScan))]
private async Task RunScanStepAsync()
{
    ScanState = StepState.Running;
    ScanError = string.Empty;
    try
    {
        await RunScanAsync();
        ScanState = StepState.Complete;
        if (HardenState == StepState.Locked) HardenState = StepState.Ready;
    }
    catch (Exception ex)
    {
        ScanState = StepState.Error;
        ScanError = ex.Message;
    }
    NotifyStepCommandsCanExecuteChanged();
}

[RelayCommand(CanExecute = nameof(CanRunHarden))]
private async Task RunHardenStepAsync()
{
    HardenState = StepState.Running;
    HardenError = string.Empty;
    try
    {
        await RunHardenAsync();
        HardenState = StepState.Complete;
        if (VerifyState == StepState.Locked) VerifyState = StepState.Ready;
    }
    catch (Exception ex)
    {
        HardenState = StepState.Error;
        HardenError = ex.Message;
    }
    NotifyStepCommandsCanExecuteChanged();
}

[RelayCommand(CanExecute = nameof(CanRunVerify))]
private async Task RunVerifyStepAsync()
{
    VerifyState = StepState.Running;
    VerifyError = string.Empty;
    try
    {
        await RunVerifyAsync();
        VerifyState = StepState.Complete;
    }
    catch (Exception ex)
    {
        VerifyState = StepState.Error;
        VerifyError = ex.Message;
    }
    NotifyStepCommandsCanExecuteChanged();
}

private void NotifyStepCommandsCanExecuteChanged()
{
    RunImportStepCommand.NotifyCanExecuteChanged();
    RunScanStepCommand.NotifyCanExecuteChanged();
    RunHardenStepCommand.NotifyCanExecuteChanged();
    RunVerifyStepCommand.NotifyCanExecuteChanged();
    RunAutoWorkflowCommand.NotifyCanExecuteChanged();
}
```

Also add `[NotifyCanExecuteChangedFor]` attributes to the step state properties:

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(RunImportStepCommand))]
[NotifyCanExecuteChangedFor(nameof(RunAutoWorkflowCommand))]
private StepState _importState = StepState.Ready;

[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(RunScanStepCommand))]
[NotifyCanExecuteChangedFor(nameof(RunAutoWorkflowCommand))]
private StepState _scanState = StepState.Locked;

[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(RunHardenStepCommand))]
[NotifyCanExecuteChangedFor(nameof(RunAutoWorkflowCommand))]
private StepState _hardenState = StepState.Locked;

[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(RunVerifyStepCommand))]
[NotifyCanExecuteChangedFor(nameof(RunAutoWorkflowCommand))]
private StepState _verifyState = StepState.Locked;
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.CanRun" -v n`

Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(vm): add individual step run commands with state management"
```

---

## Task 5: Add Auto Workflow Command

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write the failing test**

Add to `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`:

```csharp
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
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.CanRunAutoWorkflow" -v n`

Expected: FAIL - command doesn't exist

**Step 3: Write minimal implementation**

Add to WorkflowViewModel:

```csharp
public bool CanRunAutoWorkflow => 
    ImportState != StepState.Running && 
    ScanState != StepState.Running && 
    HardenState != StepState.Running && 
    VerifyState != StepState.Running;

[RelayCommand(CanExecute = nameof(CanRunAutoWorkflow))]
private async Task RunAutoWorkflowAsync()
{
    // Run Import
    await RunImportStepAsync();
    if (ImportState == StepState.Error) return;
    
    // Run Scan
    await RunScanStepAsync();
    if (ScanState == StepState.Error) return;
    
    // Run Harden
    await RunHardenStepAsync();
    if (HardenState == StepState.Error) return;
    
    // Run Verify
    await RunVerifyStepAsync();
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.CanRunAutoWorkflow" -v n`

Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(vm): add auto workflow command to run all steps sequentially"
```

---

## Task 6: Add SaveSettings Command for Settings Dialog

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write the failing test**

Add to `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`:

```csharp
[Fact]
public void SaveSettingsCommand_Exists()
{
    var vm = new WorkflowViewModel();
    Assert.NotNull(vm.SaveSettingsCommand);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.SaveSettingsCommand_Exists" -v n`

Expected: FAIL - command doesn't exist

**Step 3: Write minimal implementation**

Update the SaveSettings method to be a command and include export formats:

```csharp
[RelayCommand]
private void SaveSettings()
{
    WorkflowSettings.Save(new WorkflowSettings
    {
        ImportFolderPath = ImportFolderPath,
        EvaluateStigToolPath = EvaluateStigToolPath,
        SccToolPath = SccToolPath,
        OutputFolderPath = OutputFolderPath,
        MachineTarget = MachineTarget,
        ExportCkl = ExportCkl,
        ExportCsv = ExportCsv,
        ExportXccdf = ExportXccdf
    });
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.SaveSettingsCommand_Exists" -v n`

Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(vm): add SaveSettings command with export format persistence"
```

---

## Task 7: Add StepState to Border Brush Converter

**Files:**
- Modify: `src/STIGForge.App/Converters.cs`
- Test: `tests/STIGForge.UnitTests/App/ConverterTests.cs` (new file)

**Step 1: Write the failing test**

Create `tests/STIGForge.UnitTests/App/ConverterTests.cs`:

```csharp
using System.Globalization;
using System.Windows.Media;
using STIGForge.App;

namespace STIGForge.UnitTests.App;

public class ConverterTests
{
    [Theory]
    [InlineData(StepState.Ready, "#3B82F6")]    // Blue
    [InlineData(StepState.Running, "#F59E0B")]  // Amber
    [InlineData(StepState.Complete, "#22C55E")] // Green
    [InlineData(StepState.Locked, "#6B7280")]   // Gray
    [InlineData(StepState.Error, "#EF4444")]    // Red
    public void StepStateToBorderBrushConverter_ReturnsCorrectColor(StepState state, string expectedHex)
    {
        var converter = new StepStateToBorderBrushConverter();
        var result = converter.Convert(state, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        
        Assert.NotNull(result);
        var expected = (Color)ColorConverter.ConvertFromString(expectedHex);
        Assert.Equal(expected, result.Color);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~ConverterTests.StepStateToBorderBrushConverter" -v n`

Expected: FAIL - converter doesn't exist

**Step 3: Write minimal implementation**

Add to `src/STIGForge.App/Converters.cs`:

```csharp
/// <summary>
/// Converts StepState to a border brush color for dashboard step panels.
/// Ready=Blue, Running=Amber, Complete=Green, Locked=Gray, Error=Red
/// </summary>
public sealed class StepStateToBorderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush BlueBrush = new(Color.FromRgb(59, 130, 246));    // Ready
    private static readonly SolidColorBrush AmberBrush = new(Color.FromRgb(245, 158, 11));   // Running
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(34, 197, 94));    // Complete
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(107, 114, 128));   // Locked
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(239, 68, 68));      // Error

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            StepState.Ready => BlueBrush,
            StepState.Running => AmberBrush,
            StepState.Complete => GreenBrush,
            StepState.Locked => GrayBrush,
            StepState.Error => RedBrush,
            _ => GrayBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~ConverterTests.StepStateToBorderBrushConverter" -v n`

Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/Converters.cs tests/STIGForge.UnitTests/App/ConverterTests.cs
git commit -m "feat(converters): add StepStateToBorderBrushConverter for dashboard panels"
```

---

## Task 8: Add StepState to Visibility Converters

**Files:**
- Modify: `src/STIGForge.App/Converters.cs`
- Test: `tests/STIGForge.UnitTests/App/ConverterTests.cs`

**Step 1: Write the failing test**

Add to `tests/STIGForge.UnitTests/App/ConverterTests.cs`:

```csharp
[Theory]
[InlineData(StepState.Running, Visibility.Visible)]
[InlineData(StepState.Ready, Visibility.Collapsed)]
[InlineData(StepState.Complete, Visibility.Collapsed)]
public void StepStateToRunningVisibilityConverter_ReturnsCorrect(StepState state, Visibility expected)
{
    var converter = new StepStateToRunningVisibilityConverter();
    var result = converter.Convert(state, typeof(Visibility), null, CultureInfo.InvariantCulture);
    Assert.Equal(expected, result);
}

[Theory]
[InlineData(StepState.Complete, Visibility.Visible)]
[InlineData(StepState.Ready, Visibility.Collapsed)]
[InlineData(StepState.Running, Visibility.Collapsed)]
public void StepStateToCompleteVisibilityConverter_ReturnsCorrect(StepState state, Visibility expected)
{
    var converter = new StepStateToCompleteVisibilityConverter();
    var result = converter.Convert(state, typeof(Visibility), null, CultureInfo.InvariantCulture);
    Assert.Equal(expected, result);
}

[Theory]
[InlineData(StepState.Error, Visibility.Visible)]
[InlineData(StepState.Ready, Visibility.Collapsed)]
[InlineData(StepState.Running, Visibility.Collapsed)]
public void StepStateToErrorVisibilityConverter_ReturnsCorrect(StepState state, Visibility expected)
{
    var converter = new StepStateToErrorVisibilityConverter();
    var result = converter.Convert(state, typeof(Visibility), null, CultureInfo.InvariantCulture);
    Assert.Equal(expected, result);
}
```

Add using statement at top:
```csharp
using System.Windows;
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~ConverterTests.StepStateTo" -v n`

Expected: FAIL - converters don't exist

**Step 3: Write minimal implementation**

Add to `src/STIGForge.App/Converters.cs`:

```csharp
/// <summary>
/// Returns Visible when StepState is Running, Collapsed otherwise.
/// </summary>
public sealed class StepStateToRunningVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is StepState.Running ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Returns Visible when StepState is Complete, Collapsed otherwise.
/// </summary>
public sealed class StepStateToCompleteVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is StepState.Complete ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Returns Visible when StepState is Error, Collapsed otherwise.
/// </summary>
public sealed class StepStateToErrorVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is StepState.Error ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~ConverterTests.StepStateTo" -v n`

Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/Converters.cs tests/STIGForge.UnitTests/App/ConverterTests.cs
git commit -m "feat(converters): add StepState visibility converters for panel indicators"
```

---

## Task 9: Register New Converters in App.xaml

**Files:**
- Modify: `src/STIGForge.App/App.xaml:9-14`

**Step 1: No test needed (XAML registration)**

**Step 2: Add converter registrations**

Add after line 14 in `src/STIGForge.App/App.xaml`:

```xml
<local:StepStateToBorderBrushConverter x:Key="StepStateToBorderBrush" />
<local:StepStateToRunningVisibilityConverter x:Key="StepStateToRunningVisibility" />
<local:StepStateToCompleteVisibilityConverter x:Key="StepStateToCompleteVisibility" />
<local:StepStateToErrorVisibilityConverter x:Key="StepStateToErrorVisibility" />
```

**Step 3: Build to verify**

Run: `dotnet build src/STIGForge.App`

Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/STIGForge.App/App.xaml
git commit -m "feat(app): register StepState converters in App.xaml"
```

---

## Task 10: Create SettingsWindow XAML

**Files:**
- Create: `src/STIGForge.App/Views/SettingsWindow.xaml`
- Create: `src/STIGForge.App/Views/SettingsWindow.xaml.cs`

**Step 1: Create SettingsWindow.xaml**

Create `src/STIGForge.App/Views/SettingsWindow.xaml`:

```xml
<Window x:Class="STIGForge.App.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Settings" Height="500" Width="550"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize"
        Background="{DynamicResource WindowBackgroundBrush}">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <ScrollViewer Grid.Row="0" VerticalScrollBarVisibility="Auto">
            <StackPanel>
                <!-- Paths Section -->
                <TextBlock Text="Paths" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,16"/>

                <TextBlock Text="Import Folder" FontWeight="SemiBold" Margin="0,0,0,4"/>
                <Grid Margin="0,0,0,12">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBox Text="{Binding ImportFolderPath, UpdateSourceTrigger=PropertyChanged}" Height="32" VerticalContentAlignment="Center"/>
                    <Button Grid.Column="1" Content="Browse..." Width="80" Height="32" Margin="8,0,0,0" Command="{Binding BrowseImportFolderCommand}"/>
                </Grid>

                <TextBlock Text="Evaluate-STIG Tool Path" FontWeight="SemiBold" Margin="0,0,0,4"/>
                <Grid Margin="0,0,0,12">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBox Text="{Binding EvaluateStigToolPath, UpdateSourceTrigger=PropertyChanged}" Height="32" VerticalContentAlignment="Center"/>
                    <Button Grid.Column="1" Content="Browse..." Width="80" Height="32" Margin="8,0,0,0" Command="{Binding BrowseEvaluateStigCommand}"/>
                </Grid>

                <TextBlock Text="SCC Tool Path (Optional)" FontWeight="SemiBold" Margin="0,0,0,4"/>
                <Grid Margin="0,0,0,12">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBox Text="{Binding SccToolPath, UpdateSourceTrigger=PropertyChanged}" Height="32" VerticalContentAlignment="Center"/>
                    <Button Grid.Column="1" Content="Browse..." Width="80" Height="32" Margin="8,0,0,0" Command="{Binding BrowseSccCommand}"/>
                </Grid>

                <TextBlock Text="Output Folder" FontWeight="SemiBold" Margin="0,0,0,4"/>
                <Grid Margin="0,0,0,12">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBox Text="{Binding OutputFolderPath, UpdateSourceTrigger=PropertyChanged}" Height="32" VerticalContentAlignment="Center"/>
                    <Button Grid.Column="1" Content="Browse..." Width="80" Height="32" Margin="8,0,0,0" Command="{Binding BrowseOutputFolderCommand}"/>
                </Grid>

                <TextBlock Text="Machine Target" FontWeight="SemiBold" Margin="0,0,0,4"/>
                <TextBox Text="{Binding MachineTarget, UpdateSourceTrigger=PropertyChanged}" Height="32" Width="300" HorizontalAlignment="Left" VerticalContentAlignment="Center" Margin="0,0,0,4"/>
                <TextBlock Text="Use 'localhost' for local machine or enter a remote hostname" Foreground="{DynamicResource TextMutedBrush}" Margin="0,0,0,20"/>

                <!-- Export Formats Section -->
                <Separator Margin="0,0,0,16"/>
                <TextBlock Text="Export Formats" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,16"/>

                <CheckBox Content="CKL (STIG Viewer Checklist)" IsChecked="{Binding ExportCkl}" Margin="0,0,0,8"/>
                <CheckBox Content="CSV (Spreadsheet)" IsChecked="{Binding ExportCsv}" Margin="0,0,0,8"/>
                <CheckBox Content="XCCDF (SCAP Results)" IsChecked="{Binding ExportXccdf}" Margin="0,0,0,8"/>
            </StackPanel>
        </ScrollViewer>

        <!-- Buttons -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
            <Button Content="Cancel" Width="100" Height="36" Margin="0,0,10,0" IsCancel="True" Click="CancelButton_Click"/>
            <Button Content="Save" Width="100" Height="36" IsDefault="True" Click="SaveButton_Click"/>
        </StackPanel>
    </Grid>
</Window>
```

**Step 2: Create SettingsWindow.xaml.cs**

Create `src/STIGForge.App/Views/SettingsWindow.xaml.cs`:

```csharp
using System.Windows;

namespace STIGForge.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkflowViewModel vm)
        {
            vm.SaveSettingsCommand.Execute(null);
        }
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

**Step 3: Build to verify**

Run: `dotnet build src/STIGForge.App`

Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/STIGForge.App/Views/SettingsWindow.xaml src/STIGForge.App/Views/SettingsWindow.xaml.cs
git commit -m "feat(views): create SettingsWindow modal dialog"
```

---

## Task 11: Add ShowSettings Command to ViewModel

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`

**Step 1: Add ShowSettings command**

Add to WorkflowViewModel:

```csharp
[RelayCommand]
private void ShowSettings()
{
    var settingsWindow = new Views.SettingsWindow
    {
        DataContext = this,
        Owner = Application.Current.MainWindow
    };
    
    // Reload settings if cancelled
    if (settingsWindow.ShowDialog() != true)
    {
        LoadSettings();
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build src/STIGForge.App`

Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs
git commit -m "feat(vm): add ShowSettings command for settings modal"
```

---

## Task 12: Create DashboardView XAML

**Files:**
- Create: `src/STIGForge.App/Views/DashboardView.xaml`
- Create: `src/STIGForge.App/Views/DashboardView.xaml.cs`

**Step 1: Create DashboardView.xaml**

Create `src/STIGForge.App/Views/DashboardView.xaml`:

```xml
<UserControl x:Class="STIGForge.App.Views.DashboardView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:STIGForge.App">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Step Panels -->
        <UniformGrid Grid.Row="0" Rows="1" Columns="4" Margin="0,0,0,20">
            <!-- Import Panel -->
            <Border Margin="5" Padding="16" CornerRadius="8" 
                    BorderThickness="3" BorderBrush="{Binding ImportState, Converter={StaticResource StepStateToBorderBrush}}">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    
                    <TextBlock Grid.Row="0" Text="1" FontSize="36" FontWeight="Bold" HorizontalAlignment="Center" Opacity="0.3"/>
                    <TextBlock Grid.Row="1" Text="IMPORT" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" Margin="0,0,0,10"/>
                    
                    <StackPanel Grid.Row="2" VerticalAlignment="Center" HorizontalAlignment="Center">
                        <!-- Running indicator -->
                        <ProgressBar IsIndeterminate="True" Height="4" Width="80" Margin="0,0,0,8"
                                     Visibility="{Binding ImportState, Converter={StaticResource StepStateToRunningVisibility}}"/>
                        <!-- Complete indicator -->
                        <TextBlock Text="&#x2713;" FontSize="32" Foreground="Green" HorizontalAlignment="Center"
                                   Visibility="{Binding ImportState, Converter={StaticResource StepStateToCompleteVisibility}}"/>
                        <!-- Error indicator -->
                        <TextBlock Text="&#x2717;" FontSize="32" Foreground="Red" HorizontalAlignment="Center"
                                   Visibility="{Binding ImportState, Converter={StaticResource StepStateToErrorVisibility}}"/>
                        <TextBlock Text="{Binding ImportError}" Foreground="Red" TextWrapping="Wrap" MaxWidth="120"
                                   Visibility="{Binding ImportState, Converter={StaticResource StepStateToErrorVisibility}}"/>
                    </StackPanel>
                    
                    <Button Grid.Row="3" Content="Run" Width="80" Height="32" HorizontalAlignment="Center"
                            Command="{Binding RunImportStepCommand}"/>
                </Grid>
            </Border>

            <!-- Scan Panel -->
            <Border Margin="5" Padding="16" CornerRadius="8"
                    BorderThickness="3" BorderBrush="{Binding ScanState, Converter={StaticResource StepStateToBorderBrush}}">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    
                    <TextBlock Grid.Row="0" Text="2" FontSize="36" FontWeight="Bold" HorizontalAlignment="Center" Opacity="0.3"/>
                    <TextBlock Grid.Row="1" Text="SCAN" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" Margin="0,0,0,10"/>
                    
                    <StackPanel Grid.Row="2" VerticalAlignment="Center" HorizontalAlignment="Center">
                        <ProgressBar IsIndeterminate="True" Height="4" Width="80" Margin="0,0,0,8"
                                     Visibility="{Binding ScanState, Converter={StaticResource StepStateToRunningVisibility}}"/>
                        <TextBlock Text="&#x2713;" FontSize="32" Foreground="Green" HorizontalAlignment="Center"
                                   Visibility="{Binding ScanState, Converter={StaticResource StepStateToCompleteVisibility}}"/>
                        <TextBlock Text="&#x2717;" FontSize="32" Foreground="Red" HorizontalAlignment="Center"
                                   Visibility="{Binding ScanState, Converter={StaticResource StepStateToErrorVisibility}}"/>
                        <TextBlock Text="{Binding ScanError}" Foreground="Red" TextWrapping="Wrap" MaxWidth="120"
                                   Visibility="{Binding ScanState, Converter={StaticResource StepStateToErrorVisibility}}"/>
                    </StackPanel>
                    
                    <Button Grid.Row="3" Content="Run" Width="80" Height="32" HorizontalAlignment="Center"
                            Command="{Binding RunScanStepCommand}"/>
                </Grid>
            </Border>

            <!-- Harden Panel -->
            <Border Margin="5" Padding="16" CornerRadius="8"
                    BorderThickness="3" BorderBrush="{Binding HardenState, Converter={StaticResource StepStateToBorderBrush}}">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    
                    <TextBlock Grid.Row="0" Text="3" FontSize="36" FontWeight="Bold" HorizontalAlignment="Center" Opacity="0.3"/>
                    <TextBlock Grid.Row="1" Text="HARDEN" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" Margin="0,0,0,10"/>
                    
                    <StackPanel Grid.Row="2" VerticalAlignment="Center" HorizontalAlignment="Center">
                        <ProgressBar IsIndeterminate="True" Height="4" Width="80" Margin="0,0,0,8"
                                     Visibility="{Binding HardenState, Converter={StaticResource StepStateToRunningVisibility}}"/>
                        <TextBlock Text="&#x2713;" FontSize="32" Foreground="Green" HorizontalAlignment="Center"
                                   Visibility="{Binding HardenState, Converter={StaticResource StepStateToCompleteVisibility}}"/>
                        <TextBlock Text="&#x2717;" FontSize="32" Foreground="Red" HorizontalAlignment="Center"
                                   Visibility="{Binding HardenState, Converter={StaticResource StepStateToErrorVisibility}}"/>
                        <TextBlock Text="{Binding HardenError}" Foreground="Red" TextWrapping="Wrap" MaxWidth="120"
                                   Visibility="{Binding HardenState, Converter={StaticResource StepStateToErrorVisibility}}"/>
                    </StackPanel>
                    
                    <Button Grid.Row="3" Content="Run" Width="80" Height="32" HorizontalAlignment="Center"
                            Command="{Binding RunHardenStepCommand}"/>
                </Grid>
            </Border>

            <!-- Verify Panel -->
            <Border Margin="5" Padding="16" CornerRadius="8"
                    BorderThickness="3" BorderBrush="{Binding VerifyState, Converter={StaticResource StepStateToBorderBrush}}">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    
                    <TextBlock Grid.Row="0" Text="4" FontSize="36" FontWeight="Bold" HorizontalAlignment="Center" Opacity="0.3"/>
                    <TextBlock Grid.Row="1" Text="VERIFY" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" Margin="0,0,0,10"/>
                    
                    <StackPanel Grid.Row="2" VerticalAlignment="Center" HorizontalAlignment="Center">
                        <ProgressBar IsIndeterminate="True" Height="4" Width="80" Margin="0,0,0,8"
                                     Visibility="{Binding VerifyState, Converter={StaticResource StepStateToRunningVisibility}}"/>
                        <TextBlock Text="&#x2713;" FontSize="32" Foreground="Green" HorizontalAlignment="Center"
                                   Visibility="{Binding VerifyState, Converter={StaticResource StepStateToCompleteVisibility}}"/>
                        <TextBlock Text="&#x2717;" FontSize="32" Foreground="Red" HorizontalAlignment="Center"
                                   Visibility="{Binding VerifyState, Converter={StaticResource StepStateToErrorVisibility}}"/>
                        <TextBlock Text="{Binding VerifyError}" Foreground="Red" TextWrapping="Wrap" MaxWidth="120"
                                   Visibility="{Binding VerifyState, Converter={StaticResource StepStateToErrorVisibility}}"/>
                    </StackPanel>
                    
                    <Button Grid.Row="3" Content="Run" Width="80" Height="32" HorizontalAlignment="Center"
                            Command="{Binding RunVerifyStepCommand}"/>
                </Grid>
            </Border>
        </UniformGrid>

        <!-- Auto Workflow Button -->
        <Button Grid.Row="1" Content="&#x25B6; Auto Workflow" Width="180" Height="40" FontSize="14" FontWeight="SemiBold"
                HorizontalAlignment="Center" Margin="0,0,0,20"
                Command="{Binding RunAutoWorkflowCommand}"/>

        <!-- Results Section -->
        <Border Grid.Row="2" Padding="16" CornerRadius="8" BorderThickness="1" BorderBrush="{DynamicResource BorderBrush}">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Grid.Column="0" Text="mission.json:" FontWeight="SemiBold" Margin="0,0,10,8"/>
                <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding MissionJsonPath, Mode=OneWay}" IsReadOnly="True" 
                         VerticalContentAlignment="Center" Height="28"/>

                <TextBlock Grid.Row="1" Grid.Column="0" Text="Output folder:" FontWeight="SemiBold" Margin="0,0,10,0"/>
                <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding OutputFolderPath, Mode=OneWay}" IsReadOnly="True" 
                         VerticalContentAlignment="Center" Height="28"/>
                <Button Grid.Row="1" Grid.Column="2" Content="Open" Width="70" Height="28" Margin="10,0,0,0"
                        Command="{Binding OpenOutputFolderCommand}"/>
            </Grid>
        </Border>
    </Grid>
</UserControl>
```

**Step 2: Create DashboardView.xaml.cs**

Create `src/STIGForge.App/Views/DashboardView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace STIGForge.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }
}
```

**Step 3: Build to verify**

Run: `dotnet build src/STIGForge.App`

Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/STIGForge.App/Views/DashboardView.xaml src/STIGForge.App/Views/DashboardView.xaml.cs
git commit -m "feat(views): create DashboardView with 4 step panels"
```

---

## Task 13: Update MainWindow with Dashboard Layout

**Files:**
- Modify: `src/STIGForge.App/MainWindow.xaml`

**Step 1: Replace MainWindow.xaml content**

Replace entire content of `src/STIGForge.App/MainWindow.xaml`:

```xml
<Window x:Class="STIGForge.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:STIGForge.App.Views"
        Title="STIGForge" Height="600" Width="900" MinHeight="500" MinWidth="800"
        WindowStartupLocation="CenterScreen"
        Background="{DynamicResource WindowBackgroundBrush}">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Header with title and corner buttons -->
        <DockPanel Grid.Row="0" Margin="0,0,0,20">
            <TextBlock Text="STIGForge" FontSize="28" FontWeight="SemiBold" VerticalAlignment="Center"/>
            
            <!-- Corner Buttons -->
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                <Button Content="?" Width="36" Height="36" Margin="0,0,8,0" ToolTip="Help"
                        FontSize="16" FontWeight="Bold"/>
                <Button Content="i" Width="36" Height="36" Margin="0,0,8,0" ToolTip="About"
                        FontSize="16" FontWeight="Bold" FontStyle="Italic"
                        Command="{Binding ShowAboutCommand}"/>
                <Button Content="&#x2699;" Width="36" Height="36" ToolTip="Settings"
                        FontSize="18"
                        Command="{Binding ShowSettingsCommand}"/>
            </StackPanel>
        </DockPanel>

        <!-- Dashboard -->
        <views:DashboardView Grid.Row="1"/>
    </Grid>
</Window>
```

**Step 2: Build to verify**

Run: `dotnet build src/STIGForge.App`

Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/STIGForge.App/MainWindow.xaml
git commit -m "feat(mainwindow): replace wizard with dashboard layout and corner buttons"
```

---

## Task 14: Update RestartWorkflow to Reset Step States

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write the failing test**

Add to `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`:

```csharp
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
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.RestartWorkflow_ResetsStepStates" -v n`

Expected: FAIL - step states not reset

**Step 3: Update RestartWorkflow method**

Update the RestartWorkflow method in `src/STIGForge.App/WorkflowViewModel.cs`:

```csharp
[RelayCommand]
private void RestartWorkflow()
{
    CurrentStep = WorkflowStep.Setup;
    BaselineFindingsCount = 0;
    VerifyFindingsCount = 0;
    FixedCount = 0;
    AppliedFixesCount = 0;
    ImportedItemsCount = 0;
    ImportedItems = new List<string>();
    MissionJsonPath = string.Empty;
    StatusText = string.Empty;
    ProgressValue = 0;
    
    // Reset step states
    ImportState = StepState.Ready;
    ScanState = StepState.Locked;
    HardenState = StepState.Locked;
    VerifyState = StepState.Locked;
    ImportError = string.Empty;
    ScanError = string.Empty;
    HardenError = string.Empty;
    VerifyError = string.Empty;
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests.RestartWorkflow_ResetsStepStates" -v n`

Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(vm): reset step states in RestartWorkflow"
```

---

## Task 15: Run All Tests and Final Build

**Files:** None (verification only)

**Step 1: Run all tests**

Run: `dotnet test tests/STIGForge.UnitTests -v n`

Expected: All tests pass (should be 599+ tests)

**Step 2: Build the application**

Run: `dotnet build src/STIGForge.App -c Release`

Expected: Build succeeded

**Step 3: Final commit**

```bash
git add -A
git commit -m "chore: complete dashboard UI redesign implementation"
```

---

## Summary

This plan transforms the STIGForge app from a 6-step wizard to a 4-panel dashboard:

| Task | Description |
|------|-------------|
| 1 | Add export format properties to WorkflowSettings |
| 2 | Add StepState enum and step state properties |
| 3 | Add export format properties to ViewModel |
| 4 | Add individual step run commands |
| 5 | Add auto workflow command |
| 6 | Add SaveSettings command |
| 7 | Add StepState to border brush converter |
| 8 | Add StepState visibility converters |
| 9 | Register converters in App.xaml |
| 10 | Create SettingsWindow modal |
| 11 | Add ShowSettings command |
| 12 | Create DashboardView |
| 13 | Update MainWindow with dashboard layout |
| 14 | Update RestartWorkflow for step states |
| 15 | Final verification and build |

Total: 15 tasks, each with TDD approach where applicable.

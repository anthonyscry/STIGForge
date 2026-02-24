# Workflow App Redesign Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the multi-tab STIGForge App with a wizard-style 6-step workflow (Setup → Import → Scan → Harden → Verify → Done) and remove the CLI project entirely.

**Architecture:** Single `WorkflowViewModel` drives a `WorkflowWizardView` that swaps step content based on `CurrentStep` enum. Each step has its own UserControl. Services from existing libraries handle actual work. Settings persist to JSON in AppData.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, xUnit, existing STIGForge.Core/Infrastructure/Apply/Verify libraries.

---

### Task 1: Remove CLI project from solution

**Files:**
- Delete: `src/STIGForge.Cli/` (entire directory)
- Modify: `STIGForge.sln`

**Step 1: Remove CLI project reference from solution**

Run:
```bash
cd /mnt/c/projects/STIGForge
dotnet sln remove src/STIGForge.Cli/STIGForge.Cli.csproj
```

**Step 2: Delete CLI directory**

Run:
```bash
rm -rf src/STIGForge.Cli
```

**Step 3: Remove CLI test references if any**

Run:
```bash
grep -r "STIGForge.Cli" tests/ --include="*.csproj" || echo "No CLI test references"
```

If found, remove the `<ProjectReference>` from test csproj files.

**Step 4: Verify solution builds**

Run:
```powershell
dotnet build STIGForge.sln
```
Expected: Build succeeded (CLI errors gone)

**Step 5: Commit**

```bash
git add -A
git commit -m "chore: remove CLI project - app is GUI-only"
```

---

### Task 2: Delete old Views and ViewModels

**Files:**
- Delete: All files in `src/STIGForge.App/Views/` except `AboutDialog.xaml` and `AboutDialog.xaml.cs`
- Delete: `src/STIGForge.App/MainViewModel*.cs` (all partials)
- Delete: `src/STIGForge.App/OverlayEditorViewModel.cs`

**Step 1: Delete old Views**

Run:
```bash
cd /mnt/c/projects/STIGForge/src/STIGForge.App/Views
ls *.xaml | grep -v AboutDialog | xargs rm -f
ls *.cs | grep -v AboutDialog | xargs rm -f
```

**Step 2: Delete old ViewModels**

Run:
```bash
cd /mnt/c/projects/STIGForge/src/STIGForge.App
rm -f MainViewModel*.cs OverlayEditorViewModel.cs
```

**Step 3: Verify remaining files**

Run:
```bash
ls src/STIGForge.App/Views/
ls src/STIGForge.App/*.cs
```
Expected: Only `AboutDialog.xaml`, `AboutDialog.xaml.cs`, `App.xaml.cs`, `MainWindow.xaml.cs`

**Step 4: Commit**

```bash
git add -A
git commit -m "chore: delete old Views and ViewModels for clean slate"
```

---

### Task 3: Create WorkflowStep enum and WorkflowViewModel skeleton

**Files:**
- Create: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write the failing test**

```csharp
// tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
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
}
```

**Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests" --no-restore
```
Expected: FAIL - `WorkflowViewModel` does not exist

**Step 3: Write minimal implementation**

```csharp
// src/STIGForge.App/WorkflowViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace STIGForge.App;

public enum WorkflowStep
{
    Setup,
    Import,
    Scan,
    Harden,
    Verify,
    Done
}

public partial class WorkflowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private WorkflowStep _currentStep = WorkflowStep.Setup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string _importFolderPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string _evaluateStigToolPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string _sccToolPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string _outputFolderPath = string.Empty;

    [ObservableProperty]
    private string _machineTarget = "localhost";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMax = 100;

    public bool CanGoBack => CurrentStep > WorkflowStep.Setup && CurrentStep < WorkflowStep.Done;

    public bool CanGoNext => CurrentStep switch
    {
        WorkflowStep.Setup => !string.IsNullOrWhiteSpace(ImportFolderPath)
                           && !string.IsNullOrWhiteSpace(EvaluateStigToolPath)
                           && !string.IsNullOrWhiteSpace(OutputFolderPath),
        WorkflowStep.Done => false,
        _ => !IsBusy
    };

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (CurrentStep > WorkflowStep.Setup)
            CurrentStep = CurrentStep - 1;
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task GoNextAsync()
    {
        if (CurrentStep < WorkflowStep.Done)
            CurrentStep = CurrentStep + 1;
    }
}
```

**Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModelTests" --no-restore
```
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(app): add WorkflowViewModel skeleton with step navigation"
```

---

### Task 4: Create WorkflowWizardView with step content switching

**Files:**
- Create: `src/STIGForge.App/Views/WorkflowWizardView.xaml`
- Create: `src/STIGForge.App/Views/WorkflowWizardView.xaml.cs`

**Step 1: Create the view XAML**

```xml
<!-- src/STIGForge.App/Views/WorkflowWizardView.xaml -->
<UserControl x:Class="STIGForge.App.Views.WorkflowWizardView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:STIGForge.App">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Step Indicator -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,20">
            <TextBlock Text="1. Setup" Margin="10,0" FontWeight="{Binding CurrentStep, Converter={StaticResource StepFontWeightConverter}, ConverterParameter=Setup}"/>
            <TextBlock Text="→" Margin="5,0"/>
            <TextBlock Text="2. Import" Margin="10,0" FontWeight="{Binding CurrentStep, Converter={StaticResource StepFontWeightConverter}, ConverterParameter=Import}"/>
            <TextBlock Text="→" Margin="5,0"/>
            <TextBlock Text="3. Scan" Margin="10,0" FontWeight="{Binding CurrentStep, Converter={StaticResource StepFontWeightConverter}, ConverterParameter=Scan}"/>
            <TextBlock Text="→" Margin="5,0"/>
            <TextBlock Text="4. Harden" Margin="10,0" FontWeight="{Binding CurrentStep, Converter={StaticResource StepFontWeightConverter}, ConverterParameter=Harden}"/>
            <TextBlock Text="→" Margin="5,0"/>
            <TextBlock Text="5. Verify" Margin="10,0" FontWeight="{Binding CurrentStep, Converter={StaticResource StepFontWeightConverter}, ConverterParameter=Verify}"/>
            <TextBlock Text="→" Margin="5,0"/>
            <TextBlock Text="6. Done" Margin="10,0" FontWeight="{Binding CurrentStep, Converter={StaticResource StepFontWeightConverter}, ConverterParameter=Done}"/>
        </StackPanel>

        <!-- Step Content -->
        <ContentControl Grid.Row="1" Content="{Binding}" Margin="20,0">
            <ContentControl.Style>
                <Style TargetType="ContentControl">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding CurrentStep}" Value="Setup">
                            <Setter Property="ContentTemplate" Value="{StaticResource SetupStepTemplate}"/>
                        </DataTrigger>
                        <DataTrigger Binding="{Binding CurrentStep}" Value="Import">
                            <Setter Property="ContentTemplate" Value="{StaticResource ImportStepTemplate}"/>
                        </DataTrigger>
                        <DataTrigger Binding="{Binding CurrentStep}" Value="Scan">
                            <Setter Property="ContentTemplate" Value="{StaticResource ScanStepTemplate}"/>
                        </DataTrigger>
                        <DataTrigger Binding="{Binding CurrentStep}" Value="Harden">
                            <Setter Property="ContentTemplate" Value="{StaticResource HardenStepTemplate}"/>
                        </DataTrigger>
                        <DataTrigger Binding="{Binding CurrentStep}" Value="Verify">
                            <Setter Property="ContentTemplate" Value="{StaticResource VerifyStepTemplate}"/>
                        </DataTrigger>
                        <DataTrigger Binding="{Binding CurrentStep}" Value="Done">
                            <Setter Property="ContentTemplate" Value="{StaticResource DoneStepTemplate}"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </ContentControl.Style>
        </ContentControl>

        <!-- Navigation Buttons -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
            <Button Content="← Back" Width="100" Height="36" Margin="0,0,10,0"
                    Command="{Binding GoBackCommand}"
                    IsEnabled="{Binding CanGoBack}"/>
            <Button Content="Next →" Width="100" Height="36"
                    Command="{Binding GoNextCommand}"
                    IsEnabled="{Binding CanGoNext}"/>
        </StackPanel>
    </Grid>
</UserControl>
```

**Step 2: Create the code-behind**

```csharp
// src/STIGForge.App/Views/WorkflowWizardView.xaml.cs
using System.Windows.Controls;

namespace STIGForge.App.Views;

public partial class WorkflowWizardView : UserControl
{
    public WorkflowWizardView()
    {
        InitializeComponent();
    }
}
```

**Step 3: Verify it compiles**

Run:
```powershell
dotnet build src/STIGForge.App/STIGForge.App.csproj
```
Expected: Build succeeded (may have warnings about missing templates - that's OK for now)

**Step 4: Commit**

```bash
git add src/STIGForge.App/Views/WorkflowWizardView.xaml src/STIGForge.App/Views/WorkflowWizardView.xaml.cs
git commit -m "feat(app): add WorkflowWizardView with step content switching"
```

---

### Task 5: Create SetupStepTemplate with path inputs

**Files:**
- Modify: `src/STIGForge.App/App.xaml` (add DataTemplates)

**Step 1: Add SetupStepTemplate to App.xaml Resources**

Add inside `<Application.Resources>`:

```xml
<!-- Setup Step Template -->
<DataTemplate x:Key="SetupStepTemplate">
    <StackPanel MaxWidth="600">
        <TextBlock Text="Step 1: Setup" FontSize="24" FontWeight="SemiBold" Margin="0,0,0,20"/>
        <TextBlock Text="Configure paths for the workflow. Paths will be auto-detected where possible." 
                   Foreground="{DynamicResource TextMutedBrush}" Margin="0,0,0,20"/>

        <TextBlock Text="Import Folder" FontWeight="SemiBold" Margin="0,0,0,4"/>
        <Grid Margin="0,0,0,16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox Text="{Binding ImportFolderPath, UpdateSourceTrigger=PropertyChanged}" Height="32" VerticalContentAlignment="Center"/>
            <Button Grid.Column="1" Content="Browse..." Width="80" Height="32" Margin="8,0,0,0" Command="{Binding BrowseImportFolderCommand}"/>
        </Grid>

        <TextBlock Text="Evaluate-STIG Tool Path" FontWeight="SemiBold" Margin="0,0,0,4"/>
        <Grid Margin="0,0,0,16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox Text="{Binding EvaluateStigToolPath, UpdateSourceTrigger=PropertyChanged}" Height="32" VerticalContentAlignment="Center"/>
            <Button Grid.Column="1" Content="Browse..." Width="80" Height="32" Margin="8,0,0,0" Command="{Binding BrowseEvaluateStigCommand}"/>
        </Grid>

        <TextBlock Text="SCC Tool Path (Optional)" FontWeight="SemiBold" Margin="0,0,0,4"/>
        <Grid Margin="0,0,0,16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox Text="{Binding SccToolPath, UpdateSourceTrigger=PropertyChanged}" Height="32" VerticalContentAlignment="Center"/>
            <Button Grid.Column="1" Content="Browse..." Width="80" Height="32" Margin="8,0,0,0" Command="{Binding BrowseSccCommand}"/>
        </Grid>

        <TextBlock Text="Output Folder" FontWeight="SemiBold" Margin="0,0,0,4"/>
        <Grid Margin="0,0,0,16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox Text="{Binding OutputFolderPath, UpdateSourceTrigger=PropertyChanged}" Height="32" VerticalContentAlignment="Center"/>
            <Button Grid.Column="1" Content="Browse..." Width="80" Height="32" Margin="8,0,0,0" Command="{Binding BrowseOutputFolderCommand}"/>
        </Grid>

        <TextBlock Text="Machine Target" FontWeight="SemiBold" Margin="0,0,0,4"/>
        <TextBox Text="{Binding MachineTarget, UpdateSourceTrigger=PropertyChanged}" Height="32" Width="300" HorizontalAlignment="Left" VerticalContentAlignment="Center"/>
        <TextBlock Text="Use 'localhost' for local machine or enter a remote hostname" Foreground="{DynamicResource TextMutedBrush}" Margin="0,4,0,0"/>
    </StackPanel>
</DataTemplate>
```

**Step 2: Add Browse commands to WorkflowViewModel**

Add to `WorkflowViewModel.cs`:

```csharp
[RelayCommand]
private void BrowseImportFolder()
{
    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Import Folder" };
    if (dialog.ShowDialog() == true)
        ImportFolderPath = dialog.FolderName;
}

[RelayCommand]
private void BrowseEvaluateStig()
{
    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Evaluate-STIG Folder" };
    if (dialog.ShowDialog() == true)
        EvaluateStigToolPath = dialog.FolderName;
}

[RelayCommand]
private void BrowseScc()
{
    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select SCC Folder" };
    if (dialog.ShowDialog() == true)
        SccToolPath = dialog.FolderName;
}

[RelayCommand]
private void BrowseOutputFolder()
{
    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Output Folder" };
    if (dialog.ShowDialog() == true)
        OutputFolderPath = dialog.FolderName;
}
```

**Step 3: Verify it compiles**

Run:
```powershell
dotnet build src/STIGForge.App/STIGForge.App.csproj
```
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/STIGForge.App/App.xaml src/STIGForge.App/WorkflowViewModel.cs
git commit -m "feat(app): add SetupStepTemplate with path inputs and browse commands"
```

---

### Task 6: Create placeholder templates for remaining steps

**Files:**
- Modify: `src/STIGForge.App/App.xaml`

**Step 1: Add placeholder templates**

Add to `<Application.Resources>`:

```xml
<!-- Import Step Template -->
<DataTemplate x:Key="ImportStepTemplate">
    <StackPanel MaxWidth="600">
        <TextBlock Text="Step 2: Import" FontSize="24" FontWeight="SemiBold" Margin="0,0,0,20"/>
        <TextBlock Text="Importing STIG content from the import folder..." Margin="0,0,0,20"/>
        <ProgressBar Height="20" IsIndeterminate="{Binding IsBusy}" Value="{Binding ProgressValue}" Maximum="{Binding ProgressMax}"/>
        <TextBlock Text="{Binding StatusText}" Margin="0,10,0,0"/>
    </StackPanel>
</DataTemplate>

<!-- Scan Step Template -->
<DataTemplate x:Key="ScanStepTemplate">
    <StackPanel MaxWidth="600">
        <TextBlock Text="Step 3: Scan" FontSize="24" FontWeight="SemiBold" Margin="0,0,0,20"/>
        <TextBlock Text="Running Evaluate-STIG to establish baseline findings..." Margin="0,0,0,20"/>
        <ProgressBar Height="20" IsIndeterminate="{Binding IsBusy}" Value="{Binding ProgressValue}" Maximum="{Binding ProgressMax}"/>
        <TextBlock Text="{Binding StatusText}" Margin="0,10,0,0"/>
    </StackPanel>
</DataTemplate>

<!-- Harden Step Template -->
<DataTemplate x:Key="HardenStepTemplate">
    <StackPanel MaxWidth="600">
        <TextBlock Text="Step 4: Harden" FontSize="24" FontWeight="SemiBold" Margin="0,0,0,20"/>
        <TextBlock Text="Applying PowerSTIG/DSC configurations, GPO settings, and ADMX templates..." Margin="0,0,0,20"/>
        <ProgressBar Height="20" IsIndeterminate="{Binding IsBusy}" Value="{Binding ProgressValue}" Maximum="{Binding ProgressMax}"/>
        <TextBlock Text="{Binding StatusText}" Margin="0,10,0,0"/>
    </StackPanel>
</DataTemplate>

<!-- Verify Step Template -->
<DataTemplate x:Key="VerifyStepTemplate">
    <StackPanel MaxWidth="600">
        <TextBlock Text="Step 5: Verify" FontSize="24" FontWeight="SemiBold" Margin="0,0,0,20"/>
        <TextBlock Text="Re-running Evaluate-STIG and SCC to verify hardening results..." Margin="0,0,0,20"/>
        <ProgressBar Height="20" IsIndeterminate="{Binding IsBusy}" Value="{Binding ProgressValue}" Maximum="{Binding ProgressMax}"/>
        <TextBlock Text="{Binding StatusText}" Margin="0,10,0,0"/>
    </StackPanel>
</DataTemplate>

<!-- Done Step Template -->
<DataTemplate x:Key="DoneStepTemplate">
    <StackPanel MaxWidth="600">
        <TextBlock Text="Step 6: Done" FontSize="24" FontWeight="SemiBold" Margin="0,0,0,20"/>
        <TextBlock Text="Workflow completed!" FontSize="18" Foreground="Green" Margin="0,0,0,20"/>
        <TextBlock Text="Mission JSON:" FontWeight="SemiBold"/>
        <TextBox Text="{Binding MissionJsonPath, Mode=OneWay}" IsReadOnly="True" Margin="0,4,0,16"/>
        <Button Content="Open Output Folder" Width="150" Height="36" Command="{Binding OpenOutputFolderCommand}"/>
    </StackPanel>
</DataTemplate>
```

**Step 2: Add StepFontWeightConverter**

Create `src/STIGForge.App/Converters/StepFontWeightConverter.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace STIGForge.App.Converters;

public class StepFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is WorkflowStep current && parameter is string stepName)
        {
            if (Enum.TryParse<WorkflowStep>(stepName, out var step))
                return current == step ? FontWeights.Bold : FontWeights.Normal;
        }
        return FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

**Step 3: Register converter in App.xaml**

Add to `<Application.Resources>`:

```xml
<local:StepFontWeightConverter x:Key="StepFontWeightConverter"/>
```

And add namespace:
```xml
xmlns:local="clr-namespace:STIGForge.App.Converters"
```

**Step 4: Add MissionJsonPath and OpenOutputFolderCommand to ViewModel**

```csharp
[ObservableProperty]
private string _missionJsonPath = string.Empty;

[RelayCommand]
private void OpenOutputFolder()
{
    if (!string.IsNullOrWhiteSpace(OutputFolderPath) && Directory.Exists(OutputFolderPath))
        System.Diagnostics.Process.Start("explorer.exe", OutputFolderPath);
}
```

**Step 5: Verify it compiles**

Run:
```powershell
dotnet build src/STIGForge.App/STIGForge.App.csproj
```
Expected: Build succeeded

**Step 6: Commit**

```bash
git add src/STIGForge.App/App.xaml src/STIGForge.App/WorkflowViewModel.cs src/STIGForge.App/Converters/StepFontWeightConverter.cs
git commit -m "feat(app): add placeholder templates for all workflow steps"
```

---

### Task 7: Update MainWindow to host WorkflowWizardView

**Files:**
- Modify: `src/STIGForge.App/MainWindow.xaml`
- Modify: `src/STIGForge.App/MainWindow.xaml.cs`

**Step 1: Replace MainWindow.xaml content**

```xml
<Window x:Class="STIGForge.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:STIGForge.App.Views"
        Title="STIGForge Workflow" Height="600" Width="800" MinHeight="500" MinWidth="700"
        WindowStartupLocation="CenterScreen"
        Background="{DynamicResource WindowBackgroundBrush}">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <DockPanel Grid.Row="0" Margin="0,0,0,20">
            <TextBlock Text="STIGForge" FontSize="28" FontWeight="SemiBold" VerticalAlignment="Center"/>
            <Button Content="About" Width="70" Height="28" DockPanel.Dock="Right" Command="{Binding ShowAboutCommand}"/>
        </DockPanel>

        <!-- Wizard -->
        <views:WorkflowWizardView Grid.Row="1"/>
    </Grid>
</Window>
```

**Step 2: Update MainWindow.xaml.cs**

```csharp
using System.Windows;

namespace STIGForge.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new WorkflowViewModel();
    }
}
```

**Step 3: Verify it compiles and runs**

Run:
```powershell
dotnet build src/STIGForge.App/STIGForge.App.csproj
dotnet run --project src/STIGForge.App/STIGForge.App.csproj
```
Expected: App launches showing Setup step with path inputs

**Step 4: Commit**

```bash
git add src/STIGForge.App/MainWindow.xaml src/STIGForge.App/MainWindow.xaml.cs
git commit -m "feat(app): wire MainWindow to WorkflowWizardView"
```

---

### Task 8: Add settings persistence

**Files:**
- Create: `src/STIGForge.App/WorkflowSettings.cs`
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowSettingsTests.cs`

**Step 1: Write the failing test**

```csharp
// tests/STIGForge.UnitTests/App/WorkflowSettingsTests.cs
using STIGForge.App;

namespace STIGForge.UnitTests.App;

public class WorkflowSettingsTests
{
    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"stigforge-test-{Guid.NewGuid()}.json");
        try
        {
            var settings = new WorkflowSettings
            {
                ImportFolderPath = @"C:\test\import",
                EvaluateStigToolPath = @"C:\test\tool",
                OutputFolderPath = @"C:\test\output"
            };

            WorkflowSettings.Save(settings, tempPath);
            var loaded = WorkflowSettings.Load(tempPath);

            Assert.Equal(settings.ImportFolderPath, loaded.ImportFolderPath);
            Assert.Equal(settings.EvaluateStigToolPath, loaded.EvaluateStigToolPath);
            Assert.Equal(settings.OutputFolderPath, loaded.OutputFolderPath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
```

**Step 2: Run test to verify it fails**

Run:
```powershell
dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowSettingsTests" --no-restore
```
Expected: FAIL - `WorkflowSettings` does not exist

**Step 3: Write minimal implementation**

```csharp
// src/STIGForge.App/WorkflowSettings.cs
using System.IO;
using System.Text.Json;

namespace STIGForge.App;

public class WorkflowSettings
{
    public string ImportFolderPath { get; set; } = string.Empty;
    public string EvaluateStigToolPath { get; set; } = string.Empty;
    public string SccToolPath { get; set; } = string.Empty;
    public string OutputFolderPath { get; set; } = string.Empty;
    public string MachineTarget { get; set; } = "localhost";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "STIGForge", "workflow-settings.json");

    public static WorkflowSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return new WorkflowSettings();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WorkflowSettings>(json) ?? new WorkflowSettings();
    }

    public static void Save(WorkflowSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }
}
```

**Step 4: Run test to verify it passes**

Run:
```powershell
dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowSettingsTests" --no-restore
```
Expected: PASS

**Step 5: Wire persistence into WorkflowViewModel**

Add to constructor:
```csharp
public WorkflowViewModel()
{
    LoadSettings();
}

private void LoadSettings()
{
    var settings = WorkflowSettings.Load();
    ImportFolderPath = settings.ImportFolderPath;
    EvaluateStigToolPath = settings.EvaluateStigToolPath;
    SccToolPath = settings.SccToolPath;
    OutputFolderPath = settings.OutputFolderPath;
    MachineTarget = settings.MachineTarget;
}

private void SaveSettings()
{
    WorkflowSettings.Save(new WorkflowSettings
    {
        ImportFolderPath = ImportFolderPath,
        EvaluateStigToolPath = EvaluateStigToolPath,
        SccToolPath = SccToolPath,
        OutputFolderPath = OutputFolderPath,
        MachineTarget = MachineTarget
    });
}
```

Call `SaveSettings()` in `GoNextAsync()` after advancing from Setup step.

**Step 6: Commit**

```bash
git add src/STIGForge.App/WorkflowSettings.cs src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowSettingsTests.cs
git commit -m "feat(app): add workflow settings persistence to AppData"
```

---

### Task 9: Implement Import step logic

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelImportTests.cs`

**Step 1: Write the failing test**

```csharp
// tests/STIGForge.UnitTests/App/WorkflowViewModelImportTests.cs
namespace STIGForge.UnitTests.App;

public class WorkflowViewModelImportTests
{
    [Fact]
    public async Task RunImportStep_SetsImportedItemsCount()
    {
        var vm = new WorkflowViewModel();
        vm.CurrentStep = WorkflowStep.Import;
        
        await vm.RunCurrentStepAsync();
        
        Assert.True(vm.ImportedItemsCount >= 0);
    }
}
```

**Step 2: Implement RunCurrentStepAsync**

Add to `WorkflowViewModel.cs`:

```csharp
[ObservableProperty]
private int _importedItemsCount;

[ObservableProperty]
private List<string> _importedItems = new();

public async Task RunCurrentStepAsync()
{
    IsBusy = true;
    StatusText = "Starting...";
    try
    {
        switch (CurrentStep)
        {
            case WorkflowStep.Import:
                await RunImportAsync();
                break;
            case WorkflowStep.Scan:
                await RunScanAsync();
                break;
            case WorkflowStep.Harden:
                await RunHardenAsync();
                break;
            case WorkflowStep.Verify:
                await RunVerifyAsync();
                break;
        }
    }
    finally
    {
        IsBusy = false;
    }
}

private async Task RunImportAsync()
{
    StatusText = "Scanning import folder...";
    // TODO: Wire to actual import service
    await Task.Delay(500); // Placeholder
    ImportedItemsCount = 0;
    StatusText = $"Found {ImportedItemsCount} items";
}

private async Task RunScanAsync()
{
    StatusText = "Running Evaluate-STIG...";
    await Task.Delay(500); // Placeholder
    StatusText = "Scan complete";
}

private async Task RunHardenAsync()
{
    StatusText = "Applying hardening...";
    await Task.Delay(500); // Placeholder
    StatusText = "Hardening complete";
}

private async Task RunVerifyAsync()
{
    StatusText = "Running verification...";
    await Task.Delay(500); // Placeholder
    StatusText = "Verification complete";
}
```

**Step 3: Auto-run step when entering**

Modify `GoNextAsync`:

```csharp
[RelayCommand(CanExecute = nameof(CanGoNext))]
private async Task GoNextAsync()
{
    if (CurrentStep == WorkflowStep.Setup)
        SaveSettings();

    if (CurrentStep < WorkflowStep.Done)
    {
        CurrentStep = CurrentStep + 1;
        
        if (CurrentStep != WorkflowStep.Setup && CurrentStep != WorkflowStep.Done)
            await RunCurrentStepAsync();
    }
}
```

**Step 4: Run tests**

Run:
```powershell
dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~WorkflowViewModel" --no-restore
```
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelImportTests.cs
git commit -m "feat(app): add step execution framework with placeholders"
```

---

### Task 10: Wire Import step to actual service

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Modify: `src/STIGForge.App/MainWindow.xaml.cs`

**Step 1: Add service dependencies to ViewModel**

```csharp
private readonly IImportInboxScanner? _importScanner;

public WorkflowViewModel() : this(null) { }

public WorkflowViewModel(IImportInboxScanner? importScanner)
{
    _importScanner = importScanner;
    LoadSettings();
}
```

**Step 2: Implement actual import**

```csharp
private async Task RunImportAsync()
{
    StatusText = "Scanning import folder...";
    
    if (_importScanner == null || string.IsNullOrWhiteSpace(ImportFolderPath))
    {
        StatusText = "Import scanner not configured";
        return;
    }

    var result = await _importScanner.ScanAsync(ImportFolderPath, CancellationToken.None);
    ImportedItems = result.DiscoveredPacks.Select(p => p.Name).ToList();
    ImportedItemsCount = ImportedItems.Count;
    StatusText = $"Found {ImportedItemsCount} content packs";
}
```

**Step 3: Update MainWindow to inject services**

```csharp
public MainWindow()
{
    InitializeComponent();
    // TODO: Proper DI setup
    DataContext = new WorkflowViewModel(/* inject services */);
}
```

**Step 4: Verify it compiles**

Run:
```powershell
dotnet build src/STIGForge.App/STIGForge.App.csproj
```

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs src/STIGForge.App/MainWindow.xaml.cs
git commit -m "feat(app): wire Import step to IImportInboxScanner"
```

---

### Task 11: Wire Scan step to Evaluate-STIG

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`

**Step 1: Add verification service dependency**

```csharp
private readonly IVerificationWorkflowService? _verifyService;

public WorkflowViewModel(
    IImportInboxScanner? importScanner = null,
    IVerificationWorkflowService? verifyService = null)
{
    _importScanner = importScanner;
    _verifyService = verifyService;
    LoadSettings();
}
```

**Step 2: Implement Scan step**

```csharp
[ObservableProperty]
private int _baselineFindingsCount;

private async Task RunScanAsync()
{
    StatusText = "Running Evaluate-STIG baseline scan...";
    
    if (_verifyService == null)
    {
        StatusText = "Verification service not configured";
        return;
    }

    var result = await _verifyService.RunAsync(new VerificationWorkflowRequest
    {
        EvaluateStigToolRoot = EvaluateStigToolPath,
        OutputRoot = OutputFolderPath,
        RunEvaluateStig = true,
        RunScap = false
    }, CancellationToken.None);

    BaselineFindingsCount = result.ConsolidatedResults?.Count ?? 0;
    StatusText = $"Baseline scan complete: {BaselineFindingsCount} findings";
}
```

**Step 3: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs
git commit -m "feat(app): wire Scan step to IVerificationWorkflowService"
```

---

### Task 12: Wire Harden step to ApplyRunner

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`

**Step 1: Add apply service dependency**

```csharp
private readonly IApplyRunner? _applyRunner;

public WorkflowViewModel(
    IImportInboxScanner? importScanner = null,
    IVerificationWorkflowService? verifyService = null,
    IApplyRunner? applyRunner = null)
{
    _importScanner = importScanner;
    _verifyService = verifyService;
    _applyRunner = applyRunner;
    LoadSettings();
}
```

**Step 2: Implement Harden step**

```csharp
[ObservableProperty]
private int _appliedFixesCount;

private async Task RunHardenAsync()
{
    StatusText = "Applying PowerSTIG/DSC configurations...";
    
    if (_applyRunner == null)
    {
        StatusText = "Apply runner not configured";
        return;
    }

    var result = await _applyRunner.RunAsync(new ApplyRequest
    {
        OutputRoot = OutputFolderPath,
        ApplyDsc = true,
        ApplyGpo = true,
        ImportAdmx = true
    }, CancellationToken.None);

    AppliedFixesCount = result.AppliedCount;
    StatusText = $"Hardening complete: {AppliedFixesCount} fixes applied";
}
```

**Step 3: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs
git commit -m "feat(app): wire Harden step to IApplyRunner"
```

---

### Task 13: Wire Verify step to Evaluate-STIG + SCC

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`

**Step 1: Implement Verify step**

```csharp
[ObservableProperty]
private int _verifyFindingsCount;

[ObservableProperty]
private int _fixedCount;

private async Task RunVerifyAsync()
{
    StatusText = "Running verification scan (Evaluate-STIG + SCC)...";
    
    if (_verifyService == null)
    {
        StatusText = "Verification service not configured";
        return;
    }

    var result = await _verifyService.RunAsync(new VerificationWorkflowRequest
    {
        EvaluateStigToolRoot = EvaluateStigToolPath,
        ScapToolRoot = SccToolPath,
        OutputRoot = OutputFolderPath,
        RunEvaluateStig = true,
        RunScap = !string.IsNullOrWhiteSpace(SccToolPath)
    }, CancellationToken.None);

    VerifyFindingsCount = result.ConsolidatedResults?.Count ?? 0;
    FixedCount = BaselineFindingsCount - VerifyFindingsCount;
    if (FixedCount < 0) FixedCount = 0;
    
    MissionJsonPath = Path.Combine(OutputFolderPath, "mission.json");
    StatusText = $"Verification complete: {VerifyFindingsCount} remaining findings ({FixedCount} fixed)";
}
```

**Step 2: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs
git commit -m "feat(app): wire Verify step to Evaluate-STIG and SCC"
```

---

### Task 14: Update Done step template with results summary

**Files:**
- Modify: `src/STIGForge.App/App.xaml`

**Step 1: Update DoneStepTemplate**

```xml
<DataTemplate x:Key="DoneStepTemplate">
    <StackPanel MaxWidth="600">
        <TextBlock Text="Step 6: Done" FontSize="24" FontWeight="SemiBold" Margin="0,0,0,20"/>
        <TextBlock Text="Workflow completed!" FontSize="18" Foreground="Green" Margin="0,0,0,20"/>
        
        <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" Padding="16" Margin="0,0,0,20">
            <StackPanel>
                <TextBlock Text="Summary" FontWeight="SemiBold" Margin="0,0,0,10"/>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="200"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition/>
                        <RowDefinition/>
                        <RowDefinition/>
                        <RowDefinition/>
                    </Grid.RowDefinitions>
                    
                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Baseline Findings:"/>
                    <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding BaselineFindingsCount}" FontWeight="SemiBold"/>
                    
                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Fixed:"/>
                    <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding FixedCount}" FontWeight="SemiBold" Foreground="Green"/>
                    
                    <TextBlock Grid.Row="2" Grid.Column="0" Text="Remaining Findings:"/>
                    <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding VerifyFindingsCount}" FontWeight="SemiBold"/>
                    
                    <TextBlock Grid.Row="3" Grid.Column="0" Text="Content Packs Imported:"/>
                    <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding ImportedItemsCount}" FontWeight="SemiBold"/>
                </Grid>
            </StackPanel>
        </Border>
        
        <TextBlock Text="Mission JSON:" FontWeight="SemiBold"/>
        <TextBox Text="{Binding MissionJsonPath, Mode=OneWay}" IsReadOnly="True" Margin="0,4,0,16"/>
        
        <StackPanel Orientation="Horizontal">
            <Button Content="Open Output Folder" Width="150" Height="36" Margin="0,0,10,0" Command="{Binding OpenOutputFolderCommand}"/>
            <Button Content="Start New Workflow" Width="150" Height="36" Command="{Binding RestartWorkflowCommand}"/>
        </StackPanel>
    </StackPanel>
</DataTemplate>
```

**Step 2: Add RestartWorkflowCommand**

```csharp
[RelayCommand]
private void RestartWorkflow()
{
    CurrentStep = WorkflowStep.Setup;
    BaselineFindingsCount = 0;
    VerifyFindingsCount = 0;
    FixedCount = 0;
    ImportedItemsCount = 0;
    MissionJsonPath = string.Empty;
    StatusText = string.Empty;
}
```

**Step 3: Commit**

```bash
git add src/STIGForge.App/App.xaml src/STIGForge.App/WorkflowViewModel.cs
git commit -m "feat(app): update Done step with results summary and restart option"
```

---

### Task 15: Setup DI container and wire all services

**Files:**
- Modify: `src/STIGForge.App/App.xaml.cs`
- Modify: `src/STIGForge.App/MainWindow.xaml.cs`

**Step 1: Setup DI in App.xaml.cs**

```csharp
using Microsoft.Extensions.DependencyInjection;
using STIGForge.Content.Import;
using STIGForge.Verify;
using STIGForge.Apply;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        
        // Register services
        services.AddSingleton<IImportInboxScanner, ImportInboxScanner>();
        services.AddSingleton<IVerificationWorkflowService, VerificationWorkflowService>();
        services.AddSingleton<IApplyRunner, ApplyRunner>();
        services.AddSingleton<WorkflowViewModel>();
        
        Services = services.BuildServiceProvider();
        
        base.OnStartup(e);
    }
}
```

**Step 2: Update MainWindow to use DI**

```csharp
public MainWindow()
{
    InitializeComponent();
    DataContext = ((App)Application.Current).Services.GetRequiredService<WorkflowViewModel>();
}
```

**Step 3: Verify it runs**

Run:
```powershell
dotnet run --project src/STIGForge.App/STIGForge.App.csproj
```

**Step 4: Commit**

```bash
git add src/STIGForge.App/App.xaml.cs src/STIGForge.App/MainWindow.xaml.cs
git commit -m "feat(app): setup DI container and wire all services"
```

---

### Task 16: Final cleanup and verification

**Files:**
- Delete any remaining unused files
- Run full test suite

**Step 1: Delete CLI test files**

Run:
```bash
rm -rf tests/STIGForge.UnitTests/Cli/
```

**Step 2: Run full test suite**

Run:
```powershell
dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj
```
Expected: All tests pass (excluding removed CLI tests)

**Step 3: Run the app end-to-end**

Run:
```powershell
dotnet run --project src/STIGForge.App/STIGForge.App.csproj
```
Expected: App launches, can navigate through all 6 steps

**Step 4: Commit**

```bash
git add -A
git commit -m "chore: final cleanup - remove CLI tests, verify all tests pass"
```

---

## Execution Notes

- Follow @superpowers:test-driven-development for ViewModel logic
- Use @superpowers:verification-before-completion before any status claim
- Keep commits small and task-bounded (one task == one commit)
- If a step fails, debug before proceeding

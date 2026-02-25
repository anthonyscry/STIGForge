# Workflow Dashboard Polish & Zip Drill-Down Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix the glaring white `ListBox` in the dashboard, shrink the workflow cards to reclaim space, and add a "drill-down" feature that expands ZIP archives to show the internal XML/content files so operators know exactly what STIGs are being evaluated.

**Architecture:** We will replace the flat `List<string> ImportedItems` with an observable hierarchy (e.g. `TreeView` or grouped `Expander` in an `ItemsControl`) bound to a new `ContentPackViewModel`. The `ImportInboxScanner` already opens the zips to find files; we just need to pass those filenames up to the View. The `App.xaml` will receive a dark-themed `ListBox` style.

**Tech Stack:** WPF, MVVM (CommunityToolkit), XAML, System.IO.Compression.

---

### Task 1: Fix ListBox White Background

**Files:**
- Modify: `src/STIGForge.App/App.xaml`

**Step 1: Write the ListBox Style**

Add a global style for `ListBox` and `ListBoxItem` to `App.xaml` so they inherit the DarkTheme brushes.

```xml
      <Style TargetType="ListBox">
        <Setter Property="Background" Value="{DynamicResource SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled" />
        <Setter Property="Padding" Value="4" />
      </Style>

      <Style TargetType="ListBoxItem">
        <Setter Property="Padding" Value="6,4" />
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="ListBoxItem">
              <Border Background="{TemplateBinding Background}"
                      BorderBrush="{TemplateBinding BorderBrush}"
                      BorderThickness="{TemplateBinding BorderThickness}"
                      CornerRadius="4"
                      Padding="{TemplateBinding Padding}">
                <ContentPresenter />
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                  <Setter Property="Background" Value="{DynamicResource ListItemHoverBrush}" />
                </Trigger>
                <Trigger Property="IsSelected" Value="True">
                  <Setter Property="Background" Value="{DynamicResource ListItemSelectedBrush}" />
                  <Setter Property="Foreground" Value="{DynamicResource ListItemSelectedTextBrush}" />
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Style>
```

**Step 2: Apply the fix**
Apply the XML to `App.xaml`.

**Step 3: Commit**
```bash
git add src/STIGForge.App/App.xaml
git commit -m "fix(ui): apply dark theme styling to ListBox and ListBoxItem"
```

---

### Task 2: Shrink Workflow Cards

**Files:**
- Modify: `src/STIGForge.App/Views/Controls/WorkflowStepCard.xaml`
- Modify: `src/STIGForge.App/Views/DashboardView.xaml`

**Step 1: Reduce MinHeight and Spacing**

In `WorkflowStepCard.xaml`, drop the massive `MinHeight`:
```xml
      <Border Style="{DynamicResource WorkflowStepCardBorderStyle}"
              BorderThickness="1"
              Padding="10"
              MinHeight="85"
              Background="{DynamicResource SurfaceBrush}"
              CornerRadius="12">
```
*Note: Dropping from 150 to 85 allows the card to tightly hug its content.*

In `DashboardView.xaml`, remove the forced 2x2 grid if needed, or just let the `UniformGrid` naturally shrink by setting `VerticalAlignment="Top"` on the grid:
```xml
      <UniformGrid Rows="2" Columns="2" Margin="0,0,0,14" VerticalAlignment="Top">
```

**Step 2: Commit**
```bash
git add src/STIGForge.App/Views/Controls/WorkflowStepCard.xaml src/STIGForge.App/Views/DashboardView.xaml
git commit -m "fix(ui): shrink workflow step cards to eliminate dead vertical space"
```

---

### Task 3: Build Zip Drill-Down Data Model

**Files:**
- Modify: `src/STIGForge.Content/Import/ImportInboxCandidate.cs`
- Modify: `src/STIGForge.Content/Import/ImportInboxScanner.cs`
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`

**Step 1: Add ContentFileNames to Candidate**

In `ImportInboxCandidate.cs`, add:
```csharp
public List<string> ContentFileNames { get; set; } = new();
```

**Step 2: Populate FileNames in Scanner**

In `ImportInboxScanner.cs` inside `BuildCandidatesAsync`, extract the `benchmarkEntries` and `scapDataStreamEntries` names, or just store the raw XML filenames. Right before returning the `candidate`:
```csharp
candidate.ContentFileNames = xmlEntries.Select(x => Path.GetFileName(x.FullName)).ToList();
```

**Step 3: Create ViewModel Wrapper**

In `WorkflowViewModel.cs`:
```csharp
public class ImportedPackViewModel
{
    public string PackName { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public bool HasFiles => Files.Count > 0;
}
```

Change `_importedItems` from `List<string>` to `ObservableCollection<ImportedPackViewModel>`:
```csharp
[ObservableProperty]
private ObservableCollection<ImportedPackViewModel> _importedPacks = new();
```

Update `RunImportAsync` to map them:
```csharp
var packs = result.Candidates
    .GroupBy(c => c.FileName)
    .Select(g => new ImportedPackViewModel
    {
        PackName = g.Key,
        Files = g.SelectMany(c => c.ContentFileNames).Distinct().OrderBy(f => f).ToList()
    }).ToList();

ImportedPacks = new ObservableCollection<ImportedPackViewModel>(packs);
ImportedItemsCount = ImportedPacks.Count;
```

**Step 4: Commit**
```bash
git add src/STIGForge.Content/Import/ImportInboxCandidate.cs src/STIGForge.Content/Import/ImportInboxScanner.cs src/STIGForge.App/WorkflowViewModel.cs
git commit -m "feat(import): capture internal XML filenames for dashboard zip drill-down"
```

---

### Task 4: UI Drill-Down using TreeView

**Files:**
- Modify: `src/STIGForge.App/Views/DashboardView.xaml`
- Modify: `src/STIGForge.App/App.xaml` (TreeView styling)

**Step 1: Add TreeView Style to App.xaml**

```xml
      <Style TargetType="TreeView">
        <Setter Property="Background" Value="{DynamicResource SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
      </Style>
```

**Step 2: Replace ListBox with TreeView**

In `DashboardView.xaml`, replace the `ListBox` bound to `ImportedItems`:
```xml
                    <TreeView Margin="0,6,0,0"
                              MinHeight="62"
                              MaxHeight="200"
                              ItemsSource="{Binding ImportedPacks}"
                              ToolTip="Content packs detected during Import step">
                        <TreeView.ItemTemplate>
                            <HierarchicalDataTemplate ItemsSource="{Binding Files}">
                                <StackPanel Orientation="Horizontal" Margin="0,4">
                                    <TextBlock Text="&#x1F4E6; " Foreground="{DynamicResource AccentBrush}" Margin="0,0,4,0"/>
                                    <TextBlock Text="{Binding PackName}" FontWeight="SemiBold" />
                                    <TextBlock Text="{Binding Files.Count, StringFormat=' ({0} files)'}" Foreground="{DynamicResource TextMutedBrush}" />
                                </StackPanel>
                                <HierarchicalDataTemplate.ItemTemplate>
                                    <DataTemplate>
                                        <StackPanel Orientation="Horizontal" Margin="0,2">
                                            <TextBlock Text="&#x1F4C4; " Foreground="{DynamicResource TextMutedBrush}" Margin="0,0,4,0"/>
                                            <TextBlock Text="{Binding}" Foreground="{DynamicResource TextMutedBrush}"/>
                                        </StackPanel>
                                    </DataTemplate>
                                </HierarchicalDataTemplate.ItemTemplate>
                            </HierarchicalDataTemplate>
                        </TreeView.ItemTemplate>
                        <TreeView.Style>
                            <Style TargetType="TreeView">
                                <Setter Property="Visibility" Value="Visible" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding ImportedItemsCount}" Value="0">
                                        <Setter Property="Visibility" Value="Collapsed" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TreeView.Style>
                    </TreeView>
```

**Step 3: Commit**
```bash
git add src/STIGForge.App/Views/DashboardView.xaml src/STIGForge.App/App.xaml
git commit -m "feat(ui): implement nested TreeView for zip content drill-down"
```

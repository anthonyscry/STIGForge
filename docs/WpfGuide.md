# STIGForge WPF Application Guide

## Launching

```powershell
dotnet run --project src\STIGForge.App\STIGForge.App.csproj
```

The application opens in a 1200x720 window with a header bar (title, status text, busy indicator, About button) and a tab control with 13 tabs.

---

## Tab Reference

### 1. Dashboard

The landing tab showing compliance status at a glance.

**Bundle Info Panel**: Displays the active bundle path, pack name, and profile name. Click **Refresh** to reload.

**Stats Cards** (visible when a bundle is loaded):
- **Controls Card**: Total control count with automated/manual breakdown
- **Automated Verification Card**: Compliance rate percentage, closed/open counts, last run timestamp
- **Manual Review Card**: Answer rate, pass/fail/N-A/open breakdown

**Quick Actions**: Run Apply, Run Verify, Export eMASS, Launch Manual Wizard, Compare Packs.

**Mission Severity + Recovery Guidance**:
- Dashboard now surfaces mission severity using CLI-aligned counters (`blocking`, `warnings`, `optional-skips`).
- Recovery guidance text is shown with required artifacts, next action, and rollback direction for operator decisions.

### 2. Content Packs

Manage imported DISA STIG content packs.

**Actions**:
- **Import ZIP**: Opens file dialog to select a STIG pack ZIP. Parses XCCDF/SCAP and stores in the database.
- **Compare Packs**: Opens pack diff dialog.

**Readability hierarchy**:
- Import tab presentation emphasizes four zones: **Primary Actions**, **Machine Context**, **Content Library**, and **Pack Details**.
- Command bindings and behavior are unchanged; this update is presentation-only for scannability.

**Auto import workflow**:
- The import pipeline processes archive files in the project `import/` folder during startup auto-scan (when enabled) or when **Scan Import Folder** is run.
- The import workspace is split into four subtabs: **Auto Import**, **Classification Results**, **Exceptions Queue**, and **Activity Log**.
- Clean packs auto-commit into the content library; the **Exceptions Queue** captures failed imports plus skipped/already-processed entries for operator review.

**Diff Output**:
- Comparison opens a dedicated diff viewer with Added, Removed, Changed, and Review Required tabs.
- Use **Export Markdown** for operator-readable review output.
- Use **Export JSON** for machine-readable artifact export.

**Pack List**: GridView showing Name, PackId, ImportedAt, Source for all imported packs.

**Pack Details Panel** (right side): Shows selected pack's metadata (name, ID, release date, import date, source, SHA-256 hash, control count, disk path). Actions: Open Pack folder, Export PowerSTIG Map CSV, Delete Pack.

### 3. Profiles

Create and edit hardening profiles.

**Profile Selector**: Dropdown of saved profiles.

**Editor Fields**:
- **Name**: Profile display name
- **Mode**: AuditOnly (no changes), Safe (reversible), Full (all)
- **Classification**: Classified, Unclassified, Mixed
- **Auto-NA**: Checkbox to auto-mark out-of-scope controls
- **Grace days**: Release age gate tolerance
- **NA comment**: Default comment template for N/A controls

**Overlay Selection**: Checkbox list of available overlays. Actions: Edit Overlays, Rebase Overlay, Refresh.

**Rebase Workflow**:
- **Rebase Overlay** opens the rebase wizard for baseline -> target pack analysis.
- Wizard surfaces auto-rebased items, review-required actions, and blocking conflict guidance.
- **Apply Rebase** stays disabled while blocking conflicts remain unresolved.
- Use **Export Markdown** and **Export JSON** in the wizard to capture deterministic rebase artifacts for release evidence.
- Wizard includes explicit recovery guidance (required artifacts, next action, rollback stance) for blocked rebase outcomes.

**Actions**: Save Profile, Delete Profile.

### 4. Build

Build offline hardening bundles.

**Selectors**: Pack dropdown and Profile dropdown.

**Build Bundle**: Creates the bundle directory structure with manifest, apply scripts, and templates.

**Build Gate**: Shows automation gate status (release-age policy).

**Recent Bundles**: Dropdown of previously built bundles with **Use** button to load.

### 5. Apply

Apply hardening configurations to the local system.

**Bundle Path**: Text box with Browse and Open buttons.

**Run Apply**: Executes the apply phase (DSC, PowerSTIG, scripts).

**Status**: Shows apply result and log file path.

### 6. Verify

Run compliance verification tools.

**Bundle Path**: Text box with Browse/Open.

**Tool Configuration**:
- **Evaluate-STIG Root**: Path to Evaluate-STIG folder
- **Evaluate-STIG Args**: Additional arguments
- **SCAP Cmd**: Path to SCAP/SCC executable
- **SCAP Args**: SCAP arguments
- **SCAP Label**: Tool label for reports

**Run Verify**: Executes configured verification tools.

**Overlap Summary**: ListView showing multi-tool coverage overlap (Sources, count, Controls, Closed, Open).

**Refresh Overlap**: Reloads overlap data from bundle.

### 7. Evidence

Attach evidence artifacts to specific STIG controls.

**Fields**:
- **Bundle**: Path to active bundle
- **RuleId**: Target rule identifier
- **Type**: Evidence type selector (File, Screenshot, Command, etc.)
- **Evidence text**: Inline text content
- **File**: Source file path with Browse

**Save Evidence**: Writes evidence with SHA-256 hash and metadata.

### 8. Manual

Review and answer manual STIG controls.

**Launch Review Wizard**: Step-by-step guided workflow for manual review.

**Filters**:
- **Search**: Text filter on RuleId/Title
- **Status**: Dropdown filter (All, Open, Pass, Fail, NA)

**Summary Bar**: Shows total, answered, and completion percentage.

**Control List** (left): GridView of RuleId, Title, Status.

**Answer Panel** (right): Shows selected control's title and check text. Fields:
- **Status**: Pass, Fail, NotApplicable, Open
- **Reason**: Justification text
- **Comment**: Additional notes

**Save Answer**: Persists the manual answer.

### 9. Orchestrate

One-click Apply -> Verify -> Export workflow.

**Configuration Panel**:
- Bundle path, Evaluate-STIG root, SCAP command path
- Checkboxes: Run Apply, Run Verify, Export eMASS

**Run Orchestration**: Executes selected pipeline steps in sequence.

**Log Output**: Scrollable Consolas-font log showing real-time progress.

**Import selection behavior**:
- STIG packages are operator-selected; SCAP/GPO/ADMX dependencies are auto-included and locked in the selection grid.
- Missing SCAP dependency is emitted as a warning and does not block mission setup.
- Selection summary counters use STIG-selected content as the source of truth for deterministic totals.

### 10. Reports

Export management and bundle reporting.

**Bundle Path**: Text box with Browse/Open.

**Recent Bundles**: Dropdown with **Use** button.

**Export eMASS**: Generates complete eMASS submission package.

**Status**: Shows export result, validation status, and output path.

### 11. Audit Log

Query and verify the tamper-evident audit trail.

**Filters**:
- **Action**: Filter by action type (apply, verify, export-emass, etc.)
- **Target**: Filter by target substring
- **Limit**: Max entries to return

**Actions**:
- **Query**: Execute the filtered query
- **Verify Integrity**: Check SHA-256 chain validity

**Verify Result**: Shows VALID/INVALID status.

**Entry List**: GridView of ID, Timestamp, Action, Target, Result, User, Detail.

### 12. Export

Standalone export tools.

**POA&M Export Section**:
- System Name field
- Export button generates poam.json, poam.csv, poam_summary.txt
- Status display

**CKL Export Section**:
- Host Name and STIG ID fields
- Export button generates STIG Viewer-compatible CKL XML
- Status display

### 13. Fleet

Multi-machine fleet operations via WinRM.

**Configuration Panel**:
- **Targets**: Comma-separated hostnames/IPs (format: `host1,host2:10.0.0.1,host3`)
- **Operation**: Dropdown (Apply, Verify, Orchestrate)
- **Concurrency**: Max parallel machines
- **Timeout**: Per-machine timeout in seconds

**Actions**:
- **Execute**: Run the selected operation across all targets
- **Check Status**: Test WinRM connectivity to all targets

**Log Output**: Scrollable Consolas-font output showing per-machine results.

---

## Architecture Notes

### MVVM Pattern
The app uses CommunityToolkit.Mvvm with source-generated `[ObservableProperty]` attributes. The ViewModel is split across partial class files:
- `MainViewModel.cs` — Core constructor (13 parameters), shared state
- `MainViewModel.Import.cs` — Content pack import logic
- `MainViewModel.ApplyVerify.cs` — Apply and verify commands
- `MainViewModel.Manual.cs` — Manual control review
- `MainViewModel.Dashboard.cs` — Dashboard stats refresh
- `MainViewModel.AuditLog.cs` — Audit trail query/verify
- `MainViewModel.Export.cs` — POA&M and CKL export
- `MainViewModel.Schedule.cs` — Scheduled task management
- `MainViewModel.Fleet.cs` — Fleet operations

### Dependency Injection
All services are registered in `App.xaml.cs` using `IServiceCollection`. The `MainViewModel` receives 13 constructor parameters (last 3 optional):
1. IContentPackRepository
2. IProfileRepository
3. IOverlayRepository
4. IControlRepository
5. BundleBuilder
6. SnapshotService
7. ApplyRunner
8. EmassExporter
9. IPathBuilder
10. ManualAnswerService
11. IAuditTrailService (optional)
12. ScheduledTaskService (optional)
13. FleetService (optional)

### Async Operations
Long-running operations are wrapped in `Task.Run(() => { ... })` to keep the UI responsive. The `IsBusy` property drives the progress bar visibility.

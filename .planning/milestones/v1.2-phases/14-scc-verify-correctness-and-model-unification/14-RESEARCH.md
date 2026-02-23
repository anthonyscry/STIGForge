# Phase 14: SCC Verify Correctness and Model Unification - Research

**Researched:** 2026-02-18
**Domain:** SCC/SCAP CLI integration, process management, XML discovery, model bridging, secure XML parsing
**Confidence:** HIGH (codebase is fully readable; external SCC CLI arg form is MEDIUM/LOW — flagged below)

---

## Summary

Phase 14 fixes five concrete defects in the verify workflow. The codebase was read in full: `ScapRunner`, `EvaluateStigRunner`, `VerificationWorkflowService`, `VerifyReportWriter`, `CklParser`, `VerifyOrchestrator`, and all adapter/model files. The test files that shipped with the phase (`ScapRunnerTests.cs`, `EvaluateStigRunnerTests.cs`, `VerifyCommandsTests.cs`, `VerifyReportWriterTests.cs`, `VerificationWorkflowServiceTests.cs`) were also read and compared against the current production code. The tests already describe the target API — implementation must satisfy them exactly.

The five defects map cleanly to two plans: (1) async-ify runners with configurable timeout and wire `VerificationWorkflowService` to use them, then fix output file discovery to include XCCDF; (2) wire `VerifyOrchestrator` into `VerificationWorkflowService` so XCCDF results flow through the adapter chain, bridge `ControlResult`/`NormalizedVerifyResult` into a single canonical model, and harden `CklParser` with `LoadSecureXml()`.

**Primary recommendation:** The tests are the specification. Implement production code until all seven test files (including the five new untracked ones) compile and pass. Do not deviate from the API signatures the tests already use.

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| VER-01 | Verify workflow completes SCC scans without premature timeout (fix 30s hardcoded limit) | `ScapRunner.Run()` has `WaitForExit(30000)` hardcoded — must become `RunAsync(commandPath, args, workingDir, ct, timeout?)` with configurable `TimeSpan` timeout, defaulting to something operator-configurable; `ScapWorkflowOptions` needs a `TimeoutSeconds` property; tests already call `RunAsync` with explicit `TimeSpan` parameter |
| VER-02 | Verify workflow discovers SCC XCCDF output from session subdirectories (fix `.ckl`-only glob) | `VerifyReportWriter.BuildFromCkls()` globs only `*.ckl` — must also discover `*.xml` XCCDF files in subdirectories; `VerificationWorkflowService` calls `BuildFromCkls` directly; the new path is through `VerifyOrchestrator.ParseAndMergeResults()` which already handles both via adapters |
| VER-03 | Verify workflow routes XCCDF results through `ScapResultAdapter` via `VerifyOrchestrator` | `VerificationWorkflowService.RunAsync()` calls `VerifyReportWriter.BuildFromCkls()` directly (CKL-only); `VerifyOrchestrator` already exists and has `ParseAndMergeResults()` but is never called from `VerificationWorkflowService`; wiring needed: discover all `.ckl` and `.xml` files under `OutputRoot`, feed to orchestrator, convert `NormalizedVerifyResult` to `ControlResult` via bridge |
| VER-04 | Result models are unified (`ControlResult`/`NormalizedVerifyResult` bridge) for downstream export | Two parallel models exist: `ControlResult` (used by `VerifyReport`, `VerifyReportWriter`, all CSV/JSON output) and `NormalizedVerifyResult` (used by adapter chain, `VerifyOrchestrator`); a bridge method must convert `NormalizedVerifyResult` → `ControlResult` so downstream `VerifyReport`/`VerifyReportWriter` output remains unchanged; see bridge pattern section |
| VER-05 | `CklParser` uses hardened XML loading consistent with `CklAdapter` security baseline | `CklParser.ParseFile()` calls `XDocument.Load(path)` directly — no `XmlReaderSettings`, no DTD prohibition, no character limits; `CklAdapter` and `ScapResultAdapter` already have a correct private `LoadSecureXml()` method with `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 1024`, `MaxCharactersInDocument = 20_000_000`; copy identical pattern into `CklParser` |

</phase_requirements>

---

## Standard Stack

### Core
| Component | Current State | What Changes |
|-----------|--------------|--------------|
| `ScapRunner` | Synchronous `Run()` with 30s hardcoded `WaitForExit` | Add `RunAsync(commandPath, args, workingDir, ct, timeout?)` using `Task.Run` + `CancellationToken` + configurable `TimeSpan` |
| `EvaluateStigRunner` | Synchronous `Run()` with 30s hardcoded `WaitForExit` | Same pattern as ScapRunner; tests also call `RunAsync` |
| `VerificationWorkflowService` | Calls `BuildFromCkls(outputRoot)` directly | Wire in `VerifyOrchestrator`; discover `.ckl` + `.xml` files; bridge results; add timeout support from `ScapWorkflowOptions` |
| `VerifyOrchestrator` | Exists but never called by `VerificationWorkflowService` | Becomes the result aggregation engine for the workflow |
| `CklParser` | `XDocument.Load(path)` — insecure | Replace with `LoadSecureXml()` matching `CklAdapter` |
| `VerifyReportWriter.BuildFromCkls()` | Called by workflow, CKL-only | May remain as a convenience method but should no longer be the primary path; the workflow moves to orchestrator-based discovery |

### No New NuGet Dependencies
This phase adds zero new packages. All work is in existing code: `System.Diagnostics.Process`, `System.Xml.Linq`, `System.Xml.XmlReaderSettings`, `System.IO`, `System.Threading.Tasks`.

---

## Architecture Patterns

### Pattern 1: Async Process Runner with Configurable Timeout

The tests in `ScapRunnerTests.cs` already specify the target signature:

```csharp
// Target API (from ScapRunnerTests.cs — must match exactly)
public Task<VerifyRunResult> RunAsync(
    string commandPath,
    string arguments,
    string? workingDirectory,
    CancellationToken ct,
    TimeSpan? timeout = null)
```

Key implementation details from test analysis:
- `timeout` parameter is optional; no-argument call uses a default (tests don't verify the default value, only that a custom timeout works)
- When timeout is exceeded: throw `TimeoutException` (test: `Should().ThrowAsync<TimeoutException>()`)
- When `ct` is cancelled: throw `OperationCanceledException` (test: `Should().ThrowAsync<OperationCanceledException>()`)
- The sync `Run()` method may remain for backward compatibility (existing tests call it, and `VerificationWorkflowService` will need updating too)

**Recommended implementation:** Use `Task.Run` to offload the blocking process, then `Task.WhenAny` with a `Task.Delay(timeout)` for timeout detection, with a `ct.Register` to kill the process on cancellation.

```csharp
// Pattern (not verbatim — implement to satisfy tests)
public async Task<VerifyRunResult> RunAsync(
    string commandPath, string arguments, string? workingDirectory,
    CancellationToken ct, TimeSpan? timeout = null)
{
    var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(600); // large default
    // Start process, use TaskCompletionSource or Task.Run for async wait
    // WhenAny([processExitTask, Task.Delay(effectiveTimeout, ct)])
    // On timeout: kill process, throw TimeoutException
    // On ct cancellation: kill process, throw OperationCanceledException
}
```

### Pattern 2: VerifyOrchestrator Wiring in VerificationWorkflowService

The current workflow:
```
SCC runs → output files in OutputRoot → BuildFromCkls(OutputRoot) → CKL-only glob → ControlResult list → VerifyReport → consolidated JSON
```

The target workflow:
```
SCC runs → output files in OutputRoot (Sessions/ subdirs included) → discover *.ckl + *.xml → VerifyOrchestrator.ParseAndMergeResults() → NormalizedVerifyResult → bridge → ControlResult → VerifyReport → consolidated JSON
```

`VerificationWorkflowService` must accept a `VerifyOrchestrator` (or construct one internally). The simplest approach is constructing it internally, matching the existing pattern where `ScapRunner` and `EvaluateStigRunner` are constructor-injected.

**File discovery change (VER-02):**
```csharp
// Current (CKL only):
Directory.GetFiles(outputRoot, "*.ckl", SearchOption.AllDirectories)

// Target (CKL + XCCDF XML):
var cklFiles = Directory.GetFiles(outputRoot, "*.ckl", SearchOption.AllDirectories);
var xmlFiles = Directory.GetFiles(outputRoot, "*.xml", SearchOption.AllDirectories);
var allFiles = cklFiles.Concat(xmlFiles)
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
    .ToList();
```

Note: `SearchOption.AllDirectories` already covers `Sessions/` subdirectories — no special-case code needed for the subdirectory structure, as long as the glob is recursive.

### Pattern 3: ControlResult / NormalizedVerifyResult Bridge (VER-04)

Two models exist and serve different purposes:
- `ControlResult` — the downstream export model used by `VerifyReport`, `VerifyReportWriter.WriteCsv()`, `VerifyReportWriter.WriteJson()`, and the consolidated JSON that all export phases read
- `NormalizedVerifyResult` — the adapter chain model with richer fields (`EvidencePaths`, `Metadata`, `VerifyStatus` enum)

The bridge must be a lossless projection: `NormalizedVerifyResult` has a superset of `ControlResult` fields. All `ControlResult` fields can be populated from `NormalizedVerifyResult`:

```csharp
// Bridge: NormalizedVerifyResult → ControlResult
private static ControlResult ToControlResult(NormalizedVerifyResult r)
{
    return new ControlResult
    {
        VulnId = r.VulnId,
        RuleId = r.RuleId,
        Title = r.Title,
        Severity = r.Severity,
        Status = MapVerifyStatusToString(r.Status),
        FindingDetails = r.FindingDetails,
        Comments = r.Comments,
        Tool = r.Tool,
        SourceFile = r.SourceFile,
        VerifiedAt = r.VerifiedAt
    };
}

private static string MapVerifyStatusToString(VerifyStatus status) => status switch
{
    VerifyStatus.Pass => "NotAFinding",
    VerifyStatus.Fail => "Open",
    VerifyStatus.NotApplicable => "Not_Applicable",
    VerifyStatus.NotReviewed => "Not_Reviewed",
    VerifyStatus.Informational => "Informational",
    VerifyStatus.Error => "Error",
    _ => "Not_Reviewed"
};
```

This bridge is the answer to VER-04. It lives in `VerificationWorkflowService` or a static helper class in `STIGForge.Verify`.

### Pattern 4: CklParser Hardening (VER-05)

`CklParser.ParseFile()` currently calls `XDocument.Load(path)` without any security settings. The fix is a direct copy of the `LoadSecureXml()` method from `CklAdapter`:

```csharp
// Replace this in CklParser.ParseFile():
var doc = XDocument.Load(path);

// With:
var doc = LoadSecureXml(path);

// Add this private method:
private static XDocument LoadSecureXml(string filePath)
{
    var settings = new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = true,
        MaxCharactersFromEntities = 1024,
        MaxCharactersInDocument = 20_000_000,
        Async = false
    };

    try
    {
        using var reader = XmlReader.Create(filePath, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }
    catch (XmlException ex)
    {
        throw new InvalidDataException(
            $"[VERIFY-CKL-XML-001] Failed to parse CKL XML '{filePath}': {ex.Message}", ex);
    }
}
```

The error code `[VERIFY-CKL-XML-001]` matches `CklAdapter`'s error code — this is intentional consistency.

### Pattern 5: ScapWorkflowOptions Timeout Field

`ScapWorkflowOptions` in `STIGForge.Core.Abstractions.Services.cs` needs a timeout property:

```csharp
public sealed class ScapWorkflowOptions
{
    // ... existing fields ...
    public int TimeoutSeconds { get; set; } = 600; // 10 minutes; no practical SCAP scan completes in 30s
}
```

`VerificationWorkflowService.RunScapIfConfigured()` passes `request.Scap.TimeoutSeconds` as the timeout to `ScapRunner.RunAsync()`. Same pattern for `EvaluateStigWorkflowOptions` if the test for `EvaluateStigRunner` also checks timeout propagation (it does — `EvaluateStigRunnerTests.cs` tests `RunAsync` with explicit timeout).

### Pattern 6: VerifyView XAML and MainViewModel Contract Tests (failing tests from Phase 13 branch)

The `VerifyViewLayoutContractTests` and `ScapArgsOptionsContractTests` are also failing (33 test failures in current run). These tests inspect:
1. `VerifyView.xaml` must have `TabItem` headers "Verify" and "Settings" (currently it's a flat `ScrollViewer` with no tabs)
2. The "Verify" tab must have `ComboBox` bound to `{Binding VerifyScannerMode}` and must NOT have `EvaluateStigRoot` / `ScapCommandPath` text boxes directly on the verify tab
3. The "Settings" tab must have `EvaluateStigRoot`, `ScapCommandPath`, `PowerStigModulePath` text boxes
4. `MainViewModel.cs` must have `[ObservableProperty] private bool scapIncludeF;` (no `= true` initializer)
5. `MainViewModel.cs` must have `[ObservableProperty] private string scapArgs = "-u";` (not `"-u -s -r -f"`)
6. `MainViewModel.Dashboard.cs` must contain: `var includeF = ScapIncludeF && hasValueInExtraArgs;`, `if (includeF)`, `ScapIncludeF = includeF;`
7. `VerificationWorkflowService.cs` must contain: `SCAP argument '-f' was missing a filename; removed invalid switch.`

**These are Phase 14 Plan 2 tasks** — the WPF view restructuring and the `-f` safety guard in VerificationWorkflowService.

The `-f` flag validation is the safety guard: SCC's `-f` switch requires a filename argument. The test `MainViewModelDashboard_OnlyAddsFWhenValuePresent` checks that `-f` is only included in SCAP args when there is a value in the "extra args" field. The pattern `var includeF = ScapIncludeF && hasValueInExtraArgs;` means the checkbox binds intent, but the actual inclusion of `-f` is conditional on `ScapAdditionalArgs` having content. When `-f` is stripped due to no value, `VerificationWorkflowService` emits a diagnostic: `"SCAP argument '-f' was missing a filename; removed invalid switch."`.

The `-u` default for `scapArgs` is confirmed by the first web search result: SCC CLI uses `cscc -u <outputDir>` as the standard output directory argument form. This aligns with the test expectation `scapArgs = "-u"`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Process async wait | Custom wait loop | `Task.Run` + `Task.WhenAny` with `Task.Delay` for timeout |
| CancellationToken propagation | Custom flag-polling | `ct.Register(() => process.Kill(true))` or `ct.ThrowIfCancellationRequested()` after kill |
| Secure XML parsing | Custom DTD stripping | `XmlReaderSettings` with `DtdProcessing.Prohibit` (already done in adapters, copy verbatim) |
| Model bridging | Separate converter service | Private static method in `VerificationWorkflowService` — no DI needed |
| XCCDF file discovery | Custom session directory walker | `Directory.GetFiles(outputRoot, "*.xml", SearchOption.AllDirectories)` — recursive is sufficient |

---

## Common Pitfalls

### Pitfall 1: Process stdout deadlock on sync reads
**What goes wrong:** `process.StandardOutput.ReadToEnd()` before `WaitForExit()` can deadlock when stdout is full.
**Why it happens:** The process blocks writing to stdout because the buffer is full; the host is also blocked waiting for the process.
**How to avoid:** In `RunAsync`, use `ReadToEndAsync()` concurrently with the process wait. Start both tasks before awaiting either.
**Warning signs:** Tests that hang indefinitely on large SCAP output.

```csharp
// Correct async pattern:
var outputTask = process.StandardOutput.ReadToEndAsync(ct);
var errorTask = process.StandardError.ReadToEndAsync(ct);
await process.WaitForExitAsync(ct); // .NET 6+ API
var output = await outputTask;
var error = await errorTask;
```

Note: `Process.WaitForExitAsync(CancellationToken)` is available in .NET 6+. The project targets `net8.0` and `net48`. For `net48`, fall back to `Task.Run(() => process.WaitForExit(timeoutMs))`.

### Pitfall 2: `SearchOption.AllDirectories` picks up too many XML files
**What goes wrong:** `*.xml` glob under `OutputRoot` could match non-SCAP XML files (e.g., application manifests, PowerShell module files) placed in the same directory tree.
**Why it happens:** The glob has no semantic filter.
**How to avoid:** `ScapResultAdapter.CanHandle()` already performs structural validation (checks XCCDF namespace, checks for `Benchmark` or `TestResult` root element). Files that don't match are silently skipped. This is the correct defense.
**Warning signs:** `DiagnosticMessages` in `ConsolidatedVerifyReport` growing with "No adapter found" messages.

### Pitfall 3: ControlResult.Status string vs VerifyStatus enum mismatch
**What goes wrong:** Downstream code (VerifyReportWriter, coverage calculations) uses string comparisons like `s.Contains("notafinding")` and `s.Contains("open")`. If the bridge emits different strings, coverage math breaks.
**Why it happens:** `VerifyStatus.Pass` → must map to `"NotAFinding"` (not `"pass"`) to match existing `IsClosed()` logic in `VerifyReportWriter`.
**How to avoid:** Use the exact strings that `IsClosed()` in `VerifyReportWriter` handles: `"NotAFinding"`, `"Open"`, `"Not_Applicable"`, `"Not_Reviewed"`.
**Warning signs:** `ClosedCount = 0` in coverage summaries even when SCAP reports `pass`.

### Pitfall 4: net48 / net8.0 dual-target async API differences
**What goes wrong:** `Process.WaitForExitAsync(CancellationToken ct)` is .NET 6+. `StandardOutput.ReadToEndAsync(CancellationToken)` is .NET 7+. Using them in net48 target causes build failures.
**Why it happens:** Dual-target `<TargetFrameworks>net8.0;net48</TargetFrameworks>` in `STIGForge.Verify.csproj`.
**How to avoid:** Use `#if NET6_0_OR_GREATER` guards or implement a fallback using `Task.Run(() => process.WaitForExit(ms))` for net48. Check the `.csproj` target frameworks before choosing the API.
**Warning signs:** `dotnet build` for `net48` target fails with `CS1061` or `CS0117`.

### Pitfall 5: ScapRunner sync Run() method backward compatibility
**What goes wrong:** `VerificationWorkflowService` currently calls `_scapRunner.Run(...)` synchronously. The existing tests for `VerificationWorkflowServiceTests` call `service.RunAsync(request, ct)` which internally uses `_scapRunner.Run()`. After the refactor, `RunAsync` must be used in `VerificationWorkflowService` too.
**Why it happens:** The planner may try to keep the sync `Run()` and only add `RunAsync()` without updating the call site.
**How to avoid:** Update `VerificationWorkflowService` to call `_scapRunner.RunAsync()` and `_evaluateStigRunner.RunAsync()`. The sync `Run()` methods can be kept for backward compatibility but should not be the primary path.

### Pitfall 6: VerifyView XAML tab restructure breaks existing bindings
**What goes wrong:** Moving `EvaluateStigRoot`, `ScapCommandPath`, `ScapIncludeF`, etc. into a "Settings" tab, and adding a "Verify" tab with `VerifyScannerMode`, requires all existing bindings to work inside their new tab context. WPF DataContext is inherited, so binding paths don't change, but automation names and layout tests do.
**Why it happens:** The `VerifyViewLayoutContractTests` is very specific about which controls appear in which tab.
**How to avoid:** Read the test assertions carefully before restructuring XAML. The test checks: "Verify" tab has `ComboBox` bound to `{Binding VerifyScannerMode}`; "Settings" tab has `TextBox` with `{Binding EvaluateStigRoot}`, `{Binding ScapCommandPath}`, `{Binding PowerStigModulePath}`.

### Pitfall 7: SCC CLI `-f` flag requires a filename
**What goes wrong:** SCC's `-f` switch requires a filename argument immediately after it. Passing `-f` without a value causes SCC to fail or produce no output.
**Why it happens:** The UI checkbox `ScapIncludeF` is a boolean that maps to `-f`, but the filename comes from `ScapAdditionalArgs`. Without validation, `-f` is injected with no value.
**How to avoid:** The test `MainViewModelDashboard_OnlyAddsFWhenValuePresent` specifies the guard: `var includeF = ScapIncludeF && hasValueInExtraArgs;`. When the checkbox is checked but extra args is empty, `-f` is removed and a diagnostic is logged in `VerificationWorkflowService`.

---

## Code Examples

### Async Runner with Timeout and CancellationToken (target for ScapRunner and EvaluateStigRunner)

```csharp
// In ScapRunner.cs — RunAsync (must match test expectations)
public async Task<VerifyRunResult> RunAsync(
    string commandPath,
    string arguments,
    string? workingDirectory,
    CancellationToken ct,
    TimeSpan? timeout = null)
{
    if (string.IsNullOrWhiteSpace(commandPath))
        throw new ArgumentException("Command path is required.");
    if (!File.Exists(commandPath))
        throw new FileNotFoundException("SCAP command not found", commandPath);

    var psi = new ProcessStartInfo
    {
        FileName = commandPath,
        Arguments = arguments ?? string.Empty,
        WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory : workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi)
        ?? throw new InvalidOperationException("Failed to start SCAP command.");

    // Guard against stdout deadlock
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();

    var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(600);
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    // Register cancellation to kill the process
    await using var reg = ct.Register(() =>
    {
        try { process.Kill(true); } catch { }
    });

    var exited = Task.Run(() => process.WaitForExit((int)effectiveTimeout.TotalMilliseconds));
    var timedOut = !(await exited);

    if (timedOut)
    {
        try { process.Kill(true); } catch { }
        throw new TimeoutException(
            $"Process did not exit within {effectiveTimeout.TotalSeconds} seconds.");
    }

    ct.ThrowIfCancellationRequested();

    var output = await outputTask;
    var error = await errorTask;

    return new VerifyRunResult
    {
        ExitCode = process.ExitCode,
        Output = output,
        Error = error,
        StartedAt = started,
        FinishedAt = DateTimeOffset.Now
    };
}
```

Note: `Process.Kill(bool entireProcessTree)` requires .NET 5+. For `net48` compatibility, use `Process.Kill()` (no argument). Check csproj target framework.

### NormalizedVerifyResult → ControlResult Bridge

```csharp
// In VerificationWorkflowService (private static)
private static ControlResult ToControlResult(NormalizedVerifyResult r)
{
    return new ControlResult
    {
        VulnId = r.VulnId,
        RuleId = r.RuleId,
        Title = r.Title,
        Severity = r.Severity,
        Status = MapStatusToString(r.Status),
        FindingDetails = r.FindingDetails,
        Comments = r.Comments,
        Tool = r.Tool,
        SourceFile = r.SourceFile,
        VerifiedAt = r.VerifiedAt
    };
}

private static string MapStatusToString(VerifyStatus status) => status switch
{
    VerifyStatus.Pass => "NotAFinding",
    VerifyStatus.Fail => "Open",
    VerifyStatus.NotApplicable => "Not_Applicable",
    VerifyStatus.NotReviewed => "Not_Reviewed",
    VerifyStatus.Informational => "Informational",
    VerifyStatus.Error => "Error",
    _ => "Not_Reviewed"
};
```

### CklParser.LoadSecureXml (VER-05)

```csharp
// In CklParser.cs — replace XDocument.Load(path) with this:
private static XDocument LoadSecureXml(string filePath)
{
    var settings = new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = true,
        MaxCharactersFromEntities = 1024,
        MaxCharactersInDocument = 20_000_000,
        Async = false
    };
    try
    {
        using var reader = XmlReader.Create(filePath, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }
    catch (XmlException ex)
    {
        throw new InvalidDataException(
            $"[VERIFY-CKL-XML-001] Failed to parse CKL XML '{filePath}': {ex.Message}", ex);
    }
}
```

### ScapWorkflowOptions Timeout Addition

```csharp
// In STIGForge.Core/Abstractions/Services.cs
public sealed class ScapWorkflowOptions
{
    public bool Enabled { get; set; }
    public string CommandPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public string ToolLabel { get; set; } = "SCAP";
    public int TimeoutSeconds { get; set; } = 600; // NEW: 10-minute default
}

// Same for EvaluateStigWorkflowOptions if tests require timeout propagation there
public sealed class EvaluateStigWorkflowOptions
{
    public bool Enabled { get; set; }
    public string ToolRoot { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public int TimeoutSeconds { get; set; } = 600; // NEW: 10-minute default
}
```

---

## State of the Art

| Old Approach | Current Approach | What Phase 14 Changes |
|--------------|-----------------|----------------------|
| 30s hardcoded `WaitForExit` | Same | Configurable `TimeSpan` + `CancellationToken` in `RunAsync` |
| `.ckl`-only glob in `BuildFromCkls` | Same | `.ckl` + `.xml` recursive glob through `VerifyOrchestrator` |
| Direct `XDocument.Load()` in `CklParser` | Same | `LoadSecureXml()` with DTD prohibition |
| `VerifyOrchestrator` exists but unused in workflow | Same | Wired into `VerificationWorkflowService` |
| Two parallel result models with no bridge | Same | `ToControlResult()` bridge method |

---

## Open Questions

1. **SCC CLI `-u` argument form (MEDIUM confidence)**
   - What we know: Web search confirms `cscc -u <outputDir>` is the standard form for specifying output directory in SCC 5.x. Multiple Ansible automation playbooks use this form.
   - What's unclear: Whether SCC 5.14 (current release) still uses `-u` or has changed to `--setOpt outputDir` or a different form.
   - Recommendation: The test `MainViewModel_UsesSafeDefault_ForScapIncludeF` asserts `scapArgs = "-u"` which is the default args string. This validates that `-u` is the correct and expected base argument. Treat this as confirmed for the WPF default. For the actual SCC binary invocation, validate against a live SCC 5.x installation. The implementation should not inject `-u <outputDir>` programmatically — instead, the operator provides the full args string through the UI or CLI.

2. **SCC Sessions subdirectory naming pattern (LOW confidence)**
   - What we know: SCC creates output in session-named subdirectories under the configured output root. The exact pattern (e.g., `Sessions/HOSTNAME_DATE_TIME/`) is not confirmed from official docs (behind CAC-gate).
   - What's unclear: Whether the subdirectory depth is always 1 level (`Sessions/`) or can be deeper.
   - Recommendation: Use `SearchOption.AllDirectories` for file discovery (as per STATE.md guidance). This handles any depth. Add diagnostic logging when no XCCDF files are found, listing what was found. The `VerifyOrchestrator` silently skips non-XCCDF XML files via `CanHandle()`.

3. **`Process.Kill(true)` net48 compatibility (HIGH confidence — must verify)**
   - What we know: `Process.Kill(bool entireProcessTree)` is .NET 5+. `net48` has only `Process.Kill()` (no argument).
   - What's unclear: Whether STIGForge.Verify targets `net48` (it does — both targets are in the csproj).
   - Recommendation: Use `#if NET5_0_OR_GREATER` preprocessor guard for `Kill(true)` vs `Kill()`.

4. **`VerifyScannerMode` property and enum (LOW confidence — not yet in codebase)**
   - What we know: `VerifyViewLayoutContractTests` asserts `ComboBox` bound to `{Binding VerifyScannerMode}`. This property does not exist yet in `MainViewModel.cs`.
   - What's unclear: What enum values are expected (SCAP/Evaluate-STIG/Both?).
   - Recommendation: Define a `VerifyScannerMode` enum with `Scap`, `EvaluateStig`, `Both` values. Add `[ObservableProperty] private VerifyScannerMode verifyScannerMode;` to `MainViewModel.cs`. The test only checks that the binding exists, not what the enum values are.

5. **Diagnostic message exact text for `-f` guard in VerificationWorkflowService**
   - What we know: `ScapArgsOptionsContractTests.MainViewModelDashboard_OnlyAddsFWhenValuePresent` checks `VerificationWorkflowService.cs` contains `"SCAP argument '-f' was missing a filename; removed invalid switch."`.
   - What's unclear: Where exactly in `VerificationWorkflowService` this diagnostic should be added.
   - Recommendation: Add it in `RunScapIfConfigured()` — before launching the process, validate the `Arguments` string. If `-f` appears but is the last token (no following non-flag value), strip it and add the diagnostic.

---

## Sources

### Primary (HIGH confidence)
- Codebase read in full: `/mnt/c/projects/STIGForge/src/STIGForge.Verify/ScapRunner.cs` — hardcoded 30s timeout confirmed
- Codebase read in full: `/mnt/c/projects/STIGForge/src/STIGForge.Verify/VerificationWorkflowService.cs` — CKL-only path confirmed, VerifyOrchestrator not wired
- Codebase read in full: `/mnt/c/projects/STIGForge/src/STIGForge.Verify/CklParser.cs` — insecure XDocument.Load confirmed
- Codebase read in full: `/mnt/c/projects/STIGForge/src/STIGForge.Verify/Adapters/CklAdapter.cs` — LoadSecureXml reference implementation
- Codebase read in full: `/mnt/c/projects/STIGForge/src/STIGForge.Verify/Adapters/ScapResultAdapter.cs` — LoadSecureXml reference implementation
- Codebase read in full: `/mnt/c/projects/STIGForge/src/STIGForge.Verify/VerifyOrchestrator.cs` — fully implemented but unwired
- Codebase read in full: `/mnt/c/projects/STIGForge/src/STIGForge.Verify/NormalizedVerifyResult.cs` — model definition
- Codebase read in full: `/mnt/c/projects/STIGForge/src/STIGForge.Verify/VerifyModels.cs` — ControlResult definition
- Codebase read in full: `/mnt/c/projects/STIGForge/src/STIGForge.Core/Abstractions/Services.cs` — VerificationWorkflowRequest, ScapWorkflowOptions
- Test files read: `ScapRunnerTests.cs`, `EvaluateStigRunnerTests.cs`, `VerifyCommandsTests.cs`, `VerifyReportWriterTests.cs`, `VerificationWorkflowServiceTests.cs`, `ScapArgsOptionsContractTests.cs`, `VerifyViewLayoutContractTests.cs`
- Build error output: confirms `ScapRunner.RunAsync()` and `EvaluateStigRunner.RunAsync()` are missing

### Secondary (MEDIUM confidence)
- Web search: SCC CLI uses `cscc -u <outputDir>` as output directory argument form — confirmed by multiple Ansible automation playbook examples
- Web search: SCC 5.14 is the current release (Q1 2026 STIG bundle); `cscc` is the executable name

### Tertiary (LOW confidence)
- SCC Sessions subdirectory structure — not verifiable from publicly available docs; covered by `SearchOption.AllDirectories` fallback
- SCC timeout settings — SCC scans can take many minutes; 600s (10 min) default is reasonable but unverified against live system

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all code is in-repo and fully readable
- Architecture patterns: HIGH — tests specify the exact API signatures
- SCC CLI arg form: MEDIUM — web search confirms `-u`, but official SCC 5.x manual is behind CAC
- SCC Sessions subdirectory: LOW — use `AllDirectories` fallback as STATE.md instructs
- Pitfalls: HIGH — all identified from reading existing code and test expectations

**Research date:** 2026-02-18
**Valid until:** 2026-03-20 (stable domain — only SCC external behavior is time-sensitive)

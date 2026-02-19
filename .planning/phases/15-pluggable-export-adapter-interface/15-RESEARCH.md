# Phase 15: Pluggable Export Adapter Interface - Research

**Researched:** 2026-02-18
**Domain:** C# interface design, adapter/registry patterns, backward-compatible refactoring, .NET dual-target (net48 + net8.0)
**Confidence:** HIGH — all findings are from direct codebase inspection; no new external libraries involved

---

## Summary

Phase 15 introduces `IExportAdapter`, `ExportAdapterRequest`, `ExportAdapterResult`, `ExportAdapterRegistry`, and `ExportOrchestrator` into `STIGForge.Export`, then retrofits `EmassExporter` and `CklExporter` to implement the interface. The architecture for these components was fully pre-designed in `.planning/research/ARCHITECTURE.md` (researched 2026-02-18 as part of v1.2 planning), and every existing call site was confirmed by reading the production code.

No new NuGet packages are needed. The interface, request/result models, registry, and orchestrator are pure C# types using only BCL. The ARCHITECTURE.md pre-decided the exact member signatures. The primary challenge is backward compatibility: `EmassExporter.ExportAsync()` and `CklExporter.ExportCkl()` are called from `MainViewModel.Export.cs` and `ExportCommands.cs` directly with their current signatures. The refactor must keep those call paths working while also satisfying `IExportAdapter.ExportAsync(ExportAdapterRequest, CancellationToken)`.

The solution is a wrapper approach: add `IExportAdapter` to each existing class, implement the adapter-shaped method, and keep the original methods intact. Both existing call sites compile against the original signatures; the registry path uses the adapter method.

**Primary recommendation:** Define the interface and models in `ExportModels.cs` (already exists), implement the registry and orchestrator as new files in `STIGForge.Export`, retrofit `EmassExporter` and `CklExporter` with the interface without changing their existing public methods. One plan is sufficient.

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| EXP-04 | Export adapters implement a pluggable `IExportAdapter` interface for extensibility | Interface design is fully specified in ARCHITECTURE.md; the pattern mirrors the existing `IVerifyResultAdapter` adapter chain. No new dependencies needed. |
| EXP-05 | Existing eMASS/CKL exporters are refactored to use the `IExportAdapter` contract | `EmassExporter` and `CklExporter` are confirmed readable; backward-compatible wrapper approach keeps existing call sites (`MainViewModel.Export.cs`, `ExportCommands.cs`) unchanged while adding `IExportAdapter` implementation. |

</phase_requirements>

---

## Standard Stack

### Core

| Component | Version / State | Purpose | Why Standard |
|-----------|----------------|---------|--------------|
| `IExportAdapter` | NEW (no package) | Pluggable export contract | Mirrors `IVerifyResultAdapter` — same pattern already proven in verify layer |
| `ExportAdapterRequest` | NEW (no package) | Common input model for all adapters | Carries `BundleRoot`, `Results`, `OutputDirectory`, `FileNameStem`, `Options` |
| `ExportAdapterResult` | NEW (no package) | Common output model for all adapters | Returns `Success`, `OutputPaths`, `Warnings`, `ErrorMessage` (not void — fail-closed) |
| `ExportAdapterRegistry` | NEW (no package) | Resolves adapters by `FormatName` | Same registry pattern as `VerifyOrchestrator` holding `List<IVerifyResultAdapter>` |
| `ExportOrchestrator` | NEW (no package) | Dispatches to selected adapter | Thin dispatcher — looks up adapter from registry, calls `ExportAsync`, returns result |
| BCL only | .NET 8 / .NET 4.8 | All types are pure C# | No NuGet additions; dual-target is safe |

### Supporting

| Component | Version / State | Purpose | When to Use |
|-----------|----------------|---------|-------------|
| `ExportModels.cs` | EXISTING (modify) | Already holds `ExportRequest`, `ExportResult`, `ValidationResult` | Add `IExportAdapter`, `ExportAdapterRequest`, `ExportAdapterResult` here |
| `EmassExporter.cs` | EXISTING (modify) | Full eMASS package builder | Add `IExportAdapter` implementation; keep `ExportAsync(ExportRequest, ct)` |
| `CklExporter.cs` | EXISTING (modify) | CKL/CKLB XML writer | Add `IExportAdapter` implementation; keep `ExportCkl(CklExportRequest)` |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Wrapper/delegation pattern on existing classes | Extract base class | Base class creates coupling; wrapper keeps `EmassExporter` and `CklExporter` independent and self-contained |
| Dictionary-keyed registry | List with LINQ lookup | List + LINQ is simpler and matches how `VerifyOrchestrator` holds adapters; lookup cost is negligible for < 10 formats |
| `ExportOrchestrator` as static class | Instance class | Instance is testable; static is not mockable in tests |

---

## Architecture Patterns

### Recommended Project Structure

No new files needed at the project layer. All new types go into `STIGForge.Export`:

```
src/STIGForge.Export/
  ExportModels.cs          ← EXISTING — add IExportAdapter, ExportAdapterRequest, ExportAdapterResult
  ExportAdapterRegistry.cs ← NEW
  ExportOrchestrator.cs    ← NEW
  EmassExporter.cs         ← EXISTING — implement IExportAdapter
  CklExporter.cs           ← EXISTING — implement IExportAdapter
  StandalonePoamExporter.cs ← no change
  PoamGenerator.cs          ← no change
  AttestationGenerator.cs   ← no change
  EmassPackageValidator.cs  ← no change
  ExportStatusMapper.cs     ← no change
```

### Pattern 1: IExportAdapter Interface (Pre-Decided in ARCHITECTURE.md)

**What:** A common interface that every export format must implement.
**When to use:** Every exporter, including future ones (XCCDF, CSV, Excel in Phases 16-18).

```csharp
// In ExportModels.cs — Source: .planning/research/ARCHITECTURE.md (verified 2026-02-18)
public interface IExportAdapter
{
    string FormatName { get; }              // "eMASS", "CKL", "XCCDF", "CSV", "Excel"
    string[] SupportedExtensions { get; }  // [".xlsx"], [".ckl"], [".xml"], [".csv"]

    Task<ExportAdapterResult> ExportAsync(ExportAdapterRequest request, CancellationToken ct);
}

public sealed class ExportAdapterRequest
{
    public string BundleRoot { get; set; } = string.Empty;
    public IReadOnlyList<ControlResult> Results { get; set; } = Array.Empty<ControlResult>();
    public string OutputDirectory { get; set; } = string.Empty;
    public string? FileNameStem { get; set; }
    public IReadOnlyDictionary<string, string> Options { get; set; }
        = new Dictionary<string, string>();
}

public sealed class ExportAdapterResult
{
    public bool Success { get; set; }
    public IReadOnlyList<string> OutputPaths { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string? ErrorMessage { get; set; }
}
```

Note: `ControlResult` is already imported via `STIGForge.Verify` in `STIGForge.Export`. No new import needed.

### Pattern 2: EmassExporter as IExportAdapter (Wrapper Delegation)

**What:** `EmassExporter` implements `IExportAdapter` by wrapping its existing `ExportAsync` signature.
**Key constraint:** `EmassExporter.ExportAsync(ExportRequest, CancellationToken)` must remain unchanged because `MainViewModel.Export.cs` (WPF) does not call it directly — it actually uses `EmassExporter` injected via DI or called from an export command. Checking: `MainViewModel.Export.cs` does NOT call `EmassExporter.ExportAsync()` directly at all — the WPF view uses `StandalonePoamExporter.ExportPoam()` and `CklExporter.ExportCkl()` directly, with eMASS export presumably from CLI. So eMASS call site is `ExportCommands.cs` which also does NOT call `EmassExporter` directly (confirmed: `ExportCommands.cs` only calls `StandalonePoamExporter.ExportPoam()` and `CklExporter.ExportCkl()`).

**Finding:** `EmassExporter.ExportAsync()` is called only from integration tests (`EmassExporterIntegrationTests.cs`) and the `EmassExporterConsistencyTests.cs` unit test. The eMASS export CLI command (`export-emass`) is NOT in `ExportCommands.cs` — it is either not yet in the CLI or is registered elsewhere.

```csharp
// In EmassExporter.cs — add interface implementation
public sealed class EmassExporter : IExportAdapter
{
    // ... existing constructor, fields, private methods unchanged ...

    // IExportAdapter contract
    public string FormatName => "eMASS";
    public string[] SupportedExtensions => Array.Empty<string>(); // directory output, not single file

    public async Task<ExportAdapterResult> ExportAsync(
        ExportAdapterRequest request, CancellationToken ct)
    {
        try
        {
            var result = await ExportAsync(new ExportRequest
            {
                BundleRoot = request.BundleRoot,
                OutputRoot = request.OutputDirectory
            }, ct).ConfigureAwait(false);

            return new ExportAdapterResult
            {
                Success = result.IsReadyForSubmission,
                OutputPaths = new[] { result.OutputRoot },
                Warnings = result.Warnings,
                ErrorMessage = result.BlockingFailures.Count > 0
                    ? string.Join("; ", result.BlockingFailures)
                    : null
            };
        }
        catch (Exception ex)
        {
            return new ExportAdapterResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // EXISTING method — unchanged signature (tests call this directly)
    public async Task<ExportResult> ExportAsync(ExportRequest request, CancellationToken ct)
    {
        // ... existing implementation unchanged ...
    }
}
```

Note: The two `ExportAsync` overloads differ by parameter type (`ExportAdapterRequest` vs `ExportRequest`). C# resolves them by overload resolution — no ambiguity.

### Pattern 3: CklExporter as IExportAdapter (Wrapper Delegation)

**What:** `CklExporter` is currently a `public static class`. Static classes cannot implement interfaces. The refactor must either convert it to an instance class or create a wrapper.

**Decision:** Convert `CklExporter` from `static` to a non-static class with a default constructor. Keep all existing static methods as-is by making them instance methods that delegate to the static implementation, OR keep them static and expose adapter-shaped instance wrapper. The simplest approach that avoids breaking existing call sites (`CklExporter.ExportCkl(...)`) is to make the static methods remain callable via the class while adding the interface:

**Option A (recommended):** Convert the class to non-static. The static call `CklExporter.ExportCkl(request)` becomes `new CklExporter().ExportCkl(request)` — this BREAKS existing call sites.

**Option B (recommended):** Keep `CklExporter` static, create a separate `CklExportAdapter : IExportAdapter` class that wraps it. The registry registers `CklExportAdapter`, not `CklExporter`.

**Option C:** Make `CklExporter` non-static but keep static convenience methods (`public static CklExportResult ExportCkl(...)`) alongside the instance adapter method. This is idiomatic C# (e.g., `File.ReadAllText` alongside `new StreamReader()`).

The ARCHITECTURE.md says "CklExporter implement IExportAdapter" (not a separate wrapper class). Given that `ExportCommands.cs` calls `CklExporter.ExportCkl(...)` as a static method and `MainViewModel.Export.cs` calls `CklExporter.ExportCkl(...)` as a static method, **Option B (separate CklExportAdapter wrapper class)** preserves all existing call sites without modification and avoids any risk:

```csharp
// NEW file or add to CklExporter.cs:
public sealed class CklExportAdapter : IExportAdapter
{
    public string FormatName => "CKL";
    public string[] SupportedExtensions => new[] { ".ckl", ".cklb" };

    public Task<ExportAdapterResult> ExportAsync(
        ExportAdapterRequest request, CancellationToken ct)
    {
        var cklRequest = new CklExportRequest
        {
            BundleRoot = request.BundleRoot,
            BundleRoots = null,
            OutputDirectory = request.OutputDirectory,
            FileName = request.FileNameStem,
            FileFormat = ParseFormat(request.Options)
        };

        var result = CklExporter.ExportCkl(cklRequest);

        return Task.FromResult(new ExportAdapterResult
        {
            Success = result.ControlCount > 0 || result.Message.Contains("complete"),
            OutputPaths = result.OutputPaths.ToArray(),
            ErrorMessage = result.ControlCount == 0 && !result.Message.Contains("complete")
                ? result.Message : null
        });
    }

    private static CklFileFormat ParseFormat(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("format", out var fmt)
            && string.Equals(fmt, "cklb", StringComparison.OrdinalIgnoreCase))
            return CklFileFormat.Cklb;
        return CklFileFormat.Ckl;
    }
}
```

This means the success criteria "Existing `CklExporter` implements `IExportAdapter`" is satisfied through `CklExportAdapter` which wraps `CklExporter`. If the phase success criteria requires `CklExporter` itself to implement the interface (not a wrapper), Option C (non-static with static convenience delegates) must be used.

**Clarification needed before planning:** Does "CklExporter implements IExportAdapter" mean the class itself must carry the interface, or is a named wrapper acceptable? The planner should decide this. Recommendation: use the wrapper `CklExportAdapter` (Option B) because it requires zero changes to existing call sites.

### Pattern 4: ExportAdapterRegistry

**What:** Holds registered `IExportAdapter` instances; resolves by `FormatName`.

```csharp
// NEW ExportAdapterRegistry.cs in STIGForge.Export
public sealed class ExportAdapterRegistry
{
    private readonly List<IExportAdapter> _adapters = new();

    public void Register(IExportAdapter adapter)
    {
        if (adapter == null) throw new ArgumentNullException(nameof(adapter));
        _adapters.Add(adapter);
    }

    public IExportAdapter? TryResolve(string formatName)
    {
        return _adapters.FirstOrDefault(a =>
            string.Equals(a.FormatName, formatName, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<IExportAdapter> GetAll() => _adapters.AsReadOnly();
}
```

### Pattern 5: ExportOrchestrator

**What:** Accepts format name + request, resolves adapter from registry, dispatches, returns result.

```csharp
// NEW ExportOrchestrator.cs in STIGForge.Export
public sealed class ExportOrchestrator
{
    private readonly ExportAdapterRegistry _registry;

    public ExportOrchestrator(ExportAdapterRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<ExportAdapterResult> ExportAsync(
        string formatName,
        ExportAdapterRequest request,
        CancellationToken ct)
    {
        var adapter = _registry.TryResolve(formatName);
        if (adapter == null)
        {
            return new ExportAdapterResult
            {
                Success = false,
                ErrorMessage = $"No export adapter registered for format '{formatName}'."
            };
        }

        return await adapter.ExportAsync(request, ct).ConfigureAwait(false);
    }
}
```

### Anti-Patterns to Avoid

- **Adding `IExportAdapter` members directly to `CklExporter` as a static class:** Static classes cannot implement interfaces in C#. Attempting this causes a compile error.
- **Changing existing method signatures:** `EmassExporter.ExportAsync(ExportRequest, ct)` and `CklExporter.ExportCkl(CklExportRequest)` have existing tests. Do not change their signatures.
- **Creating a fourth result model:** `ExportAdapterResult` is the adapter boundary model. It does NOT replace `ExportResult` or `CklExportResult` — those remain in the existing call paths.
- **Making `ExportOrchestrator` static:** A static orchestrator is untestable. It must be an instance class for test injection.
- **Catching all exceptions silently in adapters:** The adapter wrapper should return `Success = false` with `ErrorMessage` for expected export failures, but unexpected exceptions (like `OutOfMemoryException`) should propagate. Catch `Exception` only for expected failure modes (`DirectoryNotFoundException`, `IOException`, `ArgumentException`).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Format name lookup | Custom switch/enum dispatch | `ExportAdapterRegistry.TryResolve()` + `IExportAdapter.FormatName` | Registry is extensible; switch requires code changes for every new format |
| Adapter failure handling | Caller-side null checks everywhere | `ExportAdapterResult.Success = false + ErrorMessage` | Fail-closed: no void returns, caller always has a result to inspect |
| CKL format wrapping | Copying CKL logic into adapter method | `CklExportAdapter` delegates to `CklExporter.ExportCkl()` | CklExporter is already tested; don't duplicate logic |

---

## Common Pitfalls

### Pitfall 1: Static Class Cannot Implement Interface

**What goes wrong:** Developer adds `: IExportAdapter` to `public static class CklExporter`.
**Why it happens:** C# does not allow static classes to implement interfaces.
**How to avoid:** Use the wrapper class `CklExportAdapter` (Option B) OR convert `CklExporter` to a non-static class. Do NOT attempt to implement the interface on the static class.
**Warning signs:** Compile error `CS0714: 'CklExporter': static classes cannot implement interfaces.`

### Pitfall 2: Overload Ambiguity in EmassExporter

**What goes wrong:** Two methods named `ExportAsync` with different parameter types create confusion for call sites using implicit conversions or reflection.
**Why it happens:** `EmassExporter` will have both `ExportAsync(ExportRequest, ct)` (existing) and `ExportAsync(ExportAdapterRequest, ct)` (new IExportAdapter implementation).
**How to avoid:** The parameter types are distinct (`ExportRequest` vs `ExportAdapterRequest`), so C# overload resolution is unambiguous. No issue in practice. The existing tests call `ExportAsync(ExportRequest, ct)` and will not be affected.
**Warning signs:** If a test calls `ExportAsync(new ExportRequest {...}, ct)` and it suddenly dispatches to the adapter overload — this cannot happen because the types are different.

### Pitfall 3: ExportAdapterResult.Success Semantics for eMASS

**What goes wrong:** `EmassExporter` returns `IsReadyForSubmission = false` even for valid exports that have warnings (not blocking failures). The adapter wrapper maps `IsReadyForSubmission` to `Success`, which could cause the orchestrator to report failure for valid-but-warned packages.
**Why it happens:** `EmassExporter.ExportResult.IsReadyForSubmission` means "zero blocking errors" — it can be true with warnings. The adapter's `Success` should mirror this.
**How to avoid:** Map `IsReadyForSubmission` to `Success` (not `Errors.Count == 0`). A package with warnings but no blocking failures is a success at the adapter level.
**Warning signs:** `ExportAdapterResult.Success = false` for packages where `validationResult.Errors.Count == 0`.

### Pitfall 4: CklExporter Empty-Result Success Definition

**What goes wrong:** `CklExporter.ExportCkl()` returns `ControlCount = 0` and `Message = "No verification results found."` for an empty bundle — this is not an error, it is a legitimate empty export. The adapter must not mark this as `Success = false`.
**Why it happens:** The adapter wrapper naively checks `ControlCount > 0` to determine success.
**How to avoid:** Map `Success` to `true` when no exception was thrown, regardless of `ControlCount`. `ControlCount == 0` is a valid outcome. Surface the message as a warning, not as `ErrorMessage`.
**Warning signs:** Tests for empty bundles returning `Success = false` when the operator ran export against a fresh bundle.

### Pitfall 5: ExportAdapterRequest.Results vs BundleRoot

**What goes wrong:** `ExportAdapterRequest` carries both `Results` (pre-loaded `ControlResult` list) and `BundleRoot` (directory path). `EmassExporter` ignores the `Results` list — it always reads the bundle root directly. If the caller populates `Results` and passes an empty `BundleRoot`, eMASS export fails.
**Why it happens:** The interface is designed for adapters that consume `Results` directly (CSV, XCCDF), but `EmassExporter` must read from disk (it copies files, reads manifests, generates hashes). The `BundleRoot` is mandatory for eMASS; `Results` is optional.
**How to avoid:** Document clearly: `EmassExporter` ignores `request.Results` and requires `request.BundleRoot`. The adapter wrapper should validate `BundleRoot` is non-empty before delegating. `CklExportAdapter` also reads from `BundleRoot` (via `CklExporter.ExportCkl()`), so same constraint.
**Warning signs:** `EmassExporter` adapter wrapper called with populated `Results` but empty `BundleRoot` throws `ArgumentException`.

### Pitfall 6: net48 Compatibility of ExportAdapterRegistry

**What goes wrong:** Using `IReadOnlyList<T>.AsReadOnly()` extension method not available on net48.
**Why it happens:** `List<T>.AsReadOnly()` returns `ReadOnlyCollection<T>`. This is available on net48 via `System.Collections.ObjectModel` — it IS available. No issue.
**How to avoid:** Verify BCL API is available on both targets. `List<T>.AsReadOnly()` is available from .NET Framework 2.0. No issue.
**Warning signs:** None — this is a false alarm. Both targets support `AsReadOnly()`.

---

## Code Examples

### Complete IExportAdapter + Models (Place in ExportModels.cs)

```csharp
// Source: .planning/research/ARCHITECTURE.md (2026-02-18) — confirmed design
// Place in namespace STIGForge.Export

public interface IExportAdapter
{
    string FormatName { get; }
    string[] SupportedExtensions { get; }
    Task<ExportAdapterResult> ExportAsync(ExportAdapterRequest request, CancellationToken ct);
}

public sealed class ExportAdapterRequest
{
    public string BundleRoot { get; set; } = string.Empty;
    public IReadOnlyList<ControlResult> Results { get; set; } = Array.Empty<ControlResult>();
    public string OutputDirectory { get; set; } = string.Empty;
    public string? FileNameStem { get; set; }
    public IReadOnlyDictionary<string, string> Options { get; set; }
        = new Dictionary<string, string>();
}

public sealed class ExportAdapterResult
{
    public bool Success { get; set; }
    public IReadOnlyList<string> OutputPaths { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string? ErrorMessage { get; set; }
}
```

### ExportAdapterRegistry (New File)

```csharp
// Source: pattern mirrors VerifyOrchestrator in STIGForge.Verify
namespace STIGForge.Export;

public sealed class ExportAdapterRegistry
{
    private readonly List<IExportAdapter> _adapters = new();

    public void Register(IExportAdapter adapter)
    {
        if (adapter == null) throw new ArgumentNullException(nameof(adapter));
        _adapters.Add(adapter);
    }

    public IExportAdapter? TryResolve(string formatName)
    {
        return _adapters.FirstOrDefault(a =>
            string.Equals(a.FormatName, formatName, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<IExportAdapter> GetAll() => _adapters.AsReadOnly();
}
```

### ExportOrchestrator (New File)

```csharp
// Source: design decision from ARCHITECTURE.md
namespace STIGForge.Export;

public sealed class ExportOrchestrator
{
    private readonly ExportAdapterRegistry _registry;

    public ExportOrchestrator(ExportAdapterRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<ExportAdapterResult> ExportAsync(
        string formatName,
        ExportAdapterRequest request,
        CancellationToken ct)
    {
        var adapter = _registry.TryResolve(formatName);
        if (adapter == null)
        {
            return new ExportAdapterResult
            {
                Success = false,
                ErrorMessage = $"No export adapter registered for format '{formatName}'."
            };
        }

        return await adapter.ExportAsync(request, ct).ConfigureAwait(false);
    }
}
```

### EmassExporter IExportAdapter Members (Add to EmassExporter.cs)

```csharp
// Add to existing EmassExporter class:
public string FormatName => "eMASS";
public string[] SupportedExtensions => Array.Empty<string>(); // produces a directory

// Explicit IExportAdapter.ExportAsync (uses different parameter type from existing ExportAsync)
async Task<ExportAdapterResult> IExportAdapter.ExportAsync(
    ExportAdapterRequest request, CancellationToken ct)
{
    try
    {
        var result = await ExportAsync(new ExportRequest
        {
            BundleRoot = request.BundleRoot,
            OutputRoot = string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? null : request.OutputDirectory
        }, ct).ConfigureAwait(false);

        return new ExportAdapterResult
        {
            Success = result.IsReadyForSubmission,
            OutputPaths = new[] { result.OutputRoot },
            Warnings = result.Warnings,
            ErrorMessage = result.BlockingFailures.Count > 0
                ? string.Join("; ", result.BlockingFailures) : null
        };
    }
    catch (Exception ex)
        when (ex is ArgumentException or DirectoryNotFoundException or FileNotFoundException)
    {
        return new ExportAdapterResult { Success = false, ErrorMessage = ex.Message };
    }
}
```

Note: Using explicit interface implementation (`Task<ExportAdapterResult> IExportAdapter.ExportAsync(...)`) avoids any ambiguity with the existing `Task<ExportResult> ExportAsync(ExportRequest, ct)` overload. Explicit interface implementation is only accessible when the caller holds an `IExportAdapter` reference.

### CklExportAdapter (New class alongside CklExporter)

```csharp
// Add to CklExporter.cs or new CklExportAdapter.cs
namespace STIGForge.Export;

public sealed class CklExportAdapter : IExportAdapter
{
    public string FormatName => "CKL";
    public string[] SupportedExtensions => new[] { ".ckl", ".cklb" };

    public Task<ExportAdapterResult> ExportAsync(
        ExportAdapterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BundleRoot))
            return Task.FromResult(new ExportAdapterResult
            {
                Success = false,
                ErrorMessage = "BundleRoot is required for CKL export."
            });

        request.Options.TryGetValue("format", out var fmtStr);
        var format = string.Equals(fmtStr, "cklb", StringComparison.OrdinalIgnoreCase)
            ? CklFileFormat.Cklb : CklFileFormat.Ckl;

        request.Options.TryGetValue("include-csv", out var csvStr);
        var includeCsv = string.Equals(csvStr, "true", StringComparison.OrdinalIgnoreCase);

        var result = CklExporter.ExportCkl(new CklExportRequest
        {
            BundleRoot = request.BundleRoot,
            OutputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? null : request.OutputDirectory,
            FileName = request.FileNameStem,
            FileFormat = format,
            IncludeCsv = includeCsv
        });

        return Task.FromResult(new ExportAdapterResult
        {
            Success = true,   // ExportCkl returns a result, not void — no exception = success
            OutputPaths = result.OutputPaths.ToArray(),
            Warnings = result.ControlCount == 0
                ? new[] { result.Message } : Array.Empty<string>()
        });
    }
}
```

---

## Call Site Inventory (What Must Remain Unchanged)

### MainViewModel.Export.cs (WPF)
- `StandalonePoamExporter.ExportPoam(request)` — static call, no change needed
- `CklExporter.ExportCkl(request)` — static call, no change needed
- `EmassExporter.ExportAsync(request, ct)` — **NOT called here** (confirmed by reading the file)

### ExportCommands.cs (CLI)
- `StandalonePoamExporter.ExportPoam(request)` — static call, no change needed
- `CklExporter.ExportCkl(request)` — static call, no change needed
- `EmassExporter.ExportAsync(request, ct)` — **NOT called here** (no `export-emass` CLI command found in `ExportCommands.cs`)

### Test Files (Must Not Break)
- `CklExporterTests.cs` — calls `CklExporter.ExportCkl(new CklExportRequest {...})` as static
- `EmassExporterConsistencyTests.cs` — calls `emassExporter.ExportAsync(new ExportRequest {...}, ct)` as instance
- `EmassPackageValidatorTests.cs` — no exporter calls; no change
- `ExportGeneratorTests.cs` — `PoamGenerator`, `AttestationGenerator`, `EmassPackageValidator`; no change
- `ExportStatusMapperTests.cs` — static calls; no change

---

## State of the Art

| Old Approach | Current Approach | What Phase 15 Changes |
|--------------|-----------------|----------------------|
| Direct static calls: `CklExporter.ExportCkl(...)` | Same | Same (call sites stay static) |
| No common interface | Separate, incompatible result types | `IExportAdapter` unifies the contract |
| WPF export tab: one button per format | Same (POAM button, CKL button) | After Phase 15: registry available for Phase 19 to build picker |
| CLI: format-specific commands | Same (`export-poam`, `export-ckl`) | After Phase 15: `ExportOrchestrator` available for Phase 16-18 CLI commands |

---

## Open Questions

1. **CklExporter static vs. instance (HIGH importance)**
   - What we know: `CklExporter` is `public static class`. Static classes cannot implement interfaces.
   - What's unclear: Phase success criteria says "Existing `CklExporter` implements `IExportAdapter`" — does this require the class itself to carry the interface, or is a named adapter wrapper (`CklExportAdapter`) acceptable?
   - Recommendation: Use `CklExportAdapter` (wrapper class). Name it `CklExportAdapter` and document it as "the IExportAdapter implementation for CKL format, backed by CklExporter." This satisfies the spirit of EXP-05 without breaking the static call sites. If the letter requires the class itself, convert `CklExporter` to non-static and add `public static CklExportResult ExportCkl(CklExportRequest request) => new CklExporter().ExportCkl(request);` as a static convenience method for existing callers.

2. **ExportAdapterResult.Success for empty-results CKL export (LOW importance)**
   - What we know: `CklExporter.ExportCkl()` returns `ControlCount = 0, Message = "No verification results found."` for empty bundles — no exception thrown.
   - What's unclear: Should `Success = false` when no results are found?
   - Recommendation: `Success = true` for no-exception path; surface the message as a warning in `Warnings`. An empty export is a valid outcome.

3. **Where to register adapters (MEDIUM importance)**
   - What we know: `ExportAdapterRegistry` must be populated somewhere before use. In DI-based systems (Cli host), this is typically done in `Program.cs` / `HostBuilder`. In the WPF app, it may be done in `App.xaml.cs`.
   - What's unclear: Phase 15 wires the interface and refactors the adapters. Phase 19 adds the WPF picker that uses the registry. The planner needs to decide if registration setup belongs in Phase 15 or Phase 19.
   - Recommendation: Phase 15 creates the registry and leaves registration wiring for Phase 19 (WPF picker) and Phases 16-18 (when each new adapter is added). Phase 15 should only register `EmassExporter` (or `EmassExportAdapter`) and `CklExportAdapter` into the registry in unit tests — not wire DI/IoC yet.

---

## Sources

### Primary (HIGH confidence)

- Codebase read in full:
  - `/mnt/c/projects/STIGForge/src/STIGForge.Export/CklExporter.cs` — static class, `ExportCkl` signature, call pattern
  - `/mnt/c/projects/STIGForge/src/STIGForge.Export/EmassExporter.cs` — instance class, `ExportAsync(ExportRequest, ct)` signature, dependency injection pattern
  - `/mnt/c/projects/STIGForge/src/STIGForge.Export/ExportModels.cs` — existing models: `ExportRequest`, `ExportResult`, `ValidationResult`, `ValidationMetrics`
  - `/mnt/c/projects/STIGForge/src/STIGForge.Export/StandalonePoamExporter.cs` — static; no change needed
  - `/mnt/c/projects/STIGForge/src/STIGForge.App/MainViewModel.Export.cs` — call sites confirmed: static `CklExporter.ExportCkl()`, static `StandalonePoamExporter.ExportPoam()`; eMASS NOT called
  - `/mnt/c/projects/STIGForge/src/STIGForge.Cli/Commands/ExportCommands.cs` — call sites confirmed: same as WPF; no `EmassExporter` usage
  - `/mnt/c/projects/STIGForge/src/STIGForge.Export/STIGForge.Export.csproj` — dual-target net48+net8.0 confirmed; no new packages needed
  - `/mnt/c/projects/STIGForge/src/STIGForge.Reporting/ReportGenerator.cs` — stub confirmed; not touched in Phase 15
- Test files read in full:
  - `tests/STIGForge.UnitTests/Export/CklExporterTests.cs` — static call sites; must not break
  - `tests/STIGForge.UnitTests/Export/EmassExporterConsistencyTests.cs` — instance call sites; must not break
- Planning documents read:
  - `.planning/research/ARCHITECTURE.md` — pre-designed interface signatures (HIGH confidence)
  - `.planning/REQUIREMENTS.md` — EXP-04 and EXP-05 definitions
  - `.planning/ROADMAP.md` — Phase 15 success criteria and prior decisions
  - `.planning/phases/14-scc-verify-correctness-and-model-unification/14-RESEARCH.md` — context from prior phase
  - `.planning/phases/14-scc-verify-correctness-and-model-unification/14-01-SUMMARY.md` — confirmed Phase 14 completion
  - `.planning/phases/14-scc-verify-correctness-and-model-unification/14-02-SUMMARY.md` — confirmed Phase 14 completion
- Build / test run:
  - All Export tests pass (58 tests, 0 failures in export test filter)
  - Overall: 23 pre-existing failures in Views/Verify tests unrelated to Phase 15

### Secondary (MEDIUM confidence)

- None required — all needed information is in the codebase and planning documents.

### Tertiary (LOW confidence)

- None.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all types are BCL or in-repo
- Architecture: HIGH — interface design is pre-decided in ARCHITECTURE.md; call sites confirmed by code read
- Pitfalls: HIGH — static class constraint is a C# language guarantee; other pitfalls from direct code inspection
- Open questions: Planner decisions (not research gaps)

**Research date:** 2026-02-18
**Valid until:** 2026-03-20 (stable domain — pure C# interface; no external dependency changes)

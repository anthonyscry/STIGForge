# Evidence Compiler Pipeline for CKL Export — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enrich STIGForge's CKL checklist exports with raw evidence in FINDING_DETAILS and auto-generated compliance rationale in COMMENTS, using existing evidence artifacts from disk.

**Architecture:** New `IEvidenceCompiler` interface in Core, `EvidenceCompiler` implementation in Evidence project that reads evidence via existing `EvidenceIndexService`. `CommentTemplateEngine` generates human-readable COMMENTS. Static `CklExporter.ExportCkl` gets an optional `IEvidenceCompiler?` parameter for backward compatibility. CLI export command resolves compiler from DI and passes it through.

**Tech Stack:** .NET 8, C#, xUnit + FluentAssertions, System.Text.Json, System.Xml.Linq

**Build note:** No .NET SDK on Linux dev machine. Build and test on Hyper-V host `triton-ajt` via `ssh majordev@triton-ajt`. Tarball + scp to `C:\STIGForge`, then `dotnet build/test`.

**Design doc:** `~/.gstack/projects/anthonyscry-STIGForge/anthonyscry-main-design-20260323-162414.md`
**Test plan:** `~/.gstack/projects/anthonyscry-STIGForge/anthonyscry-main-test-plan-20260323-164244.md`

---

## Key existing code to reuse (DO NOT DUPLICATE)

| Existing | Location | What it does |
|---|---|---|
| `EvidenceIndexService` | `src/STIGForge.Evidence/EvidenceIndexService.cs` | Scans `by_control/`, reads metadata JSON, provides `GetEvidenceForControl()` |
| `EvidenceIndexEntry` | `src/STIGForge.Evidence/EvidenceIndexModels.cs` | Has ControlKey, RuleId, Type, Source, TimestampUtc, StepName, RunId, RelativePath |
| `EvidenceIndex` | `src/STIGForge.Evidence/EvidenceIndexModels.cs` | Top-level index with BundleRoot, Entries list |
| `EmassExporter.ResolveEvidencePaths()` | `src/STIGForge.Export/EmassExporter.cs:647` | Dual-key probe (RuleId + VulnId directories) — pattern to follow |
| `StepEvidenceWriter` | `src/STIGForge.Apply/Steps/StepEvidenceWriter.cs` | Already writes step-level apply evidence with SHA-256 and continuity |
| `VerifyStatus` enum | `src/STIGForge.Verify/NormalizedVerifyResult.cs` | Pass, Fail, NotApplicable, NotReviewed, etc. |
| `ExportStatusMapper` | `src/STIGForge.Export/` | Maps between status representations |

## Key types

| Type | Project | Role in this feature |
|---|---|---|
| `STIGForge.Verify.ControlResult` | Verify | Used by `CklExporter.BuildVulnElements()` — has VulnId, RuleId, Status, Tool, VerifiedAt, FindingDetails, Comments |
| `STIGForge.Export.CklExporter` | Export | **Static class.** Target for integration. Called directly by `ExportCommands.cs:96` |
| `STIGForge.Core.Services.CklExporter` | Core | **Instance class** (different type). Used for CKL sync. NOT the target. |
| `EvidenceIndexService` | Evidence | Instance class, takes `bundleRoot` in constructor. Has async `BuildIndexAsync()` |

## File map

**New files:**
1. `src/STIGForge.Core/Abstractions/IEvidenceCompiler.cs` — interface + input/output records
2. `src/STIGForge.Evidence/EvidenceCompiler.cs` — implementation using EvidenceIndexService
3. `src/STIGForge.Evidence/CommentTemplateEngine.cs` — pure-function COMMENTS generation
4. `tests/STIGForge.UnitTests/Evidence/EvidenceCompilerTests.cs` — unit tests
5. `tests/STIGForge.UnitTests/Evidence/CommentTemplateEngineTests.cs` — unit tests

**Modified files:**
6. `src/STIGForge.Export/CklExporter.cs` — add optional `IEvidenceCompiler?` param, enrichment loop
7. `src/STIGForge.Cli/Commands/ExportCommands.cs` — resolve IEvidenceCompiler from DI, pass to ExportCkl
8. `src/STIGForge.Cli/CliHostFactory.cs` — register IEvidenceCompiler -> EvidenceCompiler

---

### Task 1: IEvidenceCompiler Interface + Models

**Files:**
- Create: `src/STIGForge.Core/Abstractions/IEvidenceCompiler.cs`

- [ ] **Step 1: Create the interface and model records**

**CRITICAL:** STIGForge.Core has ZERO project references — it is the base project. The interface CANNOT reference `EvidenceIndex` or any Evidence project types. Solution: the interface takes only `bundleRoot` (string) and the implementation handles index building/caching internally.

```csharp
namespace STIGForge.Core.Abstractions;

/// <summary>
/// Input for evidence compilation — lightweight record that carries only
/// the fields needed from Verify.ControlResult without coupling to the Verify project.
/// </summary>
public sealed record EvidenceCompilationInput(
    string? VulnId,
    string? RuleId,
    string? Status,
    string? Tool,
    DateTimeOffset? VerifiedAt,
    string? FindingDetails,
    string? Comments);

/// <summary>
/// Compiled evidence output — FINDING_DETAILS (machine-grade) and COMMENTS (human-grade).
/// Null fields mean "no enrichment available for this field."
/// </summary>
public sealed record CompiledEvidence(
    string? FindingDetails,
    string? Comments);

/// <summary>
/// Compiles raw evidence artifacts + verify/apply context into
/// auditor-ready FINDING_DETAILS and COMMENTS text for CKL export.
/// The implementation handles evidence index building/caching internally.
/// </summary>
public interface IEvidenceCompiler
{
    /// <summary>
    /// Compile evidence for a single control. Returns null if no evidence is available.
    /// The implementation reads evidence artifacts from {bundleRoot}/Evidence/by_control/.
    /// Index is built and cached per bundleRoot automatically.
    /// </summary>
    /// <param name="input">Control data from the verify pipeline.</param>
    /// <param name="bundleRoot">Bundle root path — evidence is at {bundleRoot}/Evidence/by_control/.</param>
    CompiledEvidence? CompileEvidence(
        EvidenceCompilationInput input,
        string bundleRoot);
}
```

- [ ] **Step 3: Commit**

```bash
git add src/STIGForge.Core/Abstractions/IEvidenceCompiler.cs
git commit -m "feat(core): add IEvidenceCompiler interface and evidence compilation models"
```

---

### Task 2: CommentTemplateEngine

**Files:**
- Create: `src/STIGForge.Evidence/CommentTemplateEngine.cs`
- Create: `tests/STIGForge.UnitTests/Evidence/CommentTemplateEngineTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using STIGForge.Evidence;
using Xunit;

namespace STIGForge.UnitTests.Evidence;

public class CommentTemplateEngineTests
{
    [Fact]
    public void Generate_PassStatus_ReturnsCompliantRationale()
    {
        var result = CommentTemplateEngine.Generate(
            status: "pass",
            keyEvidence: "Registry key EnableLUA is set to 1",
            toolName: "Evaluate-STIG",
            verifiedAt: new DateTimeOffset(2026, 3, 20, 14, 35, 0, TimeSpan.Zero),
            artifactFileNames: new[] { "registry_export.txt" });

        result.Should().Contain("Verified compliant");
        result.Should().Contain("EnableLUA");
        result.Should().Contain("Evaluate-STIG");
        result.Should().Contain("2026-03-20");
        result.Should().Contain("registry_export.txt");
    }

    [Fact]
    public void Generate_FailStatus_ReturnsOpenFindingRationale()
    {
        var result = CommentTemplateEngine.Generate(
            status: "fail",
            keyEvidence: "Registry key EnableLUA is set to 0",
            toolName: "SCAP",
            verifiedAt: new DateTimeOffset(2026, 3, 20, 14, 35, 0, TimeSpan.Zero),
            artifactFileNames: Array.Empty<string>());

        result.Should().Contain("Open finding");
        result.Should().Contain("EnableLUA");
    }

    [Fact]
    public void Generate_NotApplicable_ReturnsNaRationale()
    {
        var result = CommentTemplateEngine.Generate(
            status: "notapplicable",
            keyEvidence: null,
            toolName: "Manual CKL",
            verifiedAt: null,
            artifactFileNames: Array.Empty<string>());

        result.Should().Contain("Not applicable");
    }

    [Fact]
    public void Generate_NotReviewed_ReturnsAwaitingRationale()
    {
        var result = CommentTemplateEngine.Generate(
            status: "notreviewed",
            keyEvidence: null,
            toolName: null,
            verifiedAt: null,
            artifactFileNames: Array.Empty<string>());

        result.Should().Contain("Awaiting review");
    }

    [Fact]
    public void Generate_NullTool_OmitsToolReference()
    {
        var result = CommentTemplateEngine.Generate(
            status: "pass",
            keyEvidence: "Setting is configured",
            toolName: null,
            verifiedAt: null,
            artifactFileNames: Array.Empty<string>());

        result.Should().NotContain("verified by");
    }

    [Fact]
    public void Generate_MergedTool_HandlesGracefully()
    {
        var result = CommentTemplateEngine.Generate(
            status: "pass",
            keyEvidence: "Setting is configured",
            toolName: "Merged",
            verifiedAt: new DateTimeOffset(2026, 3, 20, 14, 35, 0, TimeSpan.Zero),
            artifactFileNames: Array.Empty<string>());

        result.Should().Contain("Verified compliant");
        result.Should().Contain("verified by multiple tools");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~CommentTemplateEngineTests" -v minimal`
Expected: FAIL — `CommentTemplateEngine` not found

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text;

namespace STIGForge.Evidence;

/// <summary>
/// Generates human-readable COMMENTS text for CKL export based on
/// control status, evidence, and verification context.
/// Pure function — no I/O, no side effects.
/// </summary>
public static class CommentTemplateEngine
{
    private const string StiGForgeSentinel = "--- STIGForge Evidence ---";

    public static string Generate(
        string? status,
        string? keyEvidence,
        string? toolName,
        DateTimeOffset? verifiedAt,
        IReadOnlyList<string> artifactFileNames)
    {
        var sb = new StringBuilder();

        // Status rationale
        var normalizedStatus = NormalizeStatus(status);
        sb.Append(normalizedStatus switch
        {
            "pass" => "Verified compliant.",
            "fail" => "Open finding.",
            "notapplicable" => "Not applicable.",
            "notreviewed" => "Awaiting review. No automated check available for this control.",
            _ => "Status: " + (status ?? "unknown") + "."
        });

        // Key evidence point
        if (!string.IsNullOrWhiteSpace(keyEvidence))
        {
            sb.Append(' ');
            sb.Append(keyEvidence.Trim());
            if (!keyEvidence.TrimEnd().EndsWith('.'))
                sb.Append('.');
        }

        // Verification context
        if (!string.IsNullOrWhiteSpace(toolName) && verifiedAt.HasValue)
        {
            sb.Append(' ');
            if (string.Equals(toolName, "Merged", StringComparison.OrdinalIgnoreCase))
                sb.Append("Scan verified by multiple tools on ");
            else
                sb.AppendFormat("Scan verified by {0} on ", toolName);
            sb.Append(verifiedAt.Value.ToString("yyyy-MM-dd"));
            sb.Append('.');
        }

        // Artifact references
        if (artifactFileNames.Count > 0)
        {
            sb.Append(" Evidence artifacts: ");
            sb.Append(string.Join(", ", artifactFileNames));
            sb.Append('.');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Check if text already contains the STIGForge evidence sentinel.
    /// Used for idempotency — prevent double-appending on re-export.
    /// </summary>
    public static bool ContainsSentinel(string? text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Contains(StiGForgeSentinel, StringComparison.Ordinal);
    }

    /// <summary>The separator used when appending evidence to existing content.</summary>
    public static string Separator => "\n\n" + StiGForgeSentinel + "\n";

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "unknown";

        var s = status.Trim().ToLowerInvariant()
            .Replace("_", "").Replace("-", "").Replace(" ", "");

        return s switch
        {
            "pass" or "notafinding" or "compliant" => "pass",
            "fail" or "open" or "noncompliant" or "error" => "fail",
            "notapplicable" or "na" => "notapplicable",
            "notreviewed" or "notchecked" or "informational" => "notreviewed",
            _ => s
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~CommentTemplateEngineTests" -v minimal`
Expected: PASS (all 6 tests)

- [ ] **Step 5: Commit**

```bash
git add src/STIGForge.Evidence/CommentTemplateEngine.cs tests/STIGForge.UnitTests/Evidence/CommentTemplateEngineTests.cs
git commit -m "feat(evidence): add CommentTemplateEngine for CKL COMMENTS generation"
```

---

### Task 3: EvidenceCompiler Implementation

**Files:**
- Create: `src/STIGForge.Evidence/EvidenceCompiler.cs`
- Create: `tests/STIGForge.UnitTests/Evidence/EvidenceCompilerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using FluentAssertions;
using STIGForge.Core;
using STIGForge.Core.Abstractions;
using STIGForge.Evidence;
using Xunit;

namespace STIGForge.UnitTests.Evidence;

public class EvidenceCompilerTests : IDisposable
{
    private readonly string _tempDir;

    public EvidenceCompilerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CompileEvidence_WithNoEvidenceDir_ReturnsNull()
    {
        var compiler = new EvidenceCompiler();
        var input = MakeInput("V-12345", "SV-12345r1_rule", "pass");

        var result = compiler.CompileEvidence(input, _tempDir);

        result.Should().BeNull();
    }

    [Fact]
    public void CompileEvidence_WithArtifacts_ReturnsPopulated()
    {
        // Set up evidence directory and files
        var controlDir = Path.Combine(_tempDir, "Evidence", "by_control", "V-12345");
        Directory.CreateDirectory(controlDir);
        File.WriteAllText(Path.Combine(controlDir, "registry_export.txt"),
            "HKLM\\SOFTWARE\\Policies\\EnableLUA\nREG_DWORD 0x1");
        File.WriteAllText(Path.Combine(controlDir, "registry_export.json"),
            JsonSerializer.Serialize(new { ControlId = "V-12345", Type = "Registry", Source = "reg.exe", TimestampUtc = "2026-03-20T14:30:00Z", Sha256 = "abc123" }));

        var compiler = new EvidenceCompiler();
        var input = MakeInput("V-12345", "SV-12345r1_rule", "pass", tool: "Evaluate-STIG",
            verifiedAt: new DateTimeOffset(2026, 3, 20, 14, 35, 0, TimeSpan.Zero));

        var result = compiler.CompileEvidence(input, _tempDir);

        result.Should().NotBeNull();
        result!.FindingDetails.Should().Contain("STIGForge Evidence Report");
        result.FindingDetails.Should().Contain("V-12345");
        result.FindingDetails.Should().Contain("EnableLUA");
        result.Comments.Should().Contain("Verified compliant");
        result.Comments.Should().Contain("Evaluate-STIG");
    }

    [Fact]
    public void CompileEvidence_WithNullIds_ReturnsNull()
    {
        var compiler = new EvidenceCompiler();
        var input = MakeInput(null, null, "pass");

        var result = compiler.CompileEvidence(input, _tempDir);

        result.Should().BeNull();
    }

    [Fact]
    public void CompileEvidence_LargeArtifact_Truncates()
    {
        var controlDir = Path.Combine(_tempDir, "Evidence", "by_control", "V-99999");
        Directory.CreateDirectory(controlDir);
        File.WriteAllText(Path.Combine(controlDir, "large.txt"), new string('X', 10000));
        File.WriteAllText(Path.Combine(controlDir, "large.json"),
            JsonSerializer.Serialize(new { ControlId = "V-99999", Type = "Command", Source = "test", TimestampUtc = "2026-03-20T14:30:00Z", Sha256 = "abc" }));

        var compiler = new EvidenceCompiler();
        var input = MakeInput("V-99999", null, "pass");

        var result = compiler.CompileEvidence(input, _tempDir);

        result.Should().NotBeNull();
        result!.FindingDetails.Should().Contain("[truncated]");
        result.FindingDetails!.Length.Should().BeLessThan(10000);
    }

    [Fact]
    public void CompileEvidence_CachesIndexPerBundle()
    {
        // Set up two controls with evidence
        var dir1 = Path.Combine(_tempDir, "Evidence", "by_control", "V-11111");
        Directory.CreateDirectory(dir1);
        File.WriteAllText(Path.Combine(dir1, "test.txt"), "evidence1");
        File.WriteAllText(Path.Combine(dir1, "test.json"),
            JsonSerializer.Serialize(new { ControlId = "V-11111", Type = "File", Source = "test", TimestampUtc = "2026-03-20T14:30:00Z", Sha256 = "a" }));

        var dir2 = Path.Combine(_tempDir, "Evidence", "by_control", "V-22222");
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, "test.txt"), "evidence2");
        File.WriteAllText(Path.Combine(dir2, "test.json"),
            JsonSerializer.Serialize(new { ControlId = "V-22222", Type = "File", Source = "test", TimestampUtc = "2026-03-20T14:30:00Z", Sha256 = "b" }));

        var compiler = new EvidenceCompiler();

        // Both should work — second call uses cached index
        var result1 = compiler.CompileEvidence(MakeInput("V-11111", null, "pass"), _tempDir);
        var result2 = compiler.CompileEvidence(MakeInput("V-22222", null, "pass"), _tempDir);

        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
    }

    private static EvidenceCompilationInput MakeInput(
        string? vulnId, string? ruleId, string? status,
        string? tool = null, DateTimeOffset? verifiedAt = null)
    {
        return new EvidenceCompilationInput(vulnId, ruleId, status, tool, verifiedAt, null, null);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~EvidenceCompilerTests" -v minimal`
Expected: FAIL — `EvidenceCompiler` not found

- [ ] **Step 3: Write the implementation**

```csharp
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;

namespace STIGForge.Evidence;

/// <summary>
/// Compiles raw evidence artifacts from disk + verify/apply context into
/// auditor-ready FINDING_DETAILS and COMMENTS text for CKL export.
/// Uses EvidenceIndexService internally, caching the index per bundle root.
/// </summary>
public sealed class EvidenceCompiler : IEvidenceCompiler
{
    private const int MaxArtifactContentLength = 4000;
    private readonly ILogger<EvidenceCompiler>? _logger;
    private readonly ConcurrentDictionary<string, EvidenceIndex?> _indexCache = new(StringComparer.OrdinalIgnoreCase);

    public EvidenceCompiler(ILogger<EvidenceCompiler>? logger = null)
    {
        _logger = logger;
    }

    public CompiledEvidence? CompileEvidence(
        EvidenceCompilationInput input,
        string bundleRoot)
    {
        var evidenceIndex = _indexCache.GetOrAdd(bundleRoot, root =>
        {
            try
            {
                var service = new EvidenceIndexService(root);
                return service.BuildIndexAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to build evidence index for {BundleRoot}", root);
                return null;
            }
        });

        if (evidenceIndex == null || evidenceIndex.Entries.Count == 0)
            return null;

        // Need at least one identifier to look up evidence
        var controlKeys = BuildControlKeys(input.VulnId, input.RuleId);
        if (controlKeys.Count == 0)
            return null;

        // Find evidence entries for this control (probe both VulnId and RuleId keys)
        var entries = FindEvidenceEntries(evidenceIndex, controlKeys);
        if (entries.Count == 0)
            return null;

        try
        {
            var findingDetails = BuildFindingDetails(input, entries, bundleRoot);
            var comments = BuildComments(input, entries);
            return new CompiledEvidence(findingDetails, comments);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to compile evidence for {VulnId}/{RuleId}", input.VulnId, input.RuleId);
            return null;
        }
    }

    private static List<string> BuildControlKeys(string? vulnId, string? ruleId)
    {
        var keys = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(vulnId))
            keys.Add(vulnId!.Trim());
        if (!string.IsNullOrWhiteSpace(ruleId))
            keys.Add(ruleId!.Trim());
        return keys;
    }

    private static List<EvidenceIndexEntry> FindEvidenceEntries(EvidenceIndex index, List<string> controlKeys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<EvidenceIndexEntry>();

        foreach (var key in controlKeys)
        {
            var matches = EvidenceIndexService.GetEvidenceForControl(index, key);
            foreach (var entry in matches)
            {
                if (seen.Add(entry.EvidenceId))
                    entries.Add(entry);
            }
        }

        return entries;
    }

    private string BuildFindingDetails(
        EvidenceCompilationInput input,
        List<EvidenceIndexEntry> entries,
        string bundleRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== STIGForge Evidence Report ===");
        sb.AppendFormat("Control: {0}", input.VulnId ?? input.RuleId ?? "unknown");
        if (!string.IsNullOrWhiteSpace(input.RuleId) && !string.IsNullOrWhiteSpace(input.VulnId))
            sb.AppendFormat(" ({0})", input.RuleId);
        sb.AppendLine();
        sb.AppendFormat("Compiled: {0:o}", DateTimeOffset.UtcNow);
        sb.AppendLine();

        // Raw evidence section
        sb.AppendLine();
        sb.AppendLine("--- Raw Evidence ---");
        foreach (var entry in entries)
        {
            sb.AppendFormat("[{0}] Collected: {1}", entry.Type, entry.TimestampUtc);
            if (!string.IsNullOrWhiteSpace(entry.Source))
                sb.AppendFormat(" (Source: {0})", entry.Source);
            sb.AppendLine();

            // Read artifact content
            var content = ReadArtifactContent(entry, bundleRoot);
            if (!string.IsNullOrWhiteSpace(content))
            {
                sb.AppendLine(content);
            }

            sb.AppendLine();
        }

        // Apply history section (from step evidence in the index)
        var stepEntries = entries.Where(e => !string.IsNullOrWhiteSpace(e.StepName)).ToList();
        if (stepEntries.Count > 0)
        {
            sb.AppendLine("--- Apply History ---");
            foreach (var step in stepEntries.DistinctBy(e => e.StepName))
            {
                sb.AppendFormat("Step: {0}", step.StepName);
                sb.AppendLine();
                sb.AppendFormat("Applied: {0}", step.TimestampUtc);
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        // Verification section
        if (!string.IsNullOrWhiteSpace(input.Tool))
        {
            sb.AppendLine("--- Verification ---");
            sb.AppendFormat("Tool: {0}", input.Tool);
            sb.AppendLine();
            if (input.VerifiedAt.HasValue)
            {
                sb.AppendFormat("Scanned: {0:o}", input.VerifiedAt.Value);
                sb.AppendLine();
            }
            sb.AppendFormat("Result: {0}", input.Status ?? "unknown");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private string? ReadArtifactContent(EvidenceIndexEntry entry, string bundleRoot)
    {
        if (string.IsNullOrWhiteSpace(entry.RelativePath))
            return null;

        var fullPath = Path.Combine(bundleRoot, "Evidence", entry.RelativePath);
        if (!File.Exists(fullPath))
            return null;

        try
        {
            var content = File.ReadAllText(fullPath);
            if (content.Length > MaxArtifactContentLength)
            {
                content = content[..MaxArtifactContentLength] + "\n[truncated at " + MaxArtifactContentLength + " chars]";
            }
            return content;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read evidence artifact at {Path}", fullPath);
            return "[Error reading artifact: " + ex.Message + "]";
        }
    }

    private static string BuildComments(
        EvidenceCompilationInput input,
        List<EvidenceIndexEntry> entries)
    {
        // Extract the first meaningful evidence line as the "key evidence point"
        string? keyEvidence = null;
        var firstEntry = entries.FirstOrDefault();
        if (firstEntry != null)
        {
            keyEvidence = firstEntry.Type + " evidence from " + (firstEntry.Source ?? "unknown source");
        }

        var artifactFileNames = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.RelativePath))
            .Select(e => Path.GetFileName(e.RelativePath))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();

        return CommentTemplateEngine.Generate(
            input.Status,
            keyEvidence,
            input.Tool,
            input.VerifiedAt,
            artifactFileNames);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~EvidenceCompilerTests" -v minimal`
Expected: PASS (all 5 tests)

- [ ] **Step 5: Commit**

```bash
git add src/STIGForge.Evidence/EvidenceCompiler.cs tests/STIGForge.UnitTests/Evidence/EvidenceCompilerTests.cs
git commit -m "feat(evidence): add EvidenceCompiler implementation using EvidenceIndexService"
```

---

### Task 4: CklExporter Integration

**Files:**
- Modify: `src/STIGForge.Export/CklExporter.cs:20` (ExportCkl signature)
- Modify: `src/STIGForge.Export/CklExporter.cs:57-61` (enrichment before write)

- [ ] **Step 1: Add optional IEvidenceCompiler parameter to ExportCkl**

In `CklExporter.cs`, change the `ExportCkl` method signature at line 20:

```csharp
// BEFORE:
public static CklExportResult ExportCkl(CklExportRequest request)

// AFTER:
public static CklExportResult ExportCkl(CklExportRequest request, IEvidenceCompiler? evidenceCompiler = null)
```

Add `using STIGForge.Core.Abstractions;` at top. Check that STIGForge.Export.csproj references STIGForge.Core (it should — it already uses `STIGForge.Core.JsonOptions`).

- [ ] **Step 2: Add evidence enrichment logic before BuildCklDocument**

After `LoadResults(bundleRoots)` at line 35 and before `WriteChecklistFile` at line 57, add enrichment:

```csharp
// After line 35: var resultSets = LoadResults(bundleRoots);
// Add evidence enrichment
if (evidenceCompiler != null)
{
    foreach (var resultSet in resultSets)
    {
        EnrichResultsWithEvidence(resultSet, evidenceCompiler);
    }
}
```

Add the new method:

```csharp
private static void EnrichResultsWithEvidence(
    BundleChecklistResultSet resultSet,
    IEvidenceCompiler compiler)
{
    foreach (var control in resultSet.Results)
    {
        try
        {
            var input = new EvidenceCompilationInput(
                control.VulnId,
                control.RuleId,
                control.Status,
                control.Tool,
                control.VerifiedAt,
                control.FindingDetails,
                control.Comments);

            var compiled = compiler.CompileEvidence(input, resultSet.BundleRoot);
            if (compiled == null)
                continue;

            // Append or fill FINDING_DETAILS
            if (!string.IsNullOrWhiteSpace(compiled.FindingDetails))
            {
                if (string.IsNullOrWhiteSpace(control.FindingDetails))
                {
                    control.FindingDetails = compiled.FindingDetails;
                }
                else if (!CommentTemplateEngine.ContainsSentinel(control.FindingDetails))
                {
                    control.FindingDetails = control.FindingDetails
                        + CommentTemplateEngine.Separator
                        + compiled.FindingDetails;
                }
                // else: sentinel already present (re-export) — skip to avoid duplication
            }

            // Append or fill COMMENTS
            if (!string.IsNullOrWhiteSpace(compiled.Comments))
            {
                if (string.IsNullOrWhiteSpace(control.Comments))
                {
                    control.Comments = compiled.Comments;
                }
                else if (!CommentTemplateEngine.ContainsSentinel(control.Comments))
                {
                    control.Comments = control.Comments
                        + CommentTemplateEngine.Separator
                        + compiled.Comments;
                }
            }
        }
        catch (Exception)
        {
            // Per-control failure must not abort the export.
            // Control keeps its original FindingDetails/Comments.
        }
    }
}
```

Add required using:
```csharp
using STIGForge.Core.Abstractions;
```

**Note:** CklExporter does NOT need `using STIGForge.Evidence` — the compiler handles evidence internals. Export only depends on Core (for the interface). No new project references.

**Note:** `resultSet.Results` is `IReadOnlyList<ControlResult>` but `ControlResult` has mutable setters for `FindingDetails` and `Comments`. The in-place mutation works because we are modifying properties on existing objects in the list, not replacing list elements.

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build src/STIGForge.Export/STIGForge.Export.csproj`
Expected: Build succeeded

- [ ] **Step 4: Run existing CklExporter tests to verify no regression**

Run: `dotnet test tests/STIGForge.UnitTests --filter "FullyQualifiedName~CklExporter" -v minimal`
Expected: All existing tests PASS (backward compatible — compiler defaults to null)

- [ ] **Step 5: Commit**

```bash
git add src/STIGForge.Export/CklExporter.cs
git commit -m "feat(export): integrate IEvidenceCompiler into CklExporter with append/idempotency"
```

---

### Task 5: CLI Integration + DI Registration

**Files:**
- Modify: `src/STIGForge.Cli/Commands/ExportCommands.cs:96`
- Modify: `src/STIGForge.Cli/CliHostFactory.cs` (add DI registration)

- [ ] **Step 1: Register IEvidenceCompiler in DI**

In `CliHostFactory.cs`, after line 107 (`services.AddSingleton<EvidenceCollector>();`), add:

```csharp
services.AddSingleton<IEvidenceCompiler>(sp =>
    new EvidenceCompiler(sp.GetRequiredService<ILoggerFactory>().CreateLogger<EvidenceCompiler>()));
```

Add `using STIGForge.Core.Abstractions;` if not already present.

- [ ] **Step 2: Pass IEvidenceCompiler to CklExporter.ExportCkl in ExportCommands**

In `ExportCommands.cs`, modify the `RegisterExportCkl` method. At line 96, change:

```csharp
// BEFORE (line 96):
var result = CklExporter.ExportCkl(new CklExportRequest

// AFTER:
var compiler = host.Services.GetService<IEvidenceCompiler>();
var result = CklExporter.ExportCkl(new CklExportRequest
```

And at the closing of the request object (after line 107), add the compiler parameter:

```csharp
// BEFORE:
});

// AFTER:
}, compiler);
```

Add `using STIGForge.Core.Abstractions;` to ExportCommands.cs.

Also add a log line after the result to show evidence enrichment:
```csharp
if (compiler != null)
    Console.WriteLine("  Evidence enrichment: enabled");
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build src/STIGForge.Cli/STIGForge.Cli.csproj`
Expected: Build succeeded

- [ ] **Step 4: Run full test suite to verify no regression**

Run: `dotnet test --verbosity minimal`
Expected: All existing tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/STIGForge.Cli/CliHostFactory.cs src/STIGForge.Cli/Commands/ExportCommands.cs
git commit -m "feat(cli): wire IEvidenceCompiler into DI and export-ckl command"
```

---

### Task 6: Integration Test

**Files:**
- Create: `tests/STIGForge.IntegrationTests/Export/CklExporterEvidenceIntegrationTests.cs` (or add to existing `CklExporterIntegrationTests.cs`)

- [ ] **Step 1: Write integration test**

Check if `tests/STIGForge.IntegrationTests/Export/CklExporterIntegrationTests.cs` exists. If yes, add tests there. If no, create a new file.

```csharp
[Fact]
public void ExportCkl_WithEvidenceCompiler_PopulatesFindingDetails()
{
    // Arrange: create temp bundle with verify results + evidence artifacts
    var bundleRoot = CreateTempBundleWithEvidence();

    var compiler = new EvidenceCompiler();
    var request = new CklExportRequest
    {
        BundleRoot = bundleRoot,
        OutputDirectory = Path.Combine(bundleRoot, "Export")
    };

    // Act
    var result = CklExporter.ExportCkl(request, compiler);

    // Assert
    result.ControlCount.Should().BeGreaterThan(0);
    var xml = XDocument.Load(result.OutputPath);
    var vulns = xml.Descendants("VULN").ToList();
    var enrichedVuln = vulns.FirstOrDefault(v =>
        v.Element("FINDING_DETAILS")?.Value?.Contains("STIGForge Evidence Report") == true);
    enrichedVuln.Should().NotBeNull("at least one VULN should have enriched FINDING_DETAILS");
}

[Fact]
public void ExportCkl_WithNullCompiler_BackwardCompatible()
{
    var bundleRoot = CreateTempBundleWithEvidence();
    var request = new CklExportRequest { BundleRoot = bundleRoot };

    var result = CklExporter.ExportCkl(request); // no compiler

    result.ControlCount.Should().BeGreaterThan(0);
}
```

Helper method `CreateTempBundleWithEvidence()` should:
1. Create a temp directory
2. Create `Verify/consolidated-results.json` with a sample ControlResult
3. Create `Evidence/by_control/V-12345/registry_export.txt` with sample content
4. Create `Evidence/by_control/V-12345/registry_export.json` with sample metadata

- [ ] **Step 2: Run integration tests**

Run: `dotnet test tests/STIGForge.IntegrationTests --filter "FullyQualifiedName~CklExporterEvidence" -v minimal`
Expected: PASS

- [ ] **Step 3: Run full test suite**

Run: `dotnet test --verbosity minimal`
Expected: All tests PASS

- [ ] **Step 4: Commit**

```bash
git add tests/STIGForge.IntegrationTests/Export/
git commit -m "test(export): add integration tests for evidence-enriched CKL export"
```

---

## Post-Implementation Checklist

- [ ] All new code has unit tests
- [ ] `CklExporter.ExportCkl(request)` works unchanged (backward compatible)
- [ ] `CklExporter.ExportCkl(request, compiler)` enriches FINDING_DETAILS and COMMENTS
- [ ] Append behavior: existing content preserved with separator
- [ ] Idempotency: re-export does not double evidence blocks
- [ ] Per-control try-catch: single bad control does not abort export
- [ ] Full test suite passes with no regressions

# STIG Forge v1 Execution Plan Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship a buildable, testable v1 that supports quarterly DISA imports, fully unattended auto-apply, manual answer capture, release diffs, scan ingestion (SCAP/Evaluate-STIG/NIWC/PowerSTIG), and eMASS exports.

**Architecture:** WPF MVVM app + CLI orchestrate a local SQLite-backed workflow. Content packs and evidence live on disk; JSON/CSV outputs are deterministic. A diff service compares releases and feeds the UI. Manual answers are stored as JSON and converted to/from Evaluate-STIG AnswerFile XML.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, SQLite (Dapper), PowerShell/DSC, xUnit.

---

### Task 1: Add control fingerprinting and diff models

**Files:**
- Create: `src/STIGForge.Core/Models/ControlDiff.cs`
- Create: `src/STIGForge.Core/Services/ControlFingerprint.cs`
- Test: `tests/STIGForge.UnitTests/Core/ControlFingerprintTests.cs`

**Step 1: Write the failing test**

```csharp
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using Xunit;

namespace STIGForge.UnitTests.Core;

public sealed class ControlFingerprintTests
{
  [Fact]
  public void Compute_SameContent_SameHash()
  {
    var a = new ControlRecord
    {
      ExternalIds = new ExternalIds { RuleId = "SV-1", VulnId = "V-1" },
      Title = "Title",
      Severity = "high",
      Discussion = "Discussion",
      CheckText = "Check",
      FixText = "Fix",
      IsManual = true
    };
    var b = new ControlRecord
    {
      ExternalIds = new ExternalIds { RuleId = "SV-1", VulnId = "V-1" },
      Title = "Title",
      Severity = "high",
      Discussion = "Discussion",
      CheckText = "Check",
      FixText = "Fix",
      IsManual = true
    };

    var ha = ControlFingerprint.Compute(a);
    var hb = ControlFingerprint.Compute(b);

    Assert.Equal(ha, hb);
  }

  [Fact]
  public void Compute_ChangedContent_DifferentHash()
  {
    var a = new ControlRecord { Title = "Title", CheckText = "A" };
    var b = new ControlRecord { Title = "Title", CheckText = "B" };

    Assert.NotEqual(ControlFingerprint.Compute(a), ControlFingerprint.Compute(b));
  }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~ControlFingerprintTests`
Expected: FAIL with missing `ControlFingerprint`.

**Step 3: Write minimal implementation**

`src/STIGForge.Core/Services/ControlFingerprint.cs`

```csharp
using System.Security.Cryptography;
using System.Text;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public static class ControlFingerprint
{
  public static string Compute(ControlRecord control)
  {
    var text = string.Join("|", new[]
    {
      control.ExternalIds.RuleId ?? string.Empty,
      control.ExternalIds.VulnId ?? string.Empty,
      control.Title ?? string.Empty,
      control.Severity ?? string.Empty,
      control.Discussion ?? string.Empty,
      control.CheckText ?? string.Empty,
      control.FixText ?? string.Empty,
      control.IsManual ? "manual" : "auto"
    });

    using var sha = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(text);
    var hash = sha.ComputeHash(bytes);
    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
  }
}
```

`src/STIGForge.Core/Models/ControlDiff.cs`

```csharp
namespace STIGForge.Core.Models;

public enum DiffKind { Added, Removed, Changed, Unchanged }

public sealed class ControlDiff
{
  public string Key { get; set; } = string.Empty;
  public string? RuleId { get; set; }
  public string? VulnId { get; set; }
  public string Title { get; set; } = string.Empty;
  public DiffKind Kind { get; set; }
  public bool IsManual { get; set; }
  public bool ManualChanged { get; set; }
  public IReadOnlyList<string> ChangedFields { get; set; } = Array.Empty<string>();
  public string? FromHash { get; set; }
  public string? ToHash { get; set; }
}

public sealed class ReleaseDiff
{
  public string FromPackId { get; set; } = string.Empty;
  public string ToPackId { get; set; } = string.Empty;
  public IReadOnlyList<ControlDiff> Items { get; set; } = Array.Empty<ControlDiff>();
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~ControlFingerprintTests`
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.Core/Models/ControlDiff.cs src/STIGForge.Core/Services/ControlFingerprint.cs tests/STIGForge.UnitTests/Core/ControlFingerprintTests.cs
git commit -m "feat(core): add control fingerprinting"
```

### Task 2: Implement release diff service with manual-change detection

**Files:**
- Create: `src/STIGForge.Core/Services/ReleaseDiffService.cs`
- Test: `tests/STIGForge.UnitTests/Core/ReleaseDiffServiceTests.cs`

**Step 1: Write the failing test**

```csharp
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using Xunit;

namespace STIGForge.UnitTests.Core;

public sealed class ReleaseDiffServiceTests
{
  [Fact]
  public void Diff_FindsAddedRemovedChangedAndManual()
  {
    var from = new List<ControlRecord>
    {
      new() { ExternalIds = new ExternalIds { RuleId = "SV-1" }, Title = "Old", CheckText = "A", IsManual = true },
      new() { ExternalIds = new ExternalIds { RuleId = "SV-2" }, Title = "Keep", CheckText = "B", IsManual = false }
    };
    var to = new List<ControlRecord>
    {
      new() { ExternalIds = new ExternalIds { RuleId = "SV-1" }, Title = "Old", CheckText = "A2", IsManual = true },
      new() { ExternalIds = new ExternalIds { RuleId = "SV-3" }, Title = "New", CheckText = "C", IsManual = false }
    };

    var diff = new ReleaseDiffService().Diff("packA", "packB", from, to);

    Assert.Contains(diff.Items, i => i.RuleId == "SV-3" && i.Kind == DiffKind.Added);
    Assert.Contains(diff.Items, i => i.RuleId == "SV-2" && i.Kind == DiffKind.Removed);
    Assert.Contains(diff.Items, i => i.RuleId == "SV-1" && i.Kind == DiffKind.Changed && i.ManualChanged);
  }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~ReleaseDiffServiceTests`
Expected: FAIL with missing `ReleaseDiffService`.

**Step 3: Write minimal implementation**

```csharp
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class ReleaseDiffService
{
  public ReleaseDiff Diff(string fromPackId, string toPackId, IReadOnlyList<ControlRecord> fromControls, IReadOnlyList<ControlRecord> toControls)
  {
    var fromMap = IndexByKey(fromControls);
    var toMap = IndexByKey(toControls);
    var keys = new HashSet<string>(fromMap.Keys, StringComparer.OrdinalIgnoreCase);
    keys.UnionWith(toMap.Keys);

    var items = new List<ControlDiff>(keys.Count);
    foreach (var key in keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
    {
      var hasFrom = fromMap.TryGetValue(key, out var from);
      var hasTo = toMap.TryGetValue(key, out var to);

      if (hasFrom && !hasTo)
      {
        items.Add(ToDiff(key, from!, null, DiffKind.Removed));
        continue;
      }
      if (!hasFrom && hasTo)
      {
        items.Add(ToDiff(key, null, to!, DiffKind.Added));
        continue;
      }

      var fromHash = ControlFingerprint.Compute(from!);
      var toHash = ControlFingerprint.Compute(to!);
      var kind = fromHash == toHash ? DiffKind.Unchanged : DiffKind.Changed;
      items.Add(ToDiff(key, from, to, kind, fromHash, toHash));
    }

    return new ReleaseDiff { FromPackId = fromPackId, ToPackId = toPackId, Items = items };
  }

  private static Dictionary<string, ControlRecord> IndexByKey(IEnumerable<ControlRecord> controls)
  {
    var map = new Dictionary<string, ControlRecord>(StringComparer.OrdinalIgnoreCase);
    foreach (var c in controls)
    {
      var key = GetKey(c);
      if (!map.ContainsKey(key)) map[key] = c;
    }
    return map;
  }

  private static string GetKey(ControlRecord c)
  {
    if (!string.IsNullOrWhiteSpace(c.ExternalIds.RuleId)) return "RULE:" + c.ExternalIds.RuleId!.Trim();
    if (!string.IsNullOrWhiteSpace(c.ExternalIds.VulnId)) return "VULN:" + c.ExternalIds.VulnId!.Trim();
    return "TITLE:" + (c.Title ?? string.Empty).Trim();
  }

  private static ControlDiff ToDiff(string key, ControlRecord? from, ControlRecord? to, DiffKind kind, string? fromHash = null, string? toHash = null)
  {
    var source = to ?? from!;
    var changed = new List<string>();
    if (from != null && to != null && kind == DiffKind.Changed)
    {
      if (!string.Equals(from.Title, to.Title, StringComparison.Ordinal)) changed.Add("Title");
      if (!string.Equals(from.Severity, to.Severity, StringComparison.Ordinal)) changed.Add("Severity");
      if (!string.Equals(from.Discussion, to.Discussion, StringComparison.Ordinal)) changed.Add("Discussion");
      if (!string.Equals(from.CheckText, to.CheckText, StringComparison.Ordinal)) changed.Add("CheckText");
      if (!string.Equals(from.FixText, to.FixText, StringComparison.Ordinal)) changed.Add("FixText");
      if (from.IsManual != to.IsManual) changed.Add("IsManual");
    }

    return new ControlDiff
    {
      Key = key,
      RuleId = source.ExternalIds.RuleId,
      VulnId = source.ExternalIds.VulnId,
      Title = source.Title,
      Kind = kind,
      IsManual = source.IsManual,
      ManualChanged = changed.Contains("IsManual") || source.IsManual,
      ChangedFields = changed,
      FromHash = fromHash,
      ToHash = toHash
    };
  }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~ReleaseDiffServiceTests`
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.Core/Services/ReleaseDiffService.cs tests/STIGForge.UnitTests/Core/ReleaseDiffServiceTests.cs
git commit -m "feat(core): add release diff service"
```

### Task 3: Add diff writer and CLI diff-packs command

**Files:**
- Create: `src/STIGForge.Content/Diff/ReleaseDiffWriter.cs`
- Modify: `src/STIGForge.Cli/Program.cs`
- Test: `tests/STIGForge.UnitTests/Content/ReleaseDiffWriterTests.cs`

**Step 1: Write the failing test**

```csharp
using STIGForge.Content.Diff;
using STIGForge.Core.Models;
using Xunit;

namespace STIGForge.UnitTests.Content;

public sealed class ReleaseDiffWriterTests
{
  [Fact]
  public void WriteCsv_WritesHeaderAndRow()
  {
    var diff = new ReleaseDiff
    {
      FromPackId = "A",
      ToPackId = "B",
      Items = new[]
      {
        new ControlDiff { RuleId = "SV-1", VulnId = "V-1", Title = "Title", Kind = DiffKind.Added, IsManual = true }
      }
    };

    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
    ReleaseDiffWriter.WriteCsv(path, diff);

    var text = File.ReadAllText(path);
    Assert.Contains("RuleId", text);
    Assert.Contains("SV-1", text);
  }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~ReleaseDiffWriterTests`
Expected: FAIL with missing `ReleaseDiffWriter`.

**Step 3: Write minimal implementation**

`src/STIGForge.Content/Diff/ReleaseDiffWriter.cs`

```csharp
using System.Text;
using System.Text.Json;
using STIGForge.Core.Models;

namespace STIGForge.Content.Diff;

public static class ReleaseDiffWriter
{
  public static void WriteJson(string path, ReleaseDiff diff)
  {
    var json = JsonSerializer.Serialize(diff, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json, Encoding.UTF8);
  }

  public static void WriteCsv(string path, ReleaseDiff diff)
  {
    var sb = new StringBuilder(2048);
    sb.AppendLine("RuleId,VulnId,Title,Kind,IsManual,ManualChanged,ChangedFields");
    foreach (var i in diff.Items)
    {
      var fields = string.Join(";", i.ChangedFields);
      sb.AppendLine(string.Join(",",
        Csv(i.RuleId),
        Csv(i.VulnId),
        Csv(i.Title),
        Csv(i.Kind.ToString()),
        Csv(i.IsManual ? "true" : "false"),
        Csv(i.ManualChanged ? "true" : "false"),
        Csv(fields)));
    }
    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private static string Csv(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
      v = "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }
}
```

Update CLI to add a `diff-packs` command that loads controls for two packs, runs `ReleaseDiffService`, and writes JSON/CSV under `Reports/Diff` in the newer pack root.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~ReleaseDiffWriterTests`
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.Content/Diff/ReleaseDiffWriter.cs src/STIGForge.Cli/Program.cs tests/STIGForge.UnitTests/Content/ReleaseDiffWriterTests.cs
git commit -m "feat(diff): add diff writer and CLI command"
```

### Task 4: Add WPF “What changed” tab

**Files:**
- Create: `src/STIGForge.App/Models/DiffItem.cs`
- Modify: `src/STIGForge.App/MainViewModel.cs`
- Modify: `src/STIGForge.App/MainWindow.xaml`

**Step 1: Write a small model for UI binding**

`src/STIGForge.App/Models/DiffItem.cs`

```csharp
namespace STIGForge.App.Models;

public sealed class DiffItem
{
  public string RuleId { get; set; } = string.Empty;
  public string VulnId { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string Kind { get; set; } = string.Empty;
  public bool IsManual { get; set; }
  public bool ManualChanged { get; set; }
  public string ChangedFields { get; set; } = string.Empty;
}
```

**Step 2: Load diff JSON and surface a Diff tab**

Add `ObservableCollection<DiffItem> DiffItems`, `LoadDiffCommand`, and a method that finds the latest diff JSON for `SelectedPack` and parses `ReleaseDiff.Items` into `DiffItem` objects. Default the view to show manual changes first.

**Step 3: Update XAML**

Add a new `TabItem Header="Diff"` with a `ListView` showing RuleId, Title, Kind, ManualChanged. Include a checkbox bound to `ShowManualOnly` to filter the list.

**Step 4: Commit**

```bash
git add src/STIGForge.App/Models/DiffItem.cs src/STIGForge.App/MainViewModel.cs src/STIGForge.App/MainWindow.xaml
git commit -m "feat(app): add release diff UI tab"
```

### Task 5: Add AnswerFile XML import/export

**Files:**
- Create: `src/STIGForge.Verify/AnswerFileXml.cs`
- Test: `tests/STIGForge.UnitTests/Verify/AnswerFileXmlTests.cs`
- Create: `tests/STIGForge.UnitTests/fixtures/answerfile-sample.xml`

**Step 1: Add an XML fixture based on Evaluate-STIG AnswerFile format**

Use the official Evaluate-STIG AnswerFile.xml schema and store a minimal sample in `tests/STIGForge.UnitTests/fixtures/answerfile-sample.xml`.

**Step 2: Write the failing test**

```csharp
using STIGForge.Core.Models;
using STIGForge.Verify;
using Xunit;

namespace STIGForge.UnitTests.Verify;

public sealed class AnswerFileXmlTests
{
  private static string Fixture(string name)
  {
    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
    return Path.Combine(baseDir, "..", "..", "..", "fixtures", name);
  }

  [Fact]
  public void ReadXml_ParsesAnswers()
  {
    var path = Fixture("answerfile-sample.xml");
    var file = AnswerFileXml.Read(path);
    Assert.NotEmpty(file.Answers);
  }

  [Fact]
  public void WriteXml_WritesRoundTrip()
  {
    var file = new AnswerFile
    {
      ProfileId = "p1",
      PackId = "pack1",
      CreatedAt = DateTimeOffset.UtcNow,
      Answers = new List<ManualAnswer>
      {
        new() { RuleId = "SV-1", VulnId = "V-1", Status = "NotApplicable", Reason = "Test", Comment = "C" }
      }
    };

    var outPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
    AnswerFileXml.Write(outPath, file);
    var loaded = AnswerFileXml.Read(outPath);

    Assert.Single(loaded.Answers);
    Assert.Equal("SV-1", loaded.Answers[0].RuleId);
  }
}
```

**Step 3: Implement AnswerFileXml**

Implement a minimal reader/writer using `XDocument` and the Evaluate-STIG AnswerFile schema (map RuleId, VulnId, Status, Reason, Comment). Keep unknown fields preserved where possible.

**Step 4: Run tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~AnswerFileXmlTests`
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.Verify/AnswerFileXml.cs tests/STIGForge.UnitTests/Verify/AnswerFileXmlTests.cs tests/STIGForge.UnitTests/fixtures/answerfile-sample.xml
git commit -m "feat(verify): add AnswerFile XML import/export"
```

### Task 6: Wire AnswerFile XML into CLI and WPF

**Files:**
- Modify: `src/STIGForge.Cli/Program.cs`
- Modify: `src/STIGForge.App/MainViewModel.cs`
- Modify: `src/STIGForge.App/MainWindow.xaml`

**Step 1: CLI commands**

Add:
- `answerfile-import --xml <path> --bundle <bundleRoot>`: reads XML, writes `Manual/answers.json`.
- `answerfile-export --bundle <bundleRoot> --xml <path>`: reads `Manual/answers.json`, writes XML.

**Step 2: WPF buttons**

Add two buttons in the Manual tab: “Import AnswerFile.xml” and “Export AnswerFile.xml”. Wire to new commands in `MainViewModel` that call `AnswerFileXml` and update `ManualControls`.

**Step 3: Commit**

```bash
git add src/STIGForge.Cli/Program.cs src/STIGForge.App/MainViewModel.cs src/STIGForge.App/MainWindow.xaml
git commit -m "feat(app,cli): add AnswerFile XML import/export"
```

### Task 7: Add CKL ingestion for NIWC/SCAP/Evaluate-STIG outputs

**Files:**
- Modify: `src/STIGForge.Cli/Program.cs`
- Modify: `src/STIGForge.App/MainViewModel.cs`
- Modify: `src/STIGForge.App/MainWindow.xaml`
- Test: `tests/STIGForge.UnitTests/Verify/CklParserTests.cs`
- Create: `tests/STIGForge.UnitTests/fixtures/sample.ckl`

**Step 1: Add CKL fixture and test**

Create `sample.ckl` with one VULN node and a simple `CklParserTests` ensuring the parser returns the rule id and status.

**Step 2: CLI verify-ingest**

Add `verify-ingest --output-root <path> --tool <label>` that calls `VerifyReportWriter.BuildFromCkls`, then writes consolidated JSON/CSV into that folder.

**Step 3: WPF ingest button**

Add a “Ingest CKL Folder” button in Verify tab to select a folder, run `BuildFromCkls`, and refresh the overlap view.

**Step 4: Commit**

```bash
git add src/STIGForge.Cli/Program.cs src/STIGForge.App/MainViewModel.cs src/STIGForge.App/MainWindow.xaml tests/STIGForge.UnitTests/Verify/CklParserTests.cs tests/STIGForge.UnitTests/fixtures/sample.ckl
git commit -m "feat(verify): ingest CKL results"
```

### Task 8: Default to fully unattended auto-apply

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.cs`
- Modify: `src/STIGForge.App/MainWindow.xaml`
- Modify: `src/STIGForge.Cli/Program.cs`
- Modify: `README.md`

**Step 1: App default and toggle**

Add a `ProfileAutoApply` boolean in `MainViewModel` with default `true` for new profiles. In `BuildBundleAsync`, pass `ForceAutoApply = ProfileAutoApply` in `BundleBuildRequest`.

**Step 2: CLI default**

Add a new option `--auto-apply` with default `true`. Map to `ForceAutoApply` and keep `--force-auto-apply` as an alias for backward compatibility.

**Step 3: Docs update**

Update `README.md` with the new auto-apply default and how to disable it.

**Step 4: Commit**

```bash
git add src/STIGForge.App/MainViewModel.cs src/STIGForge.App/MainWindow.xaml src/STIGForge.Cli/Program.cs README.md
git commit -m "feat(app,cli): default to unattended auto-apply"
```

---

## Verification

Run all unit tests: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`

Run integration tests (if configured): `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj`

Manual smoke (WPF):
- Import a pack, build a bundle, run apply, ingest a CKL folder, open Manual tab, export AnswerFile.xml, export eMASS.

---

## Execution Handoff

Plan complete and saved to `docs/plans/2026-02-05-stigforge-v1-execution-plan.md`. Two execution options:

1. Subagent-Driven (this session) - I dispatch a fresh subagent per task, review between tasks, fast iteration
2. Parallel Session (separate) - Open new session with executing-plans, batch execution with checkpoints

Which approach?

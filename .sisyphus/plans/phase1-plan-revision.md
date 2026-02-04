# Phase 1 Plan Revision

**Phase:** 01-content-parsing
**Type:** revision
**Priority:** HIGH - Must complete before executing Phase 1 plans

---

## Purpose

Fix critical bugs and gaps in Phase 1 plans before execution. Without these fixes, the executor will encounter compilation errors, API mismatches, and missing test coverage.

---

## Revision Tasks

### Task 1: Fix RESEARCH.md Code Sample Bugs

**File:** `.planning/phases/01-content-parsing/01-RESEARCH.md`

**Fix 1.1 - Line 85: Double closing paren**
```
FIND:    using var reader = XmlReader.Create(xccdfFilePath))
REPLACE: using var reader = XmlReader.Create(xccdfFilePath, settings)
```

**Fix 1.2 - Line 83: Incorrect XmlNamespaceManager constructor**
```
FIND:    var nsManager = new XmlNamespaceManager("http://checklists.nist.gov/xccdf/1.2", "xccdf");
REPLACE: var nametable = new NameTable();
         var nsManager = new XmlNamespaceManager(nametable);
         nsManager.AddNamespace("xccdf", "http://checklists.nist.gov/xccdf/1.2");
```

**Fix 1.3 - Line 161: Missing await on async method**
```
FIND:    return reader.ReadContentAsStringAsync().Trim();
REPLACE: return reader.ReadElementContentAsString().Trim();
```
Note: Use sync method `ReadElementContentAsString()` since we're not in async context.

**Fix 1.4 - Remove redundant GetAttribute extension (lines 141-156)**
XmlReader already has built-in `GetAttribute(string name)`. Remove the extension method from code samples.

**Fix 1.5 - Line 386: Same double paren bug**
```
FIND:    using var reader = XmlReader.Create(xmlPath, settings))
REPLACE: using var reader = XmlReader.Create(xmlPath, settings)
```

---

### Task 2: Fix AdmxParser API in RESEARCH.md and Plan 02

**Problem:** The plans assume `LoadAdmxFile(path)` method exists, but AdmxParser only supports loading from system PolicyDefinitions folder.

**Verified API (from official docs):**
```csharp
// ONLY way to use AdmxParser - loads from C:\Windows\PolicyDefinitions
var instance = AdmxDirectory.GetSystemPolicyDefinitions();
await instance.LoadAsync(true);
var admxCollection = instance.LoadedAdmxFiles;
var models = instance.ParseModels();
```

**Decision: Manual XmlReader Parsing** (User confirmed)

GpoParser will use XmlReader streaming pattern instead of AdmxParser:

```csharp
// GpoParser approach - consistent with XccdfParser
public static class GpoParser
{
    public static IReadOnlyList<ControlRecord> Parse(string admxPath, string packName)
    {
        var records = new List<ControlRecord>();
        
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true
        };
        
        using var reader = XmlReader.Create(admxPath, settings);
        
        string? currentNamespace = null;
        
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "policyDefinitions":
                        // Root element - no namespace attribute typically
                        break;
                    case "policyNamespaces":
                        // Contains target namespace
                        break;
                    case "target":
                        currentNamespace = reader.GetAttribute("namespace");
                        break;
                    case "policy":
                        var record = ParsePolicy(reader, currentNamespace, packName);
                        if (record != null)
                            records.Add(record);
                        break;
                }
            }
        }
        
        return records;
    }
    
    private static ControlRecord? ParsePolicy(XmlReader reader, string? ns, string packName)
    {
        var policyName = reader.GetAttribute("name");
        var displayName = reader.GetAttribute("displayName");
        var key = reader.GetAttribute("key");
        var valueName = reader.GetAttribute("valueName");
        
        if (string.IsNullOrEmpty(policyName))
            return null;
        
        return new ControlRecord
        {
            ControlId = Guid.NewGuid().ToString("n"),
            ExternalIds = new ExternalIds
            {
                RuleId = policyName,
                BenchmarkId = ns ?? "unknown"
            },
            Title = CleanDisplayName(displayName) ?? policyName,
            Severity = "medium",  // ADMX doesn't have severity
            Discussion = $"Registry Key: {key}",
            CheckText = $"Verify registry value '{valueName}' under '{key}'",
            FixText = $"Configure via Group Policy: {policyName}",
            IsManual = false,  // GPO policies are automated
            Applicability = new Applicability
            {
                OsTarget = OsTarget.Win11,
                RoleTags = Array.Empty<RoleTemplate>(),
                ClassificationScope = ScopeTag.Unknown,
                Confidence = Confidence.Medium
            },
            Revision = new RevisionInfo { PackName = packName }
        };
    }
    
    private static string? CleanDisplayName(string? displayName)
    {
        // ADMX displayName often uses $(string.ResourceId) format
        // For now, return as-is; can enhance later to resolve from ADML
        return displayName?.Trim();
    }
}
```

**Update Plan 02 Task 2:**
- Remove AdmxParser NuGet dependency
- Use XmlReader pattern above
- Keep AdmxPolicy.cs model for internal use
- Add ADML parsing later (v2) for localized display names

**Update STIGForge.Content.csproj:**
- Do NOT add AdmxParser package reference
- Keep existing System.Text.Json reference

---

### Task 3: Add TDD Tasks to All Plans

**Plan 01 - Add Test Task:**
```xml
<task type="auto">
  <name>Task 0: Create XccdfParser unit tests</name>
  <files>tests/STIGForge.UnitTests/Content/XccdfParserTests.cs</files>
  <action>
    Create unit tests for XccdfParser BEFORE rewriting the parser:
    
    1. Test: ParseSmallXccdf_ReturnsCorrectControlCount
       - Input: Small test XCCDF file (embedded resource or fixture)
       - Assert: Returns expected number of ControlRecords
    
    2. Test: ParseXccdf_ExtractsControlId
       - Assert: ControlRecord.ExternalIds.RuleId matches expected value
    
    3. Test: ParseXccdf_DetectsManualCheck
       - Input: XCCDF with manual check marker
       - Assert: IsManual = true
    
    4. Test: ParseXccdf_DetectsAutomatedCheck  
       - Input: XCCDF with SCC system
       - Assert: IsManual = false
    
    5. Test: ParseXccdf_HandlesCorruptXml
       - Input: Invalid XML
       - Assert: Throws or returns empty list (not crash)
    
    Run tests to establish baseline (they should pass with current XDocument implementation).
  </action>
  <verify>
    dotnet test tests/STIGForge.UnitTests/ --filter "FullyQualifiedName~XccdfParser" exits 0
    All 5 tests pass
  </verify>
</task>
```

**Plan 02 - Add Test Tasks:**
```xml
<task type="auto">
  <name>Task 0: Create parser unit tests</name>
  <files>
    tests/STIGForge.UnitTests/Content/OvalParserTests.cs
    tests/STIGForge.UnitTests/Content/ScapBundleParserTests.cs
    tests/STIGForge.UnitTests/Content/GpoParserTests.cs
  </files>
  <action>
    Create unit tests for new parsers:
    
    OvalParserTests:
    - ParseOval_ReturnsDefinitions
    - ParseOval_ExtractsDefinitionId
    
    ScapBundleParserTests:
    - ParseBundle_FindsXccdfFiles
    - ParseBundle_CombinesControlRecords
    
    GpoParserTests:
    - ParseAdmx_ReturnsControlRecords
    - ParseAdmx_ExtractsRegistryKey
  </action>
</task>
```

**Plan 03 - Add Integration Test Task:**
```xml
<task type="auto">
  <name>Task 0: Create ContentPackImporter integration tests</name>
  <files>tests/STIGForge.IntegrationTests/Import/ContentPackImporterTests.cs</files>
  <action>
    Create integration tests for format detection:
    
    1. Test: ImportStigZip_DetectsFormatCorrectly
    2. Test: ImportScapZip_DetectsFormatCorrectly  
    3. Test: ImportGpoZip_DetectsFormatCorrectly
    4. Test: ImportZip_SavesControlsToSqlite
  </action>
</task>
```

---

### Task 4: Rewrite Acceptance Criteria with Executable Commands

**Plan 01 Acceptance Criteria (replace existing):**
```yaml
verify:
  compilation:
    command: "dotnet build src/STIGForge.Content/"
    expect: "exit code 0, no errors"
  
  no_xdocument:
    command: "grep -r 'XDocument' src/STIGForge.Content/Import/XccdfParser.cs"
    expect: "empty output (no matches)"
  
  has_xmlreader:
    command: "grep 'XmlReader.Create' src/STIGForge.Content/Import/XccdfParser.cs"
    expect: "at least 1 match"
  
  unit_tests:
    command: "dotnet test tests/STIGForge.UnitTests/ --filter XccdfParser"
    expect: "all tests pass"
```

**Plan 02 Acceptance Criteria:**
```yaml
verify:
  compilation:
    command: "dotnet build src/STIGForge.Content/"
    expect: "exit code 0"
  
  oval_parser_exists:
    command: "test -f src/STIGForge.Content/Import/OvalParser.cs"
    expect: "file exists"
  
  scap_parser_exists:
    command: "test -f src/STIGForge.Content/Import/ScapBundleParser.cs"
    expect: "file exists"
  
  gpo_parser_exists:
    command: "test -f src/STIGForge.Content/Import/GpoParser.cs"
    expect: "file exists"
  
  unit_tests:
    command: "dotnet test tests/STIGForge.UnitTests/ --filter 'OvalParser|ScapBundle|GpoParser'"
    expect: "all tests pass"
```

**Plan 03 Acceptance Criteria:**
```yaml
verify:
  format_detection:
    command: "grep 'DetectPackFormat' src/STIGForge.Content/Import/ContentPackImporter.cs"
    expect: "method exists"
  
  no_empty_catch:
    command: "grep -A2 'catch' src/STIGForge.Content/Import/ContentPackImporter.cs | grep -v '//'"
    expect: "no empty catch blocks"
  
  integration_tests:
    command: "dotnet test tests/STIGForge.IntegrationTests/ --filter ContentPackImporter"
    expect: "all tests pass"
```

---

### Task 5: Update IControlRepository Interface

**File:** `src/STIGForge.Core/Abstractions/Repositories.cs`

Add new method to IControlRepository:
```csharp
public interface IControlRepository
{
    Task SaveControlsAsync(string packId, IReadOnlyList<ControlRecord> controls, CancellationToken ct);
    Task<IReadOnlyList<ControlRecord>> ListControlsAsync(string packId, CancellationToken ct);
    
    // NEW: Schema verification
    Task<bool> VerifySchemaAsync(CancellationToken ct);
}
```

Update Plan 03 Task 3 to reference this interface change.

---

### Task 6: Add Missing IsManual Heuristics to Plan 01

**Update Plan 01 Task 2** to explicitly add these heuristics:

```csharp
private static bool IsManualCheck(string? checkSystem, string? checkContent)
{
    // Heuristic 1: Explicit manual marker in system attribute
    if (!string.IsNullOrEmpty(checkSystem) && 
        checkSystem.IndexOf("manual", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    
    // Heuristic 2: SCC automated check system (NOT manual)
    if (!string.IsNullOrEmpty(checkSystem) && 
        checkSystem.Contains("scap.nist.gov", StringComparison.OrdinalIgnoreCase))
        return false;  // SCC = automated
    
    // Heuristic 3: Keywords in check content
    var text = checkContent ?? string.Empty;
    string[] manualKeywords = { "manually", "manual", "review", "examine", "inspect", "audit" };
    foreach (var keyword in manualKeywords)
    {
        if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
    }
    
    return false;  // Default to automated
}
```

---

## Execution Order

1. **Task 1** - Fix RESEARCH.md bugs (enables correct code generation)
2. **Task 2** - Decide and document GpoParser approach
3. **Task 3** - Add TDD tasks to plans (establishes test baseline)
4. **Task 4** - Rewrite acceptance criteria (enables verification)
5. **Task 5** - Update interface (enables Plan 03 Task 3)
6. **Task 6** - Add IsManual heuristics (improves accuracy)

---

## Success Criteria

- [x] All code samples in RESEARCH.md compile without errors
- [x] AdmxParser API usage is correct or alternative documented (using XmlReader instead)
- [x] Each plan has at least one test task (Task 0 added to all 3 plans)
- [x] All acceptance criteria have executable commands
- [x] IControlRepository has VerifySchemaAsync method
- [x] IsManual heuristics include SCC detection

## Completion Status

**COMPLETED:** February 3, 2026

All revision tasks completed. Phase 1 plans are ready for execution.

---

## After Revision Complete

Once this revision plan is executed:
1. Phase 1 plans will be ready for execution
2. Run `/start-work` to begin Phase 1 execution with fixed plans

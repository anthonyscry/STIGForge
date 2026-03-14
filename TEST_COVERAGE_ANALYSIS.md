# STIGForge Test Coverage Analysis
**Analysis Date:** March 2025  
**Scope:** STIGForge.Content + STIGForge.Apply modules  
**Status:** CRITICAL GAPS IDENTIFIED

---

## Executive Summary

**Overall Coverage:** ~45% (138 existing tests, ~240 needed)

This analysis identified **6 CRITICAL security/functionality gaps** across 12 major source files:

| # | Issue | Severity | Module | Impact |
|---|-------|----------|--------|--------|
| 1 | **ImportZipHandler completely untested** | CRITICAL | Content | Zip bombs, path traversal, extraction bypass |
| 2 | **ServiceRemediationHandler WMI escaping untested** | CRITICAL | Apply | WMI filter injection attacks |
| 3 | **RebootCoordinator detection has placeholder tests** | CRITICAL | Apply | Reboot detection non-functional |
| 4 | **AuditPolicyRemediationHandler validation untested** | HIGH | Apply | Command injection in auditpol arguments |
| 5 | **IdempotencyTracker concurrent access untested** | HIGH | Apply | Race conditions, lost updates |
| 6 | **WdacPolicyService validation severely undertested** | HIGH | Apply | Invalid policy deployment modes |

---

## Detailed Analysis by File

### CRITICAL GAPS

#### 1. ImportZipHandler (src/STIGForge.Content/Import/ImportZipHandler.cs)
- **Status:** 0 tests (COMPLETELY UNTESTED) ⚠️ CRITICAL
- **Public Methods:** 5
  - `ExtractZipSafelyAsync()`
  - `ExpandNestedZipArchivesAsync()`
  - `FindScopedGpoRoots()`
  - `GetRelativePathCompat()`
  - `CombinePathSegments()`
- **Untested Critical Logic:**
  - **CountingStream.BytesWritten tracking** - async path vulnerability
  - **Path traversal defense** - zip entries with `..` not tested
  - **MaxExtractedBytes enforcement** - 512MB limit not verified
  - **MaxArchiveEntryCount** - 4096 entry limit not enforced in tests
  - **Nested archive expansion** - maxPasses limit untested
- **Security Impact:** Zip bombs (DoS), directory traversal attacks, extraction limit bypass
- **Tests Needed:** 20-30 cases
- **Priority:** CRITICAL

#### 2. ServiceRemediationHandler (src/STIGForge.Apply/Remediation/Handlers/ServiceRemediationHandler.cs)
- **Status:** 0 dedicated tests (only via RemediationRunner mocks) ⚠️ CRITICAL
- **Public Methods:** 2
  - `TestAsync()`
  - `ApplyAsync()`
- **Untested Critical Logic:**
  - **EscapeWmiFilterValue()** Line 140 - WMI filter injection risk
    - Code: `value.Replace("\\", "\\\\").Replace("'", "\\'");`
    - Gap: Order matters! What about `\` followed by `'`?
    - Test Case: `"Service\\'Name"` → `"Service\\\\\\'Name"` correctness?
  - **PowerShell escaping consistency** - TestAsync uses different escaping than ApplyAsync
  - **WMI injection vectors:** Service names like `"MyService' OR '1'='1"`
- **Security Impact:** WMI filter bypass, arbitrary service manipulation
- **Tests Needed:** 15-20 cases
- **Priority:** CRITICAL

#### 3. RebootCoordinator (src/STIGForge.Apply/Reboot/RebootCoordinator.cs)
- **Status:** 15 tests but DetectRebootRequired tests are STUBS ⚠️ CRITICAL
- **Public Methods:** 3
  - `DetectRebootRequired()`
  - `ScheduleReboot()`
  - `ResumeAfterReboot()`
- **Untested Critical Logic:**
  - **DetectRebootRequired() - COMPLETELY NON-FUNCTIONAL TESTS**
    - Lines 41-55: Method calls 3 different checks but tests just do `true.Should().BeTrue()`
    - Gap: DSC reboot check (PowerShell integration) never tested
    - Gap: Pending file operations registry check (Line 244) never tested
    - Gap: Windows Update reboot flag detection (Line 275) never tested
  - **RebootCount ordering** (Lines 79-92) - What if crash between marker write and increment?
  - **MaxReboots enforcement** - Limit of 3 reboots
- **Functional Impact:** Reboot detection completely non-functional
- **Tests Needed:** 20+ to rewrite stubs properly
- **Priority:** CRITICAL

---

### HIGH PRIORITY GAPS

#### 4. AuditPolicyRemediationHandler (src/STIGForge.Apply/Remediation/Handlers/AuditPolicyRemediationHandler.cs)
- **Status:** 0 dedicated tests ⚠️ HIGH
- **Public Methods:** 2
  - `TestAsync()`
  - `ApplyAsync()`
- **Untested Critical Logic:**
  - **ValidateAuditSubcategory()** Line 165 - Regex validation
    - Pattern: `@"^[\w\s\-/]+$"`
    - Gaps: Unicode characters, injection with special chars not tested
  - **BuildSetArguments()** - Only hardcoded settings tested via mocks
  - **ParseAuditSetting()** - Output parsing with malformed auditpol output
- **Security Impact:** Command injection in auditpol.exe arguments
- **Tests Needed:** 15-20 cases
- **Priority:** HIGH

#### 5. WdacPolicyService (src/STIGForge.Apply/Security/WdacPolicyService.cs)
- **Status:** 3 tests (only null ProcessRunner + basic flow) ⚠️ HIGH
- **Public Methods:** 3
  - `GetStatusAsync()`
  - `TestAsync()`
  - `ApplyAsync()`
- **Untested Critical Logic:**
  - **ValidateCiOption()** Line 172 - Input validation
    - Code checks digits OR predefined strings
    - Gaps: Edge cases with spaces, empty string, case sensitivity
  - **Option selection** Line 112 - `0` for Full mode vs `3` for Safe mode not verified
  - **Policy path resolution** Lines 104-106 - File existence before invoke
- **Functional Impact:** Invalid WDAC policy deployment, wrong enforcement mode
- **Tests Needed:** 10-15 cases
- **Priority:** HIGH

#### 6. IdempotencyTracker (src/STIGForge.Apply/IdempotencyTracker.cs)
- **Status:** 6 tests (single-threaded only, NO concurrent tests) ⚠️ HIGH
- **Public Methods:** 5
  - `IsCompleted()`
  - `MarkCompleted()`
  - `FingerprintMatches()`
  - `Reset()`
  - `GetCompletedOperations()`
- **Untested Critical Logic:**
  - **Atomic write race condition** Lines 120-126
    - Code: `File.WriteAllText(tempPath, json); File.Move(tempPath, _trackerPath, overwrite: true);`
    - Gap: Race between instances (Process A writes temp, B loads, A moves = B loses data)
  - **Lock scope insufficient** - Per-instance lock doesn't prevent inter-process races
  - **Concurrent dict modification during Save()** - No deep copy before serialization
- **Functional Impact:** Lost updates when multiple ApplyRunner instances execute
- **Tests Needed:** 15-20 cases (multi-threaded tests)
- **Priority:** HIGH

---

### MEDIUM PRIORITY GAPS

| File | Issue | Tests | Gap | Priority |
|------|-------|-------|-----|----------|
| **PolFileParser.cs** | Numeric overflow checks (Line 125) | 8 | Off-by-one, scan cap, entry recovery | HIGH |
| **GpoParser.cs** | ParsePackage integration (mixed artifacts) | 6 | OsScope inference, error accumulation | MEDIUM |
| **RollbackScriptGenerator.cs** | PowerShell escaping verification | 4 | Special character handling, injection tests | MEDIUM |
| **SnapshotService.cs** | LGPO path resolution logic | 7 | Bundle vs PATH precedence, override handling | MEDIUM |
| **XccdfParser.cs** | OS detection edge cases | 14 | CPE case sensitivity, format variations | MEDIUM |
| **ApplyRunner.cs** | Step ordering and workflow | 10 | Post-reboot resume, snapshot integration | MEDIUM |

---

## Coverage Summary Table

| Module | File | Tests | Est. Coverage | Status |
|--------|------|-------|----------------|--------|
| **Content** | PolFileParser.cs | 8 | 40% | Basic paths only |
| **Content** | ImportZipHandler.cs | **0** | **0%** | ❌ UNTESTED |
| **Content** | XccdfParser.cs | 14 | 70% | ✓ Good |
| **Content** | GpoParser.cs | 6 | 50% | Needs integration |
| **Apply** | AuditPolicyRemediationHandler.cs | **0** | **0%** | ❌ UNTESTED |
| **Apply** | ServiceRemediationHandler.cs | **0** | **0%** | ❌ UNTESTED |
| **Apply** | SnapshotService.cs | 7 | 80% | ✓ Mostly good |
| **Apply** | RollbackScriptGenerator.cs | 4 | 60% | Needs escaping |
| **Apply** | WdacPolicyService.cs | 3 | 30% | Severely under |
| **Apply** | IdempotencyTracker.cs | 6 | 50% | No concurrency |
| **Apply** | RebootCoordinator.cs | 15 | 40% | ❌ BROKEN STUBS |
| **Apply** | ApplyRunner.cs | 10 | 50% | Workflow untested |
| **TOTAL** | | **~138** | **~45%** | |

---

## Recommended Action Plan

### Phase 1: IMMEDIATE (Week 1)
**Must complete before production release**

1. **Rewrite RebootCoordinator tests** (Lines 41-55 are stubs)
   - Implement actual DSC reboot detection tests
   - Mock registry checks for pending operations
   - Mock Windows Update reboot flag
   - **Est. 20+ tests**

2. **Create ImportZipHandler test suite** (NEW)
   - CountingStream accuracy with various patterns
   - Path traversal attack vectors
   - MaxExtractedBytes enforcement
   - Archive bomb (large entry count)
   - **Est. 20-30 tests**

3. **Create ServiceRemediationHandler test suite** (NEW)
   - WMI filter escaping correctness
   - Service name injection attempts
   - PowerShell escaping consistency
   - **Est. 15-20 tests**

### Phase 2: HIGH PRIORITY (Week 2)
**Complete within 2 weeks**

4. Create AuditPolicyRemediationHandler test suite (15-20 tests)
5. Enhance IdempotencyTracker with concurrency tests (15-20 tests)
6. Enhance PolFileParser edge case tests (10-15 tests)
7. Enhance WdacPolicyService validation tests (10-15 tests)

### Phase 3: MEDIUM PRIORITY (Weeks 3-4)
**Complete within 4 weeks**

8. Enhance GpoParser integration tests (12-18 tests)
9. Enhance SnapshotService path resolution tests (8-12 tests)
10. Enhance RollbackScriptGenerator escaping tests (8-12 tests)
11. Enhance ApplyRunner workflow tests (15-20 tests)
12. Enhance XccdfParser edge case tests (8-10 tests)

---

## Effort Estimate

| Metric | Value |
|--------|-------|
| Current test cases | ~138 |
| Tests needed | 180-240 |
| Estimated effort | 3-4 weeks |
| Team size | 2 developers |
| Framework | Xunit + FluentAssertions (existing) |
| Test tools | Moq (mocking), temp directories |

---

## Implementation Guidelines

### Testing Strategy
- Use parametrized tests `[Theory]` for edge cases
- Mock external dependencies: `IProcessRunner`, `ILogger`
- Use temporary directories for file-based tests
- Focus security-critical tests on injection vectors
- Document test intentions for security-related cases

### Focus Areas
1. **Security Testing** - Injection, escaping, validation
2. **Boundary Testing** - Numeric limits, off-by-one errors
3. **Concurrency Testing** - Multi-threaded access, race conditions
4. **Integration Testing** - Component interaction, workflow order
5. **Error Handling** - Malformed input, exception recovery

### Test Naming Convention
```csharp
// Format: MethodName_Condition_ExpectedResult
[Fact]
public void ValidateAuditSubcategory_WithInvalidCharacters_ThrowsArgumentException()
{ ... }

[Theory]
[InlineData("System Integrity")]  // valid
[InlineData("System\"; DROP--")]  // injection attempt
[InlineData("System\x00Null")]    // null byte
public void ValidateAuditSubcategory_WithVariousInputs(string input) { ... }
```

---

## Files Requiring Attention

### Must Create Tests
- [ ] ImportZipHandler (currently 0 tests)
- [ ] AuditPolicyRemediationHandler (currently 0 dedicated tests)
- [ ] ServiceRemediationHandler (currently 0 dedicated tests)

### Must Fix Tests
- [ ] RebootCoordinator.DetectRebootRequired (placeholder tests)

### Must Enhance Tests
- [ ] IdempotencyTracker (add concurrency scenarios)
- [ ] PolFileParser (add edge cases)
- [ ] WdacPolicyService (expand validation tests)
- [ ] GpoParser (add integration tests)
- [ ] SnapshotService (add path resolution tests)
- [ ] RollbackScriptGenerator (add escaping tests)
- [ ] ApplyRunner (add workflow tests)
- [ ] XccdfParser (add edge cases)

---

## Security Considerations

The following untested features have **security implications**:

1. **Command Injection** (HIGH)
   - AuditPolicyRemediationHandler.ValidateAuditSubcategory
   - Needs: Fuzzing with special characters

2. **WMI Injection** (CRITICAL)
   - ServiceRemediationHandler.EscapeWmiFilterValue
   - Needs: Test with WMI filter syntax combinations

3. **PowerShell Injection** (MEDIUM)
   - RollbackScriptGenerator.EscapePowerShellSingleQuoted
   - Needs: Test with quotes, backticks, dollar signs

4. **Path Traversal** (CRITICAL)
   - ImportZipHandler path checking
   - Needs: Zip with `../`, `..\\` entries

5. **Zip Bombs** (CRITICAL)
   - ImportZipHandler.CountingStream
   - Needs: Test with compression ratios >100x

---

## Appendices

### A. CRITICAL FEATURES MENTIONED IN BRIEF
✅ = TESTED | ❌ = UNTESTED

- ✅ PolFileParser basic parsing
- ❌ PolFileParser numeric overflow + off-by-one + scan cap
- ❌ ImportZipHandler CountingStream
- ❌ XccdfParser OS lookup table edge cases (partial)
- ✅ GpoParser single-pass design (basic)
- ❌ AuditPolicyRemediationHandler regex validation
- ❌ ServiceRemediationHandler WMI escaping
- ❌ WdacPolicyService CI option validation
- ⚠️ SnapshotService ArgumentList (safe usage, basic verification)
- ❌ RollbackScriptGenerator PowerShell escaping
- ❌ IdempotencyTracker atomic write + lock
- ❌ RebootCoordinator ordering

### B. Repository Structure
```
src/
  STIGForge.Content/Import/
    ✓ PolFileParser.cs (8 tests)
    ❌ ImportZipHandler.cs (0 tests)
    ✓ XccdfParser.cs (14 tests)
    ✓ GpoParser.cs (6 tests)
  
  STIGForge.Apply/
    Remediation/Handlers/
      ❌ AuditPolicyRemediationHandler.cs (0 dedicated)
      ❌ ServiceRemediationHandler.cs (0 dedicated)
    Snapshot/
      ✓ SnapshotService.cs (7 tests)
      ⚠️ RollbackScriptGenerator.cs (4 tests)
    Security/
      ⚠️ WdacPolicyService.cs (3 tests)
    Reboot/
      ⚠️ RebootCoordinator.cs (15 broken stubs)
    IdempotencyTracker.cs (6 tests, no concurrency)
    ApplyRunner.cs (10 tests, limited scope)

tests/STIGForge.UnitTests/
  Content/
    ✓ PolFileParserTests.cs
    ✓ XccdfParserTests.cs
    ✓ GpoParserTests.cs
    ❌ ImportZipHandlerTests.cs (missing)
  
  Apply/
    Remediation/
      ❌ AuditPolicyRemediationHandlerTests.cs (missing)
      ❌ ServiceRemediationHandlerTests.cs (missing)
    Snapshot/
      ✓ SnapshotServiceTests.cs
      ⚠️ RollbackScriptGeneratorTests.cs
    Security/
      ⚠️ WdacPolicyServiceTests.cs
    ⚠️ RebootCoordinatorTests.cs (placeholder tests)
    ⚠️ IdempotencyTrackerTests.cs (single-threaded)
    ✓ ApplyRunnerTests.cs
```

---

**Report Generated:** March 2025  
**Analysis Version:** 1.0  
**Next Review:** Post-implementation of Phase 1 gaps

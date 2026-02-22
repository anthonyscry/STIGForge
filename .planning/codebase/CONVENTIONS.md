# Coding Conventions

**Analysis Date:** 2026-02-21

## Naming Patterns

**Files:**
- Pascal case: `ApplyRunner.cs`, `BaselineDiffService.cs`, `ControlRecord.cs`
- Interfaces prefixed with `I`: `IProcessRunner`, `IControlRepository`, `IAuditTrailService`
- Test files follow pattern: `[Class]Tests.cs` (e.g., `ApplyRunnerTests.cs`, `BaselineDiffServiceTests.cs`)
- Partial views/view models split by feature: `MainViewModel.ApplyVerify.cs`, `MainViewModel.Import.cs`

**Functions/Methods:**
- Pascal case: `RunAsync()`, `ComparePacksAsync()`, `BuildControlMap()`, `GetControlKey()`
- Private methods use camelCase patterns in some cases (rare), but generally PascalCase
- Async operations consistently suffixed with `Async`: `ListControlsAsync()`, `RecordAsync()`, `VerifyIntegrityAsync()`
- Method parameters use camelCase: `baselinePackId`, `cancellationToken`, `bundleRoot`

**Variables:**
- camelCase: `_logger`, `_controls`, `_bundleRoot`, `controls`, `baselineMap`, `changes`
- Private fields prefixed with underscore: `_logger`, `_snapshotService`, `_disposed`
- Loop variables: single letter or short camelCase: `key`, `newControl`, `baselineControl`
- Constants: UPPER_SNAKE_CASE in some cases, but more often PascalCase: `PowerStigStepName = "powerstig_compile"`, `const string ScriptStepName = "apply_script"`

**Types:**
- Classes and structs: Pascal case: `ControlRecord`, `BaselineDiff`, `ApplyRunner`
- Sealed classes preferred: `public sealed class BaselineDiffService`, `public sealed class ControlRecord`
- Interface implementations use concrete names without "Impl" suffix
- Enums: Pascal case: `ControlChangeType`, `FieldChangeImpact`, `HardeningMode`
- Record types: Pascal case when used: properties and backing fields use automatic properties

## Code Style

**Formatting:**
- EditorConfig defines rules: `.editorconfig`
  - Charset: utf-8
  - Line endings: crlf
  - Insert final newline: true
  - Indent: 2 spaces (for C# files)
  - End of line: crlf for all files

**Linting:**
- EditorConfig-based style enforcement: `[*.cs]`
  - `dotnet_style_qualification_for_field = false:suggestion` - No `this.` prefix for fields
  - `dotnet_style_qualification_for_property = false:suggestion` - No `this.` prefix for properties
  - `dotnet_style_qualification_for_method = false:suggestion` - No `this.` prefix for methods
  - `csharp_style_var_for_built_in_types = true:suggestion` - Use `var` for built-in types
  - `csharp_style_var_when_type_is_apparent = true:suggestion` - Use `var` when type is obvious
  - `csharp_style_var_elsewhere = true:suggestion` - Use `var` elsewhere

**Language Features:**
- Latest C# language version: `<LangVersion>latest</LangVersion>`
- Nullable reference types enabled: `<Nullable>enable</Nullable>`
- Implicit usings enabled: `<ImplicitUsings>enable</ImplicitUsings>` (NET8.0)
- Top-level namespace declarations: `namespace STIGForge.Core.Services;` (file-scoped namespaces)
- Target framework: `net8.0` or `net8.0-windows`

## Import Organization

**Order:**
1. System and standard library imports: `using System;`, `using System.Collections.ObjectModel;`, `using System.Diagnostics;`
2. Third-party/library imports: `using FluentAssertions;`, `using Microsoft.Extensions.Logging;`, `using Moq;`
3. Project-specific imports: `using STIGForge.Core.Abstractions;`, `using STIGForge.Core.Models;`, `using STIGForge.Apply;`

**Organization notes:**
- No blank lines between groups; groups are ordered by category
- Imports sorted alphabetically within category
- Example from `BaselineDiffService.cs`: System imports first (`using STIGForge.Core.Abstractions; using STIGForge.Core.Models;`), then namespace declaration

**Path Aliases:**
- No custom path aliases detected; projects use explicit namespaces

## Error Handling

**Patterns:**
- Null checks in constructor via throw expressions: `_logger = logger ?? throw new ArgumentNullException(nameof(logger));`
- Guard clauses at method entry: `if (string.IsNullOrWhiteSpace(request.BundleRoot)) throw new ArgumentException("BundleRoot is required.");`
- Specific exception types used: `ArgumentNullException`, `ArgumentException`, `DirectoryNotFoundException`, `InvalidOperationException`
- Null-coalescing with defaults for optional parameters: `var mode = request.ModeOverride ?? TryReadModeFromManifest(root) ?? HardeningMode.Safe;`
- Try-catch blocks for non-critical operations with logging:
  ```csharp
  try
  {
    resumeContext = await _rebootCoordinator.ResumeAfterReboot(root, ct).ConfigureAwait(false);
  }
  catch (RebootException ex)
  {
    throw new InvalidOperationException("Message with context", ex);
  }
  ```
- Silent exception suppression only in specific contexts: `try { Directory.Delete(_bundleRoot, true); } catch { }` in test teardown

## Logging

**Framework:** `Microsoft.Extensions.Logging`

**Patterns:**
- Logger injected via constructor as `ILogger<T>`: `ILogger<ApplyRunner>`
- LogInformation for operational events: `_logger.LogInformation("Resuming apply after reboot from step {CurrentStepIndex}...")`
- Structured logging with named parameters: `_logger.LogInformation("Message {ParamName}", paramValue)`
- Used in services and runners, not in models or data objects

## Comments

**When to Comment:**
- XML documentation for public types and methods: `/// <summary>` tags
- Complex algorithms explained with inline comments
- Non-obvious intent or business logic documented
- No comments for self-documenting code

**JSDoc/TSDoc:**
- Uses C# XML documentation: `/// <summary>`, `/// <remarks>`, `/// <param>`, `/// <returns>`
- Applied to interfaces, public classes, and public methods
- Example from `BaselineDiffService.cs`:
  ```csharp
  /// <summary>
  /// Compares two STIG packs (baseline vs new release) and identifies changes.
  /// Supports quarterly STIG update workflows.
  /// </summary>
  public sealed class BaselineDiffService
  ```

## Function Design

**Size:**
- Methods range from 20-80 lines for service methods
- Private helper methods kept compact (10-40 lines)
- Complex logic broken into named private methods for clarity
- Example: `BuildControlMap()` and `CompareControls()` are extracted helper methods

**Parameters:**
- Use value objects/classes for multi-parameter operations rather than long parameter lists
- Async methods include `CancellationToken cancellationToken = default` parameter
- Optional parameters use nullable types: `string? comment`, `IAuditTrailService? audit = null`

**Return Values:**
- Task-based async returns: `Task<T>`, `Task`
- Specific data types (not generic objects): `Task<BaselineDiff>`, `Task<IReadOnlyList<AuditEntry>>`
- Nullable returns used: `(string Username, string Password)?` for tuple returns
- IReadOnlyList/IReadOnlyCollection for collection returns to prevent external modification

## Module Design

**Exports:**
- Interface-based public contracts: Implementations are public sealed classes, interfaces define public API
- Service classes typically sealed: `public sealed class BaselineDiffService`
- Models use auto-properties with backing fields
- Examples of good exports: `IControlRepository`, `IAuditTrailService`, `IPathBuilder`

**Barrel Files:**
- Not detected; each file contains single logical entity
- Namespaces map to directory structure: `STIGForge.Core.Services`, `STIGForge.Core.Models`, `STIGForge.Core.Abstractions`

## Property Initialization

**Auto-properties:**
- Prefer auto-properties with initializers: `public string ControlId { get; set; } = string.Empty;`
- Initialize collections inline: `public List<ControlDiff> AddedControls { get; set; } = new();`
- Use init-only properties where appropriate for immutability

**Sealed vs Abstract:**
- Data models heavily use sealed: `public sealed class ControlRecord`
- Service implementations sealed: `public sealed class BaselineDiffService`
- This prevents unintended inheritance chains

## Null Safety

**Approach:**
- Nullable reference types enabled project-wide
- Null checks with `??` operator for defaults
- `?. ` operator for safe navigation in rare cases
- Properties explicitly marked nullable: `public string? Discussion { get; set; }`
- Non-nullable by default: `public string ControlId { get; set; }`

---

*Convention analysis: 2026-02-21*

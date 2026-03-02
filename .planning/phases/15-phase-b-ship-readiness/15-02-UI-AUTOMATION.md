---
feature: ui-automation
type: technical-spec
status: complete
---

# UI Automation Framework (FlaUI)

## Overview

Production-grade UI automation for WPF app testing using FlaUI (native Windows UI Automation). Provides Playwright-like API for desktop application testing.

## Architecture

```
tests/STIGForge.UiDriver/
├── UiAppDriver.cs          # Application lifecycle management
└── UiLocator.cs            # Element discovery and interaction

tests/STIGForge.App.UiTests/
└── AppSmokeTests.cs        # Smoke test suite
```

## UiAppDriver

**Purpose:** Manage WPF app lifecycle for testing

**Capabilities:**
- `Launch(path)`: Start application with retry logic
- `Attach(processName)`: Attach to running instance
- `Close()`: Clean shutdown
- `TakeScreenshot(path)`: Visual regression capture

**Key Features:**
- Process health monitoring
- Automatic retry on launch failure
- Screenshot capture on test failure

## UiLocator

**Purpose:** Discover and interact with UI elements

**Element Selection:**
```csharp
// By automation ID
var button = _locator.FindByAutomationId("ScanButton");

// By name
var tab = _locator.FindByName("Compliance");

// By control type
var grids = _locator.FindAllByType("DataGrid");
```

**Interactions:**
- `Click()`: Mouse click with wait
- `SendKeys(text)`: Keyboard input
- `GetText()`: Read element text
- `WaitForElement(id, timeout)`: Explicit waits

## Test Coverage

| Test | Description |
|------|-------------|
| `HeaderButtons_AreClickable` | Validates all header buttons respond |
| `NavigationTabs_LoadCorrectly` | Each tab renders without errors |
| `ScanWorkflow_CanBeCanceled` | Full scan -> cancel flow |
| `ComplianceTab_ShowsResults` | Results display after scan |
| `SettingsTab_PersistsChanges` | Settings save/load roundtrip |
| `HelpTab_ContentIsAccessible` | Documentation content loads |

## CI Integration

**Workflow:** `vm-smoke-matrix.yml`

```yaml
- name: Run UI Smoke Tests
  run: dotnet test tests/STIGForge.App.UiTests --filter Category=UI
```

**VM Requirements:**
- Windows 10/11 or Server 2019/2022
- Display session (not headless)
- UI Automation framework enabled

## Usage Example

```csharp
[Fact]
public void ScanWorkflow_CompletesSuccessfully()
{
    using var driver = new UiAppDriver();
    driver.Launch("STIGForge.App.exe");
    
    var locator = new UiLocator(driver.MainWindow);
    
    // Navigate to scan tab
    locator.FindByName("Scan").Click();
    
    // Click start
    locator.FindByAutomationId("StartScanButton").Click();
    
    // Wait for completion
    var results = locator.WaitForElement("ScanResultsGrid", TimeSpan.FromSeconds(30));
    Assert.NotNull(results);
    
    driver.TakeScreenshot("scan_complete.png");
}
```

## Visual Regression

Screenshots captured automatically:
- On test start (baseline)
- On test completion (result)
- On test failure (debug)

**Artifacts:** `.artifacts/ui-tests/screenshots/`

## Dependencies

- FlaUI.Core
- FlaUI.UIA3
- xUnit (test framework)

## Maintenance Notes

- Automation IDs must be stable for tests to pass
- Add `AutomationProperties.AutomationId` to new XAML elements
- Run UI tests on VM agents (not containers)

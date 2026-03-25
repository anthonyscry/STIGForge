namespace STIGForge.UiDriver;

/// <summary>
/// A visual discrepancy detected during E2E testing. Logged as a warning,
/// not a test failure. Includes screenshot evidence.
/// </summary>
public sealed record VisualDiscrepancy(
    string Name,
    string Description,
    string ScreenshotPath,
    DateTimeOffset DetectedAt);

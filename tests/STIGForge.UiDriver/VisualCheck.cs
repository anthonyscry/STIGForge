using System.Text.Json;

namespace STIGForge.UiDriver;

/// <summary>
/// Soft-assertion helper for visual E2E checks. Captures screenshot evidence
/// on failure and records issues without throwing, so tests can accumulate
/// multiple discrepancies before deciding whether to fail.
/// </summary>
public sealed class VisualCheck : IDisposable
{
    private readonly UiAppDriver _driver;
    private readonly string _screenshotDir;
    private readonly List<VisualDiscrepancy> _issues = new();
    private bool _disposed;

    public VisualCheck(UiAppDriver driver, string screenshotDir)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (string.IsNullOrWhiteSpace(screenshotDir))
            throw new ArgumentException("Screenshot directory must be provided.", nameof(screenshotDir));

        _driver = driver;
        _screenshotDir = screenshotDir;
    }

    /// <summary>Gets the number of visual discrepancies detected so far.</summary>
    public int WarningCount => _issues.Count;

    /// <summary>Gets the list of all detected visual discrepancies.</summary>
    public IReadOnlyList<VisualDiscrepancy> Issues => _issues;

    /// <summary>
    /// Evaluates <paramref name="condition"/>. When false, captures a screenshot
    /// and records a <see cref="VisualDiscrepancy"/>. Never throws.
    /// </summary>
    public void Check(string name, bool condition, string description)
    {
        if (condition)
            return;

        var screenshotPath = TryCaptureScreenshot(name);
        _issues.Add(new VisualDiscrepancy(
            Name: name,
            Description: description,
            ScreenshotPath: screenshotPath,
            DetectedAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Checks that <paramref name="actualText"/> contains <paramref name="expectedSubstring"/>.
    /// Failure suggests truncation or rendering issues. Captures screenshot evidence on failure.
    /// Never throws.
    /// </summary>
    public void CheckTextNotTruncated(string name, string actualText, string expectedSubstring)
    {
        var condition = actualText.Contains(expectedSubstring, StringComparison.Ordinal);
        var description = condition
            ? string.Empty
            : $"Text truncation detected: expected to find \"{expectedSubstring}\" in \"{actualText}\".";

        Check(name, condition, description);
    }

    /// <summary>
    /// Writes a JSON report of all detected issues to <paramref name="outputPath"/>.
    /// No-ops when there are no issues.
    /// </summary>
    public void WriteReport(string outputPath)
    {
        if (_issues.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must be provided.", nameof(outputPath));

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_issues, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(outputPath, json);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_issues.Count > 0)
        {
            var reportPath = Path.Combine(_screenshotDir, "visual-check-report.json");
            try
            {
                WriteReport(reportPath);
            }
            catch (Exception)
            {
                // Best-effort — do not let report writing mask test failures.
            }
        }
    }

    private string TryCaptureScreenshot(string checkName)
    {
        try
        {
            var sanitized = string.Concat(checkName.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"visual-{sanitized}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.png";
            return _driver.CaptureScreenshot(_screenshotDir, fileName);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

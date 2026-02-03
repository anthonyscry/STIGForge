using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace STIGForge.Apply.Reboot;

/// <summary>
/// Coordinates reboot detection, scheduling, and resume-after-reboot workflow.
/// </summary>
public sealed class RebootCoordinator
{
    private readonly ILogger<RebootCoordinator> _logger;

    public RebootCoordinator(ILogger<RebootCoordinator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Detects whether a system reboot is required.
    /// Checks DSC reboot status, pending file operations, and Windows Update reboot flags.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>True if reboot is required, false otherwise.</returns>
    public async Task<bool> DetectRebootRequired(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking if reboot is required...");

        // Check 1: DSC reboot status
        if (await IsDscRebootRequired(cancellationToken))
        {
            _logger.LogInformation("Reboot required: DSC configuration requests reboot");
            return true;
        }

        // Check 2: Pending file operations
        if (IsPendingFileRenameOperationRequired())
        {
            _logger.LogInformation("Reboot required: Pending file rename operations detected");
            return true;
        }

        // Check 3: Windows Update reboot flag
        if (IsWindowsUpdateRebootRequired())
        {
            _logger.LogInformation("Reboot required: Windows Update requires reboot");
            return true;
        }

        _logger.LogDebug("No reboot required");
        return false;
    }

    /// <summary>
    /// Schedules a system reboot with a resume marker.
    /// </summary>
    /// <param name="context">Reboot context containing state to resume after reboot.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <param name="delaySeconds">Delay before reboot (default: 60 seconds).</param>
    /// <exception cref="RebootException">Thrown when marker creation or reboot scheduling fails.</exception>
    public async Task ScheduleReboot(RebootContext context, CancellationToken cancellationToken, int delaySeconds = 60)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(context.BundleRoot))
            throw new ArgumentException("BundleRoot is required", nameof(context));

        _logger.LogInformation("Scheduling reboot with resume marker...");

        try
        {
            // Write resume marker BEFORE scheduling reboot (critical for recovery)
            await WriteResumeMarker(context, cancellationToken);
            _logger.LogInformation("Resume marker created at {MarkerPath}", GetMarkerPath(context.BundleRoot));

            // Schedule system reboot
            var result = ScheduleSystemReboot(delaySeconds);
            if (!result)
            {
                throw new RebootException("Failed to schedule system reboot");
            }

            _logger.LogInformation("Reboot scheduled in {DelaySeconds} seconds", delaySeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule reboot");
            throw new RebootException("Failed to schedule reboot", ex);
        }
    }

    /// <summary>
    /// Resumes apply workflow after a reboot by reading the resume marker.
    /// </summary>
    /// <param name="bundleRoot">Root directory of the bundle being applied.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Reboot context if marker exists, null otherwise.</returns>
    /// <exception cref="RebootException">Thrown when marker exists but is invalid.</exception>
    public async Task<RebootContext?> ResumeAfterReboot(string bundleRoot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bundleRoot))
            throw new ArgumentException("BundleRoot is required", nameof(bundleRoot));

        var markerPath = GetMarkerPath(bundleRoot);

        if (!File.Exists(markerPath))
        {
            _logger.LogDebug("No resume marker found at {MarkerPath}", markerPath);
            return null;
        }

        _logger.LogInformation("Resume marker found at {MarkerPath}", markerPath);

        try
        {
            // Read and deserialize marker
            var json = File.ReadAllText(markerPath);
            var context = JsonSerializer.Deserialize<RebootContext>(json);

            // Validate marker content
            if (context == null)
                throw new RebootException($"Invalid resume marker: deserialization failed at {markerPath}");

            if (string.IsNullOrWhiteSpace(context.BundleRoot))
                throw new RebootException($"Invalid resume marker: BundleRoot is missing in {markerPath}");

            if (context.CurrentStepIndex < 0)
                throw new RebootException($"Invalid resume marker: CurrentStepIndex is invalid in {markerPath}");

            _logger.LogInformation("Resume context loaded: BundleRoot={BundleRoot}, Step={CurrentStepIndex}, CompletedSteps={CompletedStepsCount}", 
                context.BundleRoot, context.CurrentStepIndex, context.CompletedSteps?.Count ?? 0);

            // Delete marker after successful read (prevent duplicate resumes)
            try
            {
                File.Delete(markerPath);
                _logger.LogDebug("Resume marker deleted after reading");
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to delete resume marker after reading. This is non-critical.");
            }

            return context;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize resume marker");
            throw new RebootException($"Invalid resume marker: JSON parse error at {markerPath}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read resume marker");
            throw new RebootException($"Failed to read resume marker at {markerPath}", ex);
        }
    }

    #region Private Methods

    private async Task<bool> IsDscRebootRequired(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Checking DSC reboot status...");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-DscConfigurationStatus -ErrorAction SilentlyContinue | Select-Object -ExpandProperty RebootRequested\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start PowerShell to check DSC status");
                return false;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(outputTask, errorTask);
            process.WaitForExit();

            var output = outputTask.Result.Trim();
            if (!string.IsNullOrEmpty(output) && output.Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("DSC reports reboot required");
                return true;
            }

            _logger.LogDebug("DSC reports no reboot required");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check DSC reboot status, assuming no reboot required");
            return false; // Assume no reboot if check fails
        }
    }

    private bool IsPendingFileRenameOperationRequired()
    {
        try
        {
            _logger.LogDebug("Checking for pending file rename operations...");

            // Check registry key for pending file operations
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager", false);
            if (key == null)
            {
                _logger.LogDebug("Registry key not accessible");
                return false;
            }

            var value = key.GetValue("PendingFileRenameOperations");
            if (value != null)
            {
                _logger.LogDebug("Pending file rename operations detected in registry");
                return true;
            }

            _logger.LogDebug("No pending file rename operations");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check pending file operations, assuming no reboot required");
            return false; // Assume no reboot if check fails
        }
    }

    private bool IsWindowsUpdateRebootRequired()
    {
        try
        {
            _logger.LogDebug("Checking Windows Update reboot flag...");

            // Check Windows Update reboot required flag
            using var wuKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", false);
            if (wuKey != null)
            {
                _logger.LogDebug("Windows Update RebootRequired key exists");
                return true;
            }

            _logger.LogDebug("No Windows Update reboot required flag");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Windows Update reboot flag, assuming no reboot required");
            return false; // Assume no reboot if check fails
        }
    }

    private async Task WriteResumeMarker(RebootContext context, CancellationToken cancellationToken)
    {
        var markerPath = GetMarkerPath(context.BundleRoot);
        var applyDir = Path.GetDirectoryName(markerPath) 
            ?? throw new InvalidOperationException($"Cannot determine directory from marker path: {markerPath}");

        // Ensure Apply directory exists
        Directory.CreateDirectory(applyDir);

        // Serialize context to JSON
        var json = JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(markerPath, json);

        _logger.LogDebug("Resume marker written to {MarkerPath}", markerPath);
    }

    private static string GetMarkerPath(string bundleRoot)
    {
        return Path.Combine(bundleRoot, "Apply", ".resume_marker.json");
    }

    private bool ScheduleSystemReboot(int delaySeconds)
    {
        try
        {
            var message = "STIGForge apply requires reboot. Resuming after restart...";
            var arguments = $"/r /t {delaySeconds} /c \"{message}\"";

            _logger.LogDebug("Executing: shutdown.exe {Arguments}", arguments);

            var psi = new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogError("Failed to start shutdown.exe");
                return false;
            }

            // Give shutdown.exe time to process
            process.WaitForExit(5000);
            
            _logger.LogDebug("shutdown.exe executed with exit code {ExitCode}", process.ExitCode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute shutdown.exe");
            return false;
        }
    }

    #endregion
}

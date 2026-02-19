using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace STIGForge.App.Models;

public enum VerifyToolState
{
    Pending,
    Running,
    Complete,
    Failed
}

public partial class VerifyToolStatus : ObservableObject
{
    [ObservableProperty] private string toolName = string.Empty;
    [ObservableProperty] private VerifyToolState state = VerifyToolState.Pending;
    [ObservableProperty] private TimeSpan elapsedTime;
    [ObservableProperty] private int findingCount;
    [ObservableProperty] private string stateDisplay = "Pending";

    public DateTime? StartedAt { get; set; }

    partial void OnStateChanged(VerifyToolState value)
    {
        UpdateStateDisplay();
    }

    partial void OnFindingCountChanged(int value)
    {
        UpdateStateDisplay();
    }

    private void UpdateStateDisplay()
    {
        StateDisplay = State switch
        {
            VerifyToolState.Pending => "Pending",
            VerifyToolState.Running => "Running...",
            VerifyToolState.Complete => FindingCount + " findings",
            VerifyToolState.Failed => "Failed",
            _ => "Unknown"
        };
    }
}

public sealed class ErrorPanelInfo
{
    public string ErrorMessage { get; set; } = string.Empty;
    public List<string> RecoverySteps { get; set; } = new();
    public bool CanRetry { get; set; } = true;

    public static ErrorPanelInfo FromException(Exception ex)
    {
        var info = new ErrorPanelInfo
        {
            ErrorMessage = ex.Message,
            CanRetry = true
        };

        switch (ex)
        {
            case FileNotFoundException fnf:
                info.RecoverySteps = new List<string>
                {
                    "Verify scanner tool path in Settings tab",
                    "Ensure the tool is installed at the configured location",
                    "Check that the file exists: " + (fnf.FileName ?? "unknown")
                };
                break;

            case IOException:
                info.RecoverySteps = new List<string>
                {
                    "Check disk space and file permissions",
                    "Verify the output directory exists and is writable",
                    "Close any programs that may have locked scanner output files"
                };
                break;

            case TimeoutException:
                info.RecoverySteps = new List<string>
                {
                    "Increase scan timeout in Settings tab",
                    "Check that the scanner tool is not hung",
                    "Verify the system is responsive and not under heavy load"
                };
                break;

            default:
                info.RecoverySteps = new List<string>
                {
                    "Review the error details above",
                    "Check tool configuration in Settings tab",
                    "Retry the operation"
                };
                break;
        }

        return info;
    }
}

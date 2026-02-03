namespace STIGForge.Apply.Reboot;

/// <summary>
/// Context for reboot state, used to persist and resume after reboot.
/// </summary>
public sealed class RebootContext
{
    /// <summary>
    /// Root directory of the bundle being applied.
    /// </summary>
    public string BundleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Index of the current step being executed.
    /// </summary>
    public int CurrentStepIndex { get; set; }

    /// <summary>
    /// List of step names that have been completed successfully.
    /// </summary>
    public List<string> CompletedSteps { get; set; } = new();

    /// <summary>
    /// Timestamp when the reboot was scheduled.
    /// </summary>
    public DateTimeOffset RebootScheduledAt { get; set; }
}

/// <summary>
/// Exception thrown when reboot coordination fails.
/// </summary>
public sealed class RebootException : Exception
{
    public RebootException(string message) : base(message) { }

    public RebootException(string message, Exception innerException) 
        : base(message, innerException) { }
}

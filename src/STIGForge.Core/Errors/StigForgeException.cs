namespace STIGForge.Core.Errors;

/// <summary>
/// Base class for all STIGForge domain exceptions.
/// Provides a machine-readable ErrorCode for cataloging and programmatic handling.
/// </summary>
public abstract class StigForgeException : Exception
{
    /// <summary>
    /// Gets the machine-readable error code for this exception.
    /// Format: COMPONENT_NUMBER (e.g., "BUILD_001", "IMPORT_002").
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Gets the optional component that raised this exception.
    /// </summary>
    public string? Component { get; }

    /// <summary>
    /// Initializes a new instance of the StigForgeException class.
    /// </summary>
    /// <param name="errorCode">The machine-readable error code.</param>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="innerException">The inner exception that caused this exception.</param>
    protected StigForgeException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
    }

    /// <summary>
    /// Initializes a new instance of the StigForgeException class with a component identifier.
    /// </summary>
    /// <param name="errorCode">The machine-readable error code.</param>
    /// <param name="component">The component that raised this exception.</param>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="innerException">The inner exception that caused this exception.</param>
    protected StigForgeException(string errorCode, string component, string message, Exception? innerException = null)
        : this(errorCode, message, innerException)
    {
        Component = component;
    }

    /// <summary>
    /// Returns a string representation including the error code for logging.
    /// </summary>
    public override string ToString()
    {
        var componentPrefix = Component != null ? $"[{Component}] " : "";
        return $"{componentPrefix}[{ErrorCode}] {base.ToString()}";
    }
}

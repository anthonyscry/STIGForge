namespace STIGForge.Core.Errors;

/// <summary>
/// Exception thrown when bundle building fails.
/// </summary>
public sealed class BundleBuildException : StigForgeException
{
    /// <summary>
    /// Initializes a new instance of the BundleBuildException class.
    /// </summary>
    /// <param name="errorCode">The specific error code (e.g., BUILD_001, BUILD_002).</param>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="innerException">The inner exception that caused this failure.</param>
    public BundleBuildException(string errorCode, string message, Exception? innerException = null)
        : base(errorCode, "Build", message, innerException)
    {
    }

    /// <summary>
    /// Creates a BundleBuildException for general bundle failure.
    /// </summary>
    public static BundleBuildException BundleFailed(string message, Exception? innerException = null)
        => new(ErrorCodes.BUILD_BUNDLE_FAILED, message, innerException);

    /// <summary>
    /// Creates a BundleBuildException for invalid profile.
    /// </summary>
    public static BundleBuildException InvalidProfile(string profileName)
        => new(ErrorCodes.BUILD_INVALID_PROFILE, $"Invalid profile: {profileName}");

    /// <summary>
    /// Creates a BundleBuildException for no STIGs selected.
    /// </summary>
    public static BundleBuildException NoStigsSelected()
        => new(ErrorCodes.BUILD_NO_STIGS_SELECTED, "No STIGs were selected for the bundle");
}

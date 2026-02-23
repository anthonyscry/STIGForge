namespace STIGForge.Infrastructure.Telemetry;

/// <summary>
/// Centralized ActivitySource and span naming constants for STIGForge distributed tracing.
/// Follows OpenTelemetry naming conventions for consistency.
/// </summary>
public static class ActivitySourceNames
{
    /// <summary>
    /// The primary ActivitySource name for mission-level tracing.
    /// </summary>
    public const string Missions = "STIGForge.Missions";

    /// <summary>
    /// Span name for the Build phase of mission execution.
    /// </summary>
    public const string BuildPhase = "build";

    /// <summary>
    /// Span name for the Apply phase of mission execution.
    /// </summary>
    public const string ApplyPhase = "apply";

    /// <summary>
    /// Span name for the Verify phase of mission execution.
    /// </summary>
    public const string VerifyPhase = "verify";

    /// <summary>
    /// Span name for the Prove phase of mission execution.
    /// </summary>
    public const string ProvePhase = "prove";
}

namespace STIGForge.UnitTests;

/// <summary>
/// Standard test category constants for xUnit trait filtering.
/// </summary>
public static class TestCategories
{
    /// <summary>
    /// Fast, isolated tests with no external dependencies.
    /// </summary>
    public const string Unit = "Unit";

    /// <summary>
    /// Slower tests with real infrastructure (files, database, processes).
    /// </summary>
    public const string Integration = "Integration";

    /// <summary>
    /// Tests that take more than 5 seconds to execute.
    /// </summary>
    public const string Slow = "Slow";
}

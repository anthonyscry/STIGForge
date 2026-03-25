using Xunit;

namespace STIGForge.UiDriver;

/// <summary>
/// Marks a test as a UI test that requires an interactive desktop session.
/// The test is skipped unless the <c>UI_TESTS_ENABLED</c> environment variable is set to <c>true</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UIFactAttribute : FactAttribute
{
    private const string EnvVar = "UI_TESTS_ENABLED";

    public UIFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnvVar), "true", StringComparison.OrdinalIgnoreCase))
            Skip = $"UI tests are disabled. Set {EnvVar}=true to run them.";
    }
}

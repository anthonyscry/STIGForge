using Xunit;

namespace STIGForge.Tests.CrossPlatform;

/// <summary>
/// Marks a test that requires Windows. Automatically skipped on non-Windows platforms.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "This test requires Windows.";
    }
}

using FluentAssertions;
using STIGForge.Core.Errors;
using Xunit;

namespace STIGForge.UnitTests.Errors;

public class StigForgeExceptionTests
{
    [Fact]
    public void StigForgeException_Should_Set_ErrorCode()
    {
        // Arrange & Act
        var exception = new TestException("TEST_001", "Test message");

        // Assert
        exception.ErrorCode.Should().Be("TEST_001");
        exception.Message.Should().Be("Test message");
    }

    [Fact]
    public void StigForgeException_Should_Set_Component()
    {
        // Arrange & Act
        var exception = new TestException("TEST_001", "TestComponent", "Test message");

        // Assert
        exception.Component.Should().Be("TestComponent");
    }

    [Fact]
    public void StigForgeException_ToString_Should_Include_ErrorCode()
    {
        // Arrange
        var exception = new TestException("TEST_001", "Test message");

        // Act
        var result = exception.ToString();

        // Assert
        result.Should().Contain("[TEST_001]");
    }

    [Fact]
    public void BundleBuildException_FactoryMethods_Should_Use_CorrectErrorCodes()
    {
        // Arrange & Act
        var bundleFailed = BundleBuildException.BundleFailed("Failed");
        var invalidProfile = BundleBuildException.InvalidProfile("BadProfile");
        var noStigs = BundleBuildException.NoStigsSelected();

        // Assert
        bundleFailed.ErrorCode.Should().Be(ErrorCodes.BUILD_BUNDLE_FAILED);
        invalidProfile.ErrorCode.Should().Be(ErrorCodes.BUILD_INVALID_PROFILE);
        noStigs.ErrorCode.Should().Be(ErrorCodes.BUILD_NO_STIGS_SELECTED);
    }

    [Fact]
    public void ErrorCodes_Should_Follow_Format_Pattern()
    {
        // All error codes should match COMPONENT_NUMBER pattern
        var codes = typeof(ErrorCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        foreach (var code in codes)
        {
            code.Should().MatchRegex("^[A-Z]+_\\d{3}$", $"code '{code}' should match COMPONENT_NUMBER format");
        }
    }

    private sealed class TestException : StigForgeException
    {
        public TestException(string errorCode, string message)
            : base(errorCode, message)
        {
        }

        public TestException(string errorCode, string component, string message)
            : base(errorCode, component, message)
        {
        }
    }
}

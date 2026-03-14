using System.Text.Json;
using FluentAssertions;
using STIGForge.Core;

namespace STIGForge.Tests.CrossPlatform.Core;

public class JsonOptionsTests
{
    [Fact]
    public void Default_IsNotNull()
    {
        JsonOptions.Default.Should().NotBeNull();
    }

    [Fact]
    public void Indented_HasWriteIndentedTrue()
    {
        JsonOptions.Indented.WriteIndented.Should().BeTrue();
    }

    [Fact]
    public void CaseInsensitive_HasPropertyNameCaseInsensitiveTrue()
    {
        JsonOptions.CaseInsensitive.PropertyNameCaseInsensitive.Should().BeTrue();
    }

    [Fact]
    public void IndentedCaseInsensitive_HasBothProperties()
    {
        JsonOptions.IndentedCaseInsensitive.WriteIndented.Should().BeTrue();
        JsonOptions.IndentedCaseInsensitive.PropertyNameCaseInsensitive.Should().BeTrue();
    }

    [Fact]
    public void IndentedCamelCase_HasWriteIndentedAndCamelCase()
    {
        JsonOptions.IndentedCamelCase.WriteIndented.Should().BeTrue();
        JsonOptions.IndentedCamelCase.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }

    [Fact]
    public void Default_SameInstanceOnRepeatedAccess()
    {
        var first = JsonOptions.Default;
        var second = JsonOptions.Default;
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void Indented_SameInstanceOnRepeatedAccess()
    {
        var first = JsonOptions.Indented;
        var second = JsonOptions.Indented;
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void CaseInsensitive_SameInstanceOnRepeatedAccess()
    {
        var first = JsonOptions.CaseInsensitive;
        var second = JsonOptions.CaseInsensitive;
        first.Should().BeSameAs(second);
    }
}

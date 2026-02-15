using System.Reflection;
using FluentAssertions;
using STIGForge.Export;

namespace STIGForge.UnitTests.Cli;

public sealed class ExportCommandsTests
{
  private static readonly MethodInfo ParseChecklistFormat =
    Assembly.Load("STIGForge.Cli")
      .GetType("STIGForge.Cli.Commands.ExportCommands", throwOnError: true)!
      .GetMethod("ParseChecklistFormat", BindingFlags.NonPublic | BindingFlags.Static)!;

  [Theory]
  [InlineData("ckl", CklFileFormat.Ckl)]
  [InlineData("CKL", CklFileFormat.Ckl)]
  [InlineData("cklb", CklFileFormat.Cklb)]
  [InlineData("CKLB", CklFileFormat.Cklb)]
  public void ParseChecklistFormat_ValidInput_ReturnsExpected(string input, CklFileFormat expected)
  {
    var result = (CklFileFormat)ParseChecklistFormat.Invoke(null, new object?[] { input })!;

    result.Should().Be(expected);
  }

  [Fact]
  public void ParseChecklistFormat_InvalidInput_ThrowsArgumentException()
  {
    var act = () => ParseChecklistFormat.Invoke(null, new object?[] { "cklbx" });

    act.Should()
      .Throw<TargetInvocationException>()
      .WithInnerException<ArgumentException>()
      .Which.Message.Should().Contain("Invalid --format");
  }
}

using FluentAssertions;

namespace STIGForge.IntegrationTests;

public class IntegrationPlaceholder
{
  [Fact]
  public void Placeholder_Should_Pass()
  {
    true.Should().BeTrue();
  }
}

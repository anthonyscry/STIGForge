using FluentAssertions;
using STIGForge.Apply.DryRun;

namespace STIGForge.UnitTests.Apply;

public sealed class DscWhatIfParserTests
{
    private const string SampleOutput = """
What if: [MSFT_RegistryResource]ScreenLockTimeout: Set-TargetResource
  ValueData: 600 -> 900
What if: [MSFT_ServiceResource]RemoteRegistry: Set-TargetResource
  State: Running -> Stopped
  StartupType: Automatic -> Disabled
What if: [MSFT_AuditPolicySubcategory]LogonLogoff: Set-TargetResource
""";

    [Fact]
    public void Parse_EmptyOutput_ReturnsEmpty()
    {
        var changes = DscWhatIfParser.Parse(string.Empty);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SingleWhatIfLine_ReturnsOneChange()
    {
        var output = "What if: [MSFT_RegistryResource]ScreenLockTimeout: Set-TargetResource";

        var changes = DscWhatIfParser.Parse(output);

        changes.Should().HaveCount(1);
        changes[0].ResourceType.Should().Be("MSFT_RegistryResource");
        changes[0].ResourcePath.Should().Be("ScreenLockTimeout");
        changes[0].Description.Should().Be("Set-TargetResource");
    }

    [Fact]
    public void Parse_MultipleResources_ParsesAll()
    {
        var changes = DscWhatIfParser.Parse(SampleOutput);

        changes.Should().HaveCount(3);
        changes.Select(c => c.ResourceType).Should().Contain(new[]
        {
            "MSFT_RegistryResource",
            "MSFT_ServiceResource",
            "MSFT_AuditPolicySubcategory"
        });
    }

    [Fact]
    public void Parse_WithPropertyChanges_ExtractsCurrentAndProposed()
    {
        var changes = DscWhatIfParser.Parse(SampleOutput);
        var valueDataChange = changes.First(c => c.Description.Contains("ValueData", StringComparison.Ordinal));

        valueDataChange.CurrentValue.Should().Be("600");
        valueDataChange.ProposedValue.Should().Be("900");
    }

    [Fact]
    public void Parse_NullInput_ReturnsEmpty()
    {
        var changes = DscWhatIfParser.Parse(null);

        changes.Should().BeEmpty();
    }
}

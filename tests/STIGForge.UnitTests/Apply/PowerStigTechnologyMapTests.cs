using FluentAssertions;
using STIGForge.Apply.Dsc;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Apply;

public sealed class PowerStigTechnologyMapTests
{
    [Theory]
    [InlineData(OsTarget.Win11, "WindowsClient", "11")]
    [InlineData(OsTarget.Win10, "WindowsClient", "10")]
    [InlineData(OsTarget.Server2022, "WindowsServer", "2022")]
    [InlineData(OsTarget.Server2019, "WindowsServer", "2019")]
    public void Resolve_KnownOsTarget_ReturnsCorrectCompositeResource(
        OsTarget osTarget, string expectedResource, string expectedVersion)
    {
        var target = PowerStigTechnologyMap.Resolve(osTarget);

        target.Should().NotBeNull();
        target!.CompositeResourceName.Should().Be(expectedResource);
        target.OsVersion.Should().Be(expectedVersion);
    }

    [Fact]
    public void Resolve_UnknownOsTarget_ReturnsNull()
    {
        var target = PowerStigTechnologyMap.Resolve(OsTarget.Unknown);
        target.Should().BeNull();
    }

    [Fact]
    public void Resolve_ServerWithDomainControllerRole_ReturnsDCStigType()
    {
        var target = PowerStigTechnologyMap.Resolve(OsTarget.Server2022, RoleTemplate.DomainController);

        target.Should().NotBeNull();
        target!.StigType.Should().Be("DC");
    }

    [Theory]
    [InlineData(RoleTemplate.Workstation)]
    [InlineData(RoleTemplate.MemberServer)]
    [InlineData(RoleTemplate.LabVm)]
    public void Resolve_ServerWithNonDCRole_ReturnsMSStigType(RoleTemplate role)
    {
        var target = PowerStigTechnologyMap.Resolve(OsTarget.Server2019, role);

        target.Should().NotBeNull();
        target!.StigType.Should().Be("MS");
    }

    [Theory]
    [InlineData(RoleTemplate.Workstation)]
    [InlineData(RoleTemplate.DomainController)]
    public void Resolve_ClientOs_ReturnsNullStigType(RoleTemplate role)
    {
        var target = PowerStigTechnologyMap.Resolve(OsTarget.Win11, role);

        target.Should().NotBeNull();
        target!.StigType.Should().BeNull();
    }

    [Fact]
    public void BuildDscConfigurationScript_ServerTarget_ContainsCorrectCompositeBlock()
    {
        var target = new PowerStigTarget("WindowsServer", "2022", "MS");

        var script = PowerStigTechnologyMap.BuildDscConfigurationScript(target, @"C:\Out\Dsc");

        script.Should().Contain("Import-DscResource -ModuleName PowerSTIG");
        script.Should().Contain("WindowsServer OsStig");
        script.Should().Contain("OsVersion = '2022'");
        script.Should().Contain("StigVersion = 'MS'");
        script.Should().Contain("STIGForgeHarden -OutputPath 'C:\\Out\\Dsc'");
        script.Should().Contain("WindowsFirewall FirewallStig");
    }

    [Fact]
    public void BuildDscConfigurationScript_ClientTarget_OmitsStigVersion()
    {
        var target = new PowerStigTarget("WindowsClient", "11", null);

        var script = PowerStigTechnologyMap.BuildDscConfigurationScript(target, @"C:\Out\Dsc");

        script.Should().Contain("WindowsClient OsStig");
        script.Should().Contain("OsVersion = '11'");
        script.Should().NotContain("StigVersion");
        script.Should().Contain("WindowsFirewall FirewallStig");
    }

    [Fact]
    public void BuildDscConfigurationScript_WithDataFile_IncludesStigDataParam()
    {
        var target = new PowerStigTarget("WindowsServer", "2019", "MS");

        var script = PowerStigTechnologyMap.BuildDscConfigurationScript(
            target, @"C:\Out\Dsc", @"C:\Data\stigdata.psd1");

        script.Should().Contain("StigData = 'C:\\Data\\stigdata.psd1'");
    }

    [Fact]
    public void BuildDscConfigurationScript_PathWithSingleQuotes_EscapesCorrectly()
    {
        var target = new PowerStigTarget("WindowsClient", "10", null);

        var script = PowerStigTechnologyMap.BuildDscConfigurationScript(
            target, @"C:\User's Folder\Dsc");

        script.Should().Contain("STIGForgeHarden -OutputPath 'C:\\User''s Folder\\Dsc'");
    }
}

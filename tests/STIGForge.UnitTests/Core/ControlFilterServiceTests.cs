using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using Xunit;

namespace STIGForge.UnitTests.Core;

public class ControlFilterServiceTests
{
    private readonly ControlFilterService _sut = new();

    private static ControlRecord MakeControl(string ruleId, string severity, string? benchmarkId = null)
    {
        return new ControlRecord
        {
            ControlId = "C-" + ruleId,
            ExternalIds = new ExternalIds
            {
                RuleId = ruleId,
                VulnId = "V-" + ruleId.Replace("SV-", ""),
                BenchmarkId = benchmarkId
            },
            Severity = severity,
            Title = "Test control " + ruleId
        };
    }

    private static IReadOnlyList<ControlRecord> SampleControls() => new[]
    {
        MakeControl("SV-001", "high", "Win11_STIG"),
        MakeControl("SV-002", "high", "Win11_STIG"),
        MakeControl("SV-003", "medium", "Win11_STIG"),
        MakeControl("SV-004", "medium", "Server2019_STIG"),
        MakeControl("SV-005", "low", "Server2019_STIG"),
    };

    [Fact]
    public void Filter_NoFilters_ReturnsAll()
    {
        var controls = SampleControls();
        var result = _sut.Filter(controls, null, null, null);
        result.Should().HaveCount(5);
    }

    [Fact]
    public void Filter_ByRuleId_ReturnsOnlyMatching()
    {
        var controls = SampleControls();
        var result = _sut.Filter(controls, new[] { "SV-001", "SV-003" }, null, null);
        result.Should().HaveCount(2);
        result.Select(c => c.ExternalIds.RuleId).Should().BeEquivalentTo("SV-001", "SV-003");
    }

    [Fact]
    public void Filter_ByVulnId_AlsoMatches()
    {
        var controls = SampleControls();
        var result = _sut.Filter(controls, new[] { "V-001" }, null, null);
        result.Should().HaveCount(1);
        result[0].ExternalIds.RuleId.Should().Be("SV-001");
    }

    [Fact]
    public void Filter_BySeverity_High_ReturnsOnlyCatI()
    {
        var controls = SampleControls();
        var result = _sut.Filter(controls, null, new[] { "high" }, null);
        result.Should().HaveCount(2);
        result.Should().OnlyContain(c => c.Severity == "high");
    }

    [Fact]
    public void Filter_BySeverityCatI_MapsToHigh()
    {
        var controls = SampleControls();
        var result = _sut.Filter(controls, null, new[] { "CAT I" }, null);
        result.Should().HaveCount(2);
        result.Should().OnlyContain(c => c.Severity == "high");
    }

    [Fact]
    public void Filter_MultipleSeverities_ReturnsUnion()
    {
        var controls = SampleControls();
        var result = _sut.Filter(controls, null, new[] { "high", "low" }, null);
        result.Should().HaveCount(3);
    }

    [Fact]
    public void Filter_ByCategory_FiltersByBenchmarkId()
    {
        var controls = SampleControls();
        var result = _sut.Filter(controls, null, null, new[] { "Server2019_STIG" });
        result.Should().HaveCount(2);
        result.Should().OnlyContain(c => c.ExternalIds.BenchmarkId == "Server2019_STIG");
    }

    [Fact]
    public void Filter_CombinedRuleIdAndSeverity_AppliesConjunction()
    {
        var controls = SampleControls();
        // SV-003 is medium, so filter high + SV-003 should return nothing (AND logic)
        var result = _sut.Filter(controls, new[] { "SV-003" }, new[] { "high" }, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Filter_NoMatches_ReturnsEmpty()
    {
        var controls = SampleControls();
        var result = _sut.Filter(controls, new[] { "NONEXISTENT" }, null, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Filter_EmptyFilterLists_TreatedAsNoFilter()
    {
        var controls = SampleControls();
        var result = _sut.Filter(controls, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        result.Should().HaveCount(5);
    }

    [Theory]
    [InlineData("high", "high")]
    [InlineData("HIGH", "high")]
    [InlineData("CAT I", "high")]
    [InlineData("cat1", "high")]
    [InlineData("medium", "medium")]
    [InlineData("CAT II", "medium")]
    [InlineData("cat2", "medium")]
    [InlineData("low", "low")]
    [InlineData("CAT III", "low")]
    [InlineData("cat3", "low")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    public void NormalizeSeverity_MapsCorrectly(string? input, string expected)
    {
        ControlFilterService.NormalizeSeverity(input).Should().Be(expected);
    }
}

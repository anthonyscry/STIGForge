using FluentAssertions;
using STIGForge.Apply;
using STIGForge.Apply.DryRun;

namespace STIGForge.UnitTests.Apply;

public sealed class DryRunCollectorTests
{
    [Fact]
    public void Build_NoChanges_ReturnsEmptyReport()
    {
        var collector = new DryRunCollector();

        var report = collector.Build("C:\\Bundle", "AuditOnly");

        report.TotalChanges.Should().Be(0);
        report.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithChanges_IncludesAllEntries()
    {
        var collector = new DryRunCollector();
        collector.Add("DSC", "First", "A", "B", "SV-1", "Registry", "HKLM\\A");
        collector.Add("LGPO", "Second", null, "PolicyA", "SV-2", "GroupPolicy", "Machine");
        collector.Add("Script", "Third", null, "C:\\script.ps1", "SV-3", "Script", "C:\\script.ps1");

        var report = collector.Build("C:\\Bundle", "AuditOnly");

        report.TotalChanges.Should().Be(3);
        report.Changes.Should().HaveCount(3);
        report.Changes[0].Description.Should().Be("First");
        report.Changes[1].StepName.Should().Be("LGPO");
        report.Changes[2].ProposedValue.Should().Be("C:\\script.ps1");
    }

    [Fact]
    public void Build_SetsMetadata_BundleRootAndMode()
    {
        var collector = new DryRunCollector();

        var report = collector.Build("C:\\BundleX", "AuditOnly");

        report.BundleRoot.Should().Be("C:\\BundleX");
        report.Mode.Should().Be("AuditOnly");
    }

    [Fact]
    public void Add_SetsAllProperties()
    {
        var collector = new DryRunCollector();

        collector.Add("DSC", "Set timeout", "600", "900", "SV-12345", "Registry", "ScreenLockTimeout");
        var report = collector.Build("C:\\Bundle", "AuditOnly");
        var change = report.Changes.Single();

        change.StepName.Should().Be("DSC");
        change.Description.Should().Be("Set timeout");
        change.CurrentValue.Should().Be("600");
        change.ProposedValue.Should().Be("900");
        change.RuleId.Should().Be("SV-12345");
        change.ResourceType.Should().Be("Registry");
        change.ResourcePath.Should().Be("ScreenLockTimeout");
    }

    [Fact]
    public void AddRange_SetsStepName()
    {
        var collector = new DryRunCollector();
        var range = new[]
        {
            new DryRunChange { StepName = "Old", Description = "A" },
            new DryRunChange { StepName = "Old", Description = "B" }
        };

        collector.AddRange("DSC", range);
        var report = collector.Build("C:\\Bundle", "AuditOnly");

        report.Changes.Should().OnlyContain(x => x.StepName == "DSC");
    }
}

using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class ControlRecordContractValidatorTests
{
    private static ControlRecord ValidRecord(string id = "V-001") => new()
    {
        ControlId = id,
        Title = "Test Title",
        Severity = "medium",
        ExternalIds = new ExternalIds { RuleId = "SV-001" },
        Applicability = new Applicability(),
        Revision = new RevisionInfo { PackName = "TestPack" }
    };

    [Fact]
    public void Validate_EmptyList_ReturnsNoErrors()
    {
        var result = ControlRecordContractValidator.Validate(Array.Empty<ControlRecord>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ValidRecord_ReturnsNoErrors()
    {
        var result = ControlRecordContractValidator.Validate(new[] { ValidRecord() });
        result.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingControlId_ReportsError()
    {
        var record = ValidRecord();
        record.ControlId = string.Empty;

        var result = ControlRecordContractValidator.Validate(new[] { record });

        result.Should().ContainSingle(e => e.Contains("missing control_id"));
    }

    [Fact]
    public void Validate_MissingTitle_ReportsError()
    {
        var record = ValidRecord();
        record.Title = "   ";

        var result = ControlRecordContractValidator.Validate(new[] { record });

        result.Should().ContainSingle(e => e.Contains("missing title"));
    }

    [Fact]
    public void Validate_MissingSeverity_ReportsError()
    {
        var record = ValidRecord();
        record.Severity = string.Empty;

        var result = ControlRecordContractValidator.Validate(new[] { record });

        result.Should().ContainSingle(e => e.Contains("missing severity"));
    }

    [Fact]
    public void Validate_NullExternalIds_ReportsError()
    {
        var record = ValidRecord();
        record.ExternalIds = null!;

        var result = ControlRecordContractValidator.Validate(new[] { record });

        result.Should().ContainSingle(e => e.Contains("missing external_ids"));
    }

    [Fact]
    public void Validate_NullApplicability_ReportsError()
    {
        var record = ValidRecord();
        record.Applicability = null!;

        var result = ControlRecordContractValidator.Validate(new[] { record });

        result.Should().ContainSingle(e => e.Contains("missing applicability"));
    }

    [Fact]
    public void Validate_NullRevision_ReportsError()
    {
        var record = ValidRecord();
        record.Revision = null!;

        var result = ControlRecordContractValidator.Validate(new[] { record });

        result.Should().ContainSingle(e => e.Contains("missing revision"));
    }

    [Fact]
    public void Validate_RevisionMissingPackName_ReportsError()
    {
        var record = ValidRecord();
        record.Revision = new RevisionInfo { PackName = "" };

        var result = ControlRecordContractValidator.Validate(new[] { record });

        result.Should().ContainSingle(e => e.Contains("missing revision.pack_name"));
    }

    [Fact]
    public void Validate_MultipleInvalidRecords_ReportsAllErrors()
    {
        var r1 = ValidRecord("V-001");
        r1.Title = string.Empty;

        var r2 = ValidRecord("V-002");
        r2.Severity = string.Empty;
        r2.ExternalIds = null!;

        var result = ControlRecordContractValidator.Validate(new[] { r1, r2 });

        result.Should().HaveCountGreaterThanOrEqualTo(3);
        result.Should().Contain(e => e.StartsWith("V-001") && e.Contains("missing title"));
        result.Should().Contain(e => e.StartsWith("V-002") && e.Contains("missing severity"));
        result.Should().Contain(e => e.StartsWith("V-002") && e.Contains("missing external_ids"));
    }

    [Fact]
    public void Validate_ErrorKey_UsesControlId_WhenPresent()
    {
        var record = ValidRecord("V-123");
        record.Title = string.Empty;

        var result = ControlRecordContractValidator.Validate(new[] { record });

        result.Should().ContainSingle(e => e.StartsWith("V-123:"));
    }

    [Fact]
    public void Validate_ErrorKey_UsesIndex_WhenControlIdMissing()
    {
        var record = ValidRecord();
        record.ControlId = string.Empty;
        record.Title = string.Empty;

        var result = ControlRecordContractValidator.Validate(new[] { record });

        result.Should().Contain(e => e.StartsWith("index:0"));
    }
}

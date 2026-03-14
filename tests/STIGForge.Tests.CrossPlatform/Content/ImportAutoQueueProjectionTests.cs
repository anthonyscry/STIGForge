using FluentAssertions;
using STIGForge.Content.Import;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class ImportAutoQueueProjectionTests
{
    // ── Guard clauses ────────────────────────────────────────────────────────

    [Fact]
    public void Project_NullPlanned_ThrowsArgumentNullException()
    {
        var act = () => ImportAutoQueueProjection.Project(null!, []);
        act.Should().Throw<ArgumentNullException>().WithParameterName("planned");
    }

    [Fact]
    public void Project_NullFailures_ThrowsArgumentNullException()
    {
        var act = () => ImportAutoQueueProjection.Project([], null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("failures");
    }

    // ── Empty / trivial inputs ───────────────────────────────────────────────

    [Fact]
    public void Project_EmptyPlannedAndFailures_ReturnsBothListsEmpty()
    {
        var result = ImportAutoQueueProjection.Project([], []);

        result.AutoCommitted.Should().BeEmpty();
        result.Exceptions.Should().BeEmpty();
    }

    [Fact]
    public void Project_AllItems_NoFailures_AllInAutoCommitted()
    {
        var planned = new[]
        {
            MakeItem("file-a.zip", "file-a.zip"),
            MakeItem("file-b.zip", "file-b.zip")
        };

        var result = ImportAutoQueueProjection.Project(planned, []);

        result.AutoCommitted.Should().HaveCount(2);
        result.Exceptions.Should().BeEmpty();
        result.AutoCommitted.All(r => r.StateLabel == "AutoCommitted").Should().BeTrue();
    }

    // ── Null row in planned is skipped ───────────────────────────────────────

    [Fact]
    public void Project_NullRowInPlanned_IsSkipped()
    {
        var planned = new PlannedContentImport?[] { null, MakeItem("x.zip", "x.zip") };
        var result = ImportAutoQueueProjection.Project(planned!, []);

        result.AutoCommitted.Should().HaveCount(1);
        result.Exceptions.Should().BeEmpty();
    }

    // ── Failure matching by file name ────────────────────────────────────────

    [Fact]
    public void Project_SingleFailure_ByFileName_MovesToExceptions()
    {
        var planned = new[] { MakeItem("content.zip", "content.zip") };

        var result = ImportAutoQueueProjection.Project(planned, ["content.zip"]);

        result.Exceptions.Should().HaveCount(1);
        result.AutoCommitted.Should().BeEmpty();
        result.Exceptions[0].StateLabel.Should().Be("Failed");
    }

    [Fact]
    public void Project_FailureFileNameWithPath_ExtractsFileNameForMatch()
    {
        var planned = new[] { MakeItem(@"C:\imports\archive.zip", "archive.zip") };

        // Failure string references path — fallback to file-name extraction
        var result = ImportAutoQueueProjection.Project(planned, [@"C:\imports\archive.zip"]);

        result.Exceptions.Should().HaveCount(1);
        result.AutoCommitted.Should().BeEmpty();
    }

    // ── Failure matching by route ────────────────────────────────────────────

    [Fact]
    public void Project_FailureWithRoute_MatchesByZipPathAndRoute()
    {
        var planned = new[]
        {
            MakeItemWithZipPath("data.zip", "data.zip", ContentImportRoute.ConsolidatedZip),
            MakeItem("other.zip", "other.zip")
        };

        // Format: "identifier(RouteName): detail"
        var result = ImportAutoQueueProjection.Project(
            planned,
            ["data.zip(ConsolidatedZip): extraction failed"]);

        result.Exceptions.Should().HaveCount(1);
        result.Exceptions[0].Planned.ZipPath.Should().Be("data.zip");
        result.AutoCommitted.Should().HaveCount(1);
        result.AutoCommitted[0].Planned.ZipPath.Should().Be("other.zip");
    }

    [Fact]
    public void Project_FailureWithRoute_FallsBackToFileNameRoute()
    {
        var planned = new[]
        {
            MakeItemFull("archive.zip", "archive.zip", "", ContentImportRoute.ConsolidatedZip)
        };

        var result = ImportAutoQueueProjection.Project(
            planned,
            ["archive.zip(ConsolidatedZip): error"]);

        result.Exceptions.Should().HaveCount(1);
    }

    // ── Multiple failures ────────────────────────────────────────────────────

    [Fact]
    public void Project_MultipleFailures_AllMatchedToCorrectItems()
    {
        var planned = new[]
        {
            MakeItem("a.zip", "a.zip"),
            MakeItem("b.zip", "b.zip"),
            MakeItem("c.zip", "c.zip")
        };

        var result = ImportAutoQueueProjection.Project(planned, ["a.zip", "c.zip"]);

        result.Exceptions.Should().HaveCount(2);
        result.AutoCommitted.Should().HaveCount(1);
        result.AutoCommitted[0].Planned.ZipPath.Should().Be("b.zip");
        result.Exceptions.Select(r => r.Planned.ZipPath).Should().Contain(["a.zip", "c.zip"]);
    }

    // ── State labels ─────────────────────────────────────────────────────────

    [Fact]
    public void Project_ExceptionRows_HaveFailedLabel()
    {
        var planned = new[] { MakeItem("fail.zip", "fail.zip") };
        var result = ImportAutoQueueProjection.Project(planned, ["fail.zip"]);

        result.Exceptions[0].StateLabel.Should().Be("Failed");
    }

    [Fact]
    public void Project_AutoCommittedRows_HaveAutoCommittedLabel()
    {
        var planned = new[] { MakeItem("ok.zip", "ok.zip") };
        var result = ImportAutoQueueProjection.Project(planned, []);

        result.AutoCommitted[0].StateLabel.Should().Be("AutoCommitted");
    }

    // ── Duplicate failures consume one row at a time ─────────────────────────

    [Fact]
    public void Project_DuplicateFailureString_ConsumesOnlyOneMatchingRow()
    {
        var planned = new[]
        {
            MakeItem("dup.zip", "dup.zip"),
            MakeItem("dup.zip", "dup.zip")
        };

        var result = ImportAutoQueueProjection.Project(planned, ["dup.zip"]);

        // Only one row is consumed by the first failure match
        result.Exceptions.Should().HaveCount(1);
        result.AutoCommitted.Should().HaveCount(1);
    }

    // ── Failure strings with detail in parentheses ───────────────────────────

    [Fact]
    public void Project_FailureWithTrailingDetail_StripsSuffixForMatch()
    {
        var planned = new[] { MakeItem("myfile.zip", "myfile.zip") };

        // Format without a route: "identifier (some detail)"
        var result = ImportAutoQueueProjection.Project(
            planned,
            ["myfile.zip (hash mismatch)"]);

        result.Exceptions.Should().HaveCount(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PlannedContentImport MakeItem(string zipPath, string fileName) =>
        new() { ZipPath = zipPath, FileName = fileName, Route = ContentImportRoute.ConsolidatedZip };

    private static PlannedContentImport MakeItemWithZipPath(string zipPath, string fileName, ContentImportRoute route) =>
        new() { ZipPath = zipPath, FileName = fileName, Route = route };

    private static PlannedContentImport MakeItemFull(
        string zipPath, string fileName, string zipInternalPath, ContentImportRoute route) =>
        new() { ZipPath = zipPath, FileName = fileName, Route = route };
}

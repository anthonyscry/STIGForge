using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace STIGForge.Tests.CrossPlatform.Infrastructure;

public class MissionRunRepositoryTests : IDisposable
{
    private readonly string _connStr;
    private readonly SqliteConnection _keepAlive;
    private readonly MissionRunRepository _sut;
    private readonly string _runId;

    public MissionRunRepositoryTests()
    {
        var dbName = Guid.NewGuid().ToString("N");
        _connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connStr);
        _keepAlive.Open();
        DbBootstrap.EnsureCreated(_connStr);
        _sut = new MissionRunRepository(new DbConnectionString(_connStr));

        _runId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        // Seed a run
        _sut.CreateRunAsync(new MissionRun
        {
            RunId = _runId,
            Label = "test-run",
            BundleRoot = "/bundles/test",
            Status = MissionRunStatus.Completed,
            CreatedAt = now
        }, CancellationToken.None).GetAwaiter().GetResult();

        // Seed 5 timeline events with seq 1-5
        for (int i = 1; i <= 5; i++)
        {
            _sut.AppendEventAsync(new MissionTimelineEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                RunId = _runId,
                Seq = i,
                Phase = MissionPhase.Verify,
                StepName = $"Step-{i}",
                Status = MissionEventStatus.Finished,
                OccurredAt = now.AddSeconds(i)
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task GetTimeline_DefaultParams_ReturnsAllEvents()
    {
        var events = await _sut.GetTimelineAsync(_runId, CancellationToken.None, limit: 1000, offset: 0);
        events.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetTimeline_WithLimit_ReturnsCorrectCount()
    {
        var events = await _sut.GetTimelineAsync(_runId, CancellationToken.None, limit: 2, offset: 0);
        events.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTimeline_WithOffset_SkipsEvents()
    {
        var events = await _sut.GetTimelineAsync(_runId, CancellationToken.None, limit: 1000, offset: 2);
        events.Should().HaveCount(3);
        events.Select(e => e.Seq).Should().Equal(3, 4, 5);
    }

    [Fact]
    public async Task GetTimeline_OffsetBeyondEnd_ReturnsEmpty()
    {
        var events = await _sut.GetTimelineAsync(_runId, CancellationToken.None, limit: 1000, offset: 100);
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTimeline_ResultsOrderedBySeq()
    {
        var events = await _sut.GetTimelineAsync(_runId, CancellationToken.None);
        events.Select(e => e.Seq).Should().BeInAscendingOrder();
    }
}

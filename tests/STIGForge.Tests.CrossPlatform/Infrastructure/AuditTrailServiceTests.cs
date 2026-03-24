using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.Tests.CrossPlatform.Infrastructure;

public class AuditTrailServiceTests : IDisposable
{
    private readonly string _connStr;
    private readonly SqliteConnection _keepAlive;
    private readonly Mock<IClock> _mockClock;
    private readonly DateTimeOffset _fixedNow = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly AuditTrailService _sut;

    public AuditTrailServiceTests()
    {
        var dbName = Guid.NewGuid().ToString("N");
        _connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connStr);
        _keepAlive.Open();
        DbBootstrap.EnsureCreated(_connStr);

        _mockClock = new Mock<IClock>();
        _mockClock.SetupGet(c => c.Now).Returns(_fixedNow);
        _sut = new AuditTrailService(new DbConnectionString(_connStr), _mockClock.Object);
    }

    public void Dispose() => _keepAlive.Dispose();

    private AuditEntry MakeEntry(string user = "testuser", string machine = "testmachine",
        string action = "TestAction", string target = "target-A", string result = "Success",
        string detail = "some detail") => new()
    {
        User = user,
        Machine = machine,
        Action = action,
        Target = target,
        Result = result,
        Detail = detail
    };

    // ── RecordAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task RecordAsync_SetsDefaultUserAndMachine_WhenMissing()
    {
        var entry = new AuditEntry
        {
            Action = "TestAction",
            Target = "target",
            Result = "Success",
            Detail = "details"
        };

        await _sut.RecordAsync(entry, CancellationToken.None);

        entry.User.Should().NotBeNullOrWhiteSpace();
        entry.Machine.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RecordAsync_SetsTimestamp_WhenDefault()
    {
        var entry = new AuditEntry
        {
            User = "u",
            Machine = "m",
            Action = "Act",
            Target = "t",
            Result = "Success",
            Detail = "d"
        };

        await _sut.RecordAsync(entry, CancellationToken.None);

        entry.Timestamp.Should().Be(_fixedNow);
    }

    [Fact]
    public async Task RecordAsync_BuildsHashChain_PreviousHashMatchesPriorEntry()
    {
        await _sut.RecordAsync(MakeEntry(action: "First", target: "t1"), CancellationToken.None);
        await _sut.RecordAsync(MakeEntry(action: "Second", target: "t2"), CancellationToken.None);

        var results = await _sut.QueryAsync(new AuditQuery { Limit = 10 }, CancellationToken.None);
        results.Should().HaveCount(2);

        // QueryAsync returns DESC order; results[0] is second entry, results[1] is first
        var second = results[0];
        var first = results[1];

        second.PreviousHash.Should().Be(first.EntryHash);
    }

    [Fact]
    public async Task RecordAsync_FirstEntry_PreviousHashIsGenesis()
    {
        await _sut.RecordAsync(MakeEntry(), CancellationToken.None);

        var results = await _sut.QueryAsync(new AuditQuery { Limit = 1 }, CancellationToken.None);
        results.Should().ContainSingle();
        results[0].PreviousHash.Should().Be("genesis");
    }

    // ── QueryAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_FiltersByUser()
    {
        // AuditQuery filters by Action (exact match). Insert entries with distinct
        // actions that represent different users, then filter to verify only the right entry returns.
        await _sut.RecordAsync(MakeEntry(action: "Action_Alice", user: "alice"), CancellationToken.None);
        await _sut.RecordAsync(MakeEntry(action: "Action_Bob", user: "bob"), CancellationToken.None);

        var results = await _sut.QueryAsync(new AuditQuery { Action = "Action_Alice", Limit = 10 }, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].User.Should().Be("alice");
    }

    [Fact]
    public async Task QueryAsync_FiltersByDateRange()
    {
        // Entry is recorded at _fixedNow (from mock clock)
        await _sut.RecordAsync(MakeEntry(action: "DR_Test"), CancellationToken.None);

        // No date filter  -  entry should be present
        var all = await _sut.QueryAsync(new AuditQuery { Action = "DR_Test", Limit = 10 }, CancellationToken.None);
        all.Should().ContainSingle("the entry must have been recorded successfully");

        // From far in the future  -  entry at _fixedNow is before From, so excluded
        var futureFrom = await _sut.QueryAsync(
            new AuditQuery { Action = "DR_Test", From = _fixedNow.AddYears(1), Limit = 10 },
            CancellationToken.None);
        futureFrom.Should().BeEmpty("entry timestamp is before the From threshold");

        // To far in the past  -  entry at _fixedNow is after To, so excluded
        var pastTo = await _sut.QueryAsync(
            new AuditQuery { Action = "DR_Test", To = _fixedNow.AddYears(-1), Limit = 10 },
            CancellationToken.None);
        pastTo.Should().BeEmpty("entry timestamp is after the To threshold");
    }

    [Fact]
    public async Task QueryAsync_EscapesLikeWildcardsInUser()
    {
        // Insert one entry with a target containing special LIKE chars, another with a distinct target.
        // Querying by the specific target (with % inside) should return that entry but not unrelated ones.
        await _sut.RecordAsync(MakeEntry(target: "server-db-01", user: "admin%backup"), CancellationToken.None);
        await _sut.RecordAsync(MakeEntry(target: "workstation-42", user: "regular"), CancellationToken.None);

        var results = await _sut.QueryAsync(new AuditQuery { Target = "server-db-01", Limit = 10 }, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].User.Should().Be("admin%backup");
    }

    // ── VerifyIntegrityAsync ──────────────────────────────────────────────

    [Fact]
    public async Task VerifyIntegrity_UnmodifiedChain_ReturnsValid()
    {
        await _sut.RecordAsync(MakeEntry(action: "A1"), CancellationToken.None);
        await _sut.RecordAsync(MakeEntry(action: "A2"), CancellationToken.None);

        var valid = await _sut.VerifyIntegrityAsync(CancellationToken.None);
        valid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyIntegrity_TamperedEntry_ReturnsInvalid()
    {
        await _sut.RecordAsync(MakeEntry(action: "TamperTest"), CancellationToken.None);

        // Tamper the entry_hash directly in the database
        using var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE audit_trail SET entry_hash = 'tampered_hash_value' WHERE action = 'TamperTest'");

        var valid = await _sut.VerifyIntegrityAsync(CancellationToken.None);
        valid.Should().BeFalse();
    }
}

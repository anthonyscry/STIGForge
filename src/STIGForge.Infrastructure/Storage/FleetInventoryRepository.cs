using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.Storage;

public sealed class FleetInventoryRepository : IFleetInventoryRepository
{
  private readonly string _connectionString;

  public FleetInventoryRepository(DbConnectionString connectionString)
  {
    ArgumentNullException.ThrowIfNull(connectionString);
    _connectionString = connectionString.Value;
  }

  public async Task UpsertHostAsync(FleetHostRecord host, CancellationToken ct)
  {
    ArgumentNullException.ThrowIfNull(host);
    if (string.IsNullOrWhiteSpace(host.HostName))
      throw new ArgumentException("HostName is required.", nameof(host));

    host.UpdatedAt = DateTimeOffset.UtcNow;
    if (host.AddedAt == default)
      host.AddedAt = host.UpdatedAt;

    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    await conn.ExecuteAsync(new CommandDefinition(
      @"INSERT INTO fleet_inventory
          (host_name, ip_address, role, os_target, stig_pack_id, stig_pack_name,
           last_compliance_percent, last_compliance_measured_at, added_at, updated_at)
        VALUES
          (@HostName, @IpAddress, @Role, @OsTarget, @StigPackId, @StigPackName,
           @LastCompliancePercent, @LastComplianceMeasuredAt, @AddedAt, @UpdatedAt)
        ON CONFLICT(host_name) DO UPDATE SET
          ip_address                  = excluded.ip_address,
          role                        = excluded.role,
          os_target                   = excluded.os_target,
          stig_pack_id                = excluded.stig_pack_id,
          stig_pack_name              = excluded.stig_pack_name,
          last_compliance_percent     = excluded.last_compliance_percent,
          last_compliance_measured_at = excluded.last_compliance_measured_at,
          updated_at                  = excluded.updated_at",
      host,
      cancellationToken: ct)
    ).ConfigureAwait(false);
  }

  public async Task<FleetHostRecord?> GetHostAsync(string hostName, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    return await conn.QueryFirstOrDefaultAsync<FleetHostRecord>(new CommandDefinition(
      @"SELECT host_name as HostName, ip_address as IpAddress, role as Role,
               os_target as OsTarget, stig_pack_id as StigPackId, stig_pack_name as StigPackName,
               last_compliance_percent as LastCompliancePercent,
               last_compliance_measured_at as LastComplianceMeasuredAt,
               added_at as AddedAt, updated_at as UpdatedAt
        FROM fleet_inventory WHERE host_name = @hostName",
      new { hostName },
      cancellationToken: ct)
    ).ConfigureAwait(false);
  }

  public async Task<IReadOnlyList<FleetHostRecord>> ListHostsAsync(CancellationToken ct)
  {
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    var results = await conn.QueryAsync<FleetHostRecord>(new CommandDefinition(
      @"SELECT host_name as HostName, ip_address as IpAddress, role as Role,
               os_target as OsTarget, stig_pack_id as StigPackId, stig_pack_name as StigPackName,
               last_compliance_percent as LastCompliancePercent,
               last_compliance_measured_at as LastComplianceMeasuredAt,
               added_at as AddedAt, updated_at as UpdatedAt
        FROM fleet_inventory ORDER BY host_name",
      cancellationToken: ct)
    ).ConfigureAwait(false);

    return results.ToList();
  }

  public async Task<IReadOnlyList<FleetHostRecord>> ListHostsByRoleAsync(string role, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    var results = await conn.QueryAsync<FleetHostRecord>(new CommandDefinition(
      @"SELECT host_name as HostName, ip_address as IpAddress, role as Role,
               os_target as OsTarget, stig_pack_id as StigPackId, stig_pack_name as StigPackName,
               last_compliance_percent as LastCompliancePercent,
               last_compliance_measured_at as LastComplianceMeasuredAt,
               added_at as AddedAt, updated_at as UpdatedAt
        FROM fleet_inventory WHERE role = @role ORDER BY host_name",
      new { role },
      cancellationToken: ct)
    ).ConfigureAwait(false);

    return results.ToList();
  }

  public async Task UpdateComplianceStateAsync(string hostName, string stigPackId, double compliancePercent, DateTimeOffset measuredAt, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    await conn.ExecuteAsync(new CommandDefinition(
      @"UPDATE fleet_inventory
        SET stig_pack_id = @stigPackId,
            last_compliance_percent = @compliancePercent,
            last_compliance_measured_at = @measuredAt,
            updated_at = @updatedAt
        WHERE host_name = @hostName",
      new { hostName, stigPackId, compliancePercent, measuredAt, updatedAt = DateTimeOffset.UtcNow },
      cancellationToken: ct)
    ).ConfigureAwait(false);
  }

  public async Task<bool> RemoveHostAsync(string hostName, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    var rows = await conn.ExecuteAsync(new CommandDefinition(
      "DELETE FROM fleet_inventory WHERE host_name = @hostName",
      new { hostName },
      cancellationToken: ct)
    ).ConfigureAwait(false);

    return rows > 0;
  }
}

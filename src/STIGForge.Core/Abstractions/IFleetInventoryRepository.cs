namespace STIGForge.Core.Abstractions;

/// <summary>
/// Persistent inventory of fleet hosts: their role, OS target, STIG mapping,
/// and last compliance state. Prerequisite for multi-STIG targeting and dashboards.
/// </summary>
public interface IFleetInventoryRepository
{
  Task UpsertHostAsync(FleetHostRecord host, CancellationToken ct);
  Task<FleetHostRecord?> GetHostAsync(string hostName, CancellationToken ct);
  Task<IReadOnlyList<FleetHostRecord>> ListHostsAsync(CancellationToken ct);
  Task<IReadOnlyList<FleetHostRecord>> ListHostsByRoleAsync(string role, CancellationToken ct);
  Task UpdateComplianceStateAsync(string hostName, string stigPackId, double compliancePercent, DateTimeOffset measuredAt, CancellationToken ct);
  Task<bool> RemoveHostAsync(string hostName, CancellationToken ct);
}

public sealed class FleetHostRecord
{
  public string HostName { get; set; } = string.Empty;
  public string? IpAddress { get; set; }
  public string Role { get; set; } = string.Empty;
  public string OsTarget { get; set; } = string.Empty;
  public string StigPackId { get; set; } = string.Empty;
  public string StigPackName { get; set; } = string.Empty;
  public double LastCompliancePercent { get; set; }
  public DateTimeOffset? LastComplianceMeasuredAt { get; set; }
  public DateTimeOffset AddedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}

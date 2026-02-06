using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

public interface IContentPackRepository
{
  Task SaveAsync(ContentPack pack, CancellationToken ct);
  Task<ContentPack?> GetAsync(string packId, CancellationToken ct);
  Task<IReadOnlyList<ContentPack>> ListAsync(CancellationToken ct);
  Task DeleteAsync(string packId, CancellationToken ct);
}

public interface IControlRepository
{
  Task SaveControlsAsync(string packId, IReadOnlyList<ControlRecord> controls, CancellationToken ct);
  Task<IReadOnlyList<ControlRecord>> ListControlsAsync(string packId, CancellationToken ct);
  
  /// <summary>
  /// Verifies the SQLite schema has all required tables and columns.
  /// Returns true if schema is valid, false otherwise.
  /// Should be called during application startup or before import operations.
  /// </summary>
  Task<bool> VerifySchemaAsync(CancellationToken ct);
}

public interface IProfileRepository
{
  Task SaveAsync(Profile profile, CancellationToken ct);
  Task<Profile?> GetAsync(string profileId, CancellationToken ct);
  Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct);
  Task DeleteAsync(string profileId, CancellationToken ct);
}

public interface IOverlayRepository
{
  Task SaveAsync(Overlay overlay, CancellationToken ct);
  Task<Overlay?> GetAsync(string overlayId, CancellationToken ct);
  Task<IReadOnlyList<Overlay>> ListAsync(CancellationToken ct);
}

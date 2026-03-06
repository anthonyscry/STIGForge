using System.Text.Json;

namespace STIGForge.Core;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> instances to avoid per-call metadata cache rebuilds.
/// System.Text.Json rebuilds internal reflection/codegen caches for each unique options instance.
/// </summary>
public static class JsonOptions
{
    /// <summary>Default options (no special configuration).</summary>
    public static JsonSerializerOptions Default { get; } = new();

    /// <summary>Pretty-printed output for human-readable files.</summary>
    public static JsonSerializerOptions Indented { get; } = new() { WriteIndented = true };

    /// <summary>Case-insensitive property matching for deserializing external JSON.</summary>
    public static JsonSerializerOptions CaseInsensitive { get; } = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Pretty-printed + case-insensitive for round-tripping human-readable files.</summary>
    public static JsonSerializerOptions IndentedCaseInsensitive { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Pretty-printed with camelCase naming for eMASS-compatible JSON exports.</summary>
    public static JsonSerializerOptions IndentedCamelCase { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

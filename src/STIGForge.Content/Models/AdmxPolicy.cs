namespace STIGForge.Content.Models;

/// <summary>
/// Represents an ADMX policy definition.
/// Simple DTO for holding parsed ADMX policy information.
/// </summary>
public class AdmxPolicy
{
    /// <summary>
    /// Policy name attribute from ADMX
    /// </summary>
    public string PolicyName { get; set; } = string.Empty;

    /// <summary>
    /// Display name (may contain resource references like $(string.ResourceId))
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Registry key path where policy is stored
    /// </summary>
    public string? RegistryKey { get; set; }

    /// <summary>
    /// Registry value name
    /// </summary>
    public string? ValueName { get; set; }

    /// <summary>
    /// ADMX namespace
    /// </summary>
    public string? Namespace { get; set; }
}

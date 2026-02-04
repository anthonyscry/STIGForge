namespace STIGForge.Content.Models;

/// <summary>
/// Represents an OVAL definition for reference-only storage.
/// Does not execute OVAL logic - stores metadata for future reference.
/// </summary>
public class OvalDefinition
{
    /// <summary>
    /// OVAL definition identifier (e.g., "oval:gov.disa.stig:def:1001")
    /// </summary>
    public string DefinitionId { get; set; } = string.Empty;

    /// <summary>
    /// Definition title from metadata
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Definition class (compliance, inventory, patch, vulnerability, etc.)
    /// </summary>
    public string? Class { get; set; }

    /// <summary>
    /// Definition description from metadata
    /// </summary>
    public string? Description { get; set; }
}

using System.Text;
using System.Xml;

namespace STIGForge.Content.Extensions;

/// <summary>
/// Extension methods for XmlReader to support streaming XML parsing patterns.
/// These methods enable forward-only parsing without loading entire DOM into memory.
/// </summary>
public static class XmlReaderExtensions
{
    /// <summary>
    /// Gets the value of an attribute by name without modifying reader position.
    /// </summary>
    /// <param name="reader">The XmlReader instance</param>
    /// <param name="name">The attribute name to find</param>
    /// <returns>The attribute value if found, null otherwise</returns>
    public static string? GetAttribute(this XmlReader reader, string name)
    {
        if (reader.NodeType != XmlNodeType.Element)
            return null;

        // Store current position
        var currentDepth = reader.Depth;
        var currentName = reader.Name;

        // Search through attributes
        for (int i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (reader.Name.Equals(name, StringComparison.Ordinal))
            {
                var value = reader.Value;
                reader.MoveToElement(); // Restore position
                return value;
            }
        }

        // Restore position if no match found
        reader.MoveToElement();
        return null;
    }

    /// <summary>
    /// Reads the content of the current element as a string.
    /// Handles empty elements gracefully.
    /// </summary>
    /// <param name="reader">The XmlReader instance</param>
    /// <returns>The element content trimmed, or empty string if element is empty</returns>
    public static string? ReadElementContent(this XmlReader reader)
    {
        if (reader.NodeType != XmlNodeType.Element)
            return null;

        if (reader.IsEmptyElement)
            return string.Empty;

        var content = reader.ReadElementContentAsString();
        return content?.Trim();
    }

    /// <summary>
    /// Reads check-content elements within a check element.
    /// Captures the check/@system attribute and all check-content text.
    /// </summary>
    /// <param name="reader">The XmlReader positioned at a check element</param>
    /// <param name="checkSystem">Output parameter for the check/@system attribute value</param>
    /// <returns>The concatenated check-content text, or null if no content found</returns>
    public static string? ReadCheckContent(this XmlReader reader, out string? checkSystem)
    {
        checkSystem = null;

        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "check")
            return null;

        // Capture check/@system attribute before moving into children
        checkSystem = reader.GetAttribute("system");

        var content = new StringBuilder();
        var checkDepth = reader.Depth;

        // Move into check element children
        while (reader.Read())
        {
            // Stop when we exit the check element
            if (reader.NodeType == XmlNodeType.EndElement && 
                reader.LocalName == "check" && 
                reader.Depth == checkDepth)
            {
                break;
            }

            // Capture check-content elements
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "check-content")
            {
                var text = reader.ReadElementContent();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (content.Length > 0)
                        content.AppendLine();
                    content.Append(text);
                }
            }
        }

        return content.Length > 0 ? content.ToString().Trim() : null;
    }

    /// <summary>
    /// Moves the reader back to the element node after attribute navigation.
    /// Critical for non-destructive attribute reading patterns.
    /// </summary>
    /// <param name="reader">The XmlReader instance</param>
    /// <returns>True if successfully moved to element, false otherwise</returns>
    public static bool MoveToPreviousAttribute(this XmlReader reader)
    {
        return reader.MoveToElement();
    }
}

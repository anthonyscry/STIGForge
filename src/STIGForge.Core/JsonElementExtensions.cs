using System.Text.Json;

namespace STIGForge.Core;

public static class JsonElementExtensions
{
    public static bool TryGetPropertyCaseInsensitive(this JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        // Fast path: exact-case match via built-in hash lookup
        if (element.TryGetProperty(propertyName, out value))
            return true;

        // Slow path: case-insensitive linear scan
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    public static string? ReadStringProperty(this JsonElement element, string propertyName)
    {
        return element.TryGetPropertyCaseInsensitive(propertyName, out var val)
            && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;
    }
}

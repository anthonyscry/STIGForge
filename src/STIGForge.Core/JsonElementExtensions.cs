using System.Text.Json;

namespace STIGForge.Core;

public static class JsonElementExtensions
{
    public static bool TryGetPropertyCaseInsensitive(this JsonElement element, string propertyName, out JsonElement value)
    {
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

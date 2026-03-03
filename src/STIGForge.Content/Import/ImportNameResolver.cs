using System.Text.RegularExpressions;

namespace STIGForge.Content.Import;

internal sealed class ImportNameResolver
{
    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        { "jan", 1 }, { "january", 1 },
        { "feb", 2 }, { "february", 2 },
        { "mar", 3 }, { "march", 3 },
        { "apr", 4 }, { "april", 4 },
        { "may", 5 },
        { "jun", 6 }, { "june", 6 },
        { "jul", 7 }, { "july", 7 },
        { "aug", 8 }, { "august", 8 },
        { "sep", 9 }, { "sept", 9 }, { "september", 9 },
        { "oct", 10 }, { "october", 10 },
        { "nov", 11 }, { "november", 11 },
        { "dec", 12 }, { "december", 12 }
    };

    internal DateTimeOffset? GuessReleaseDate(string zipPath, string packName)
    {
        var name = Path.GetFileName(zipPath);
        var text = name + " " + packName;
        var month = ParseMonth(text);
        if (month.HasValue)
        {
            var year = ParseYear(text);
            if (year.HasValue)
                return new DateTimeOffset(new DateTime(year.Value, month.Value, 1));
        }

        return null;
    }

    internal string BuildAdmxTemplatePackName(string templateFolderName)
    {
        var baseName = templateFolderName;
        if (string.IsNullOrWhiteSpace(baseName))
            return "ADMX Templates - Imported";

        return "ADMX Templates - " + baseName.Trim();
    }

    internal int? ParseYear(string text)
    {
        for (var i = 0; i < text.Length - 3; i++)
        {
            if (char.IsDigit(text[i]) && char.IsDigit(text[i + 1]) && char.IsDigit(text[i + 2]) && char.IsDigit(text[i + 3]))
            {
                var year = int.Parse(text.Substring(i, 4));
                if (year >= 2000 && year <= 2100)
                    return year;
            }
        }

        return null;
    }

    internal int? ParseMonth(string text)
    {
        foreach (var kv in Months)
        {
            if (text.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                return kv.Value;
        }

        return null;
    }

    internal string BuildImportedPackName(string zipPath, string prefix)
    {
        var baseName = Path.GetFileNameWithoutExtension(zipPath);
        if (string.IsNullOrWhiteSpace(baseName))
            return prefix + "_Pack_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");

        baseName = CleanDisaPackName(baseName);
        return string.IsNullOrWhiteSpace(baseName)
            ? prefix + "_Pack_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss")
            : baseName;
    }

    internal string CleanDisaPackName(string raw)
    {
        var name = raw
            .Replace("_", " ")
            .Replace("-", " ");

        foreach (var strip in new[] { "U MS ", "U ", "Imported " })
        {
            if (name.StartsWith(strip, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(strip.Length);
        }

        var trimSuffixes = new[] { " STIG", " Benchmark", " Manual" };
        var suffixFound = string.Empty;
        foreach (var suffix in trimSuffixes)
        {
            var index = name.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                suffixFound = suffix.Trim();
                name = name.Substring(0, index);
                break;
            }
        }

        name = Regex.Replace(name, @"\s*V\d+R\d+\s*", " ").Trim();
        name = Regex.Replace(name, @"\s*\d{8} \d{6}\s*$", "").Trim();
        name = Regex.Replace(name, @"\s+", " ");

        if (!string.IsNullOrWhiteSpace(suffixFound))
            name = name + " " + suffixFound;

        return name.Trim();
    }
}

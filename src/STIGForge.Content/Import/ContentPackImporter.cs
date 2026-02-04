using System.IO.Compression;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Content.Models;

namespace STIGForge.Content.Import;

public enum PackFormat
{
    Unknown,
    Stig,
    Scap,
    Gpo
}

public sealed class ContentPackImporter
{
    private readonly IPathBuilder _paths;
    private readonly IHashingService _hash;
    private readonly IContentPackRepository _packs;
    private readonly IControlRepository _controls;

    public ContentPackImporter(IPathBuilder paths, IHashingService hash, IContentPackRepository packs, IControlRepository controls)
    {
        _paths = paths;
        _hash = hash;
        _packs = packs;
        _controls = controls;
    }

    public async Task<ContentPack> ImportZipAsync(string zipPath, string packName, string sourceLabel, CancellationToken ct)
    {
        var packId = Guid.NewGuid().ToString("n");
        var packRoot = _paths.GetPackRoot(packId);
        var rawRoot = Path.Combine(packRoot, "raw");
        Directory.CreateDirectory(rawRoot);

        // Extract ZIP
        ZipFile.ExtractToDirectory(zipPath, rawRoot);

        var zipHash = await _hash.Sha256FileAsync(zipPath, ct);

        var pack = new ContentPack
        {
            PackId = packId,
            Name = packName,
            ImportedAt = DateTimeOffset.Now,
            ReleaseDate = GuessReleaseDate(zipPath, packName),
            SourceLabel = sourceLabel,
            HashAlgorithm = "sha256",
            ManifestSha256 = zipHash
        };

        // Detect format and import accordingly
        var format = DetectPackFormat(rawRoot);
        var parsed = format switch
        {
            PackFormat.Stig => ImportStigZip(rawRoot, packName),
            PackFormat.Scap => ImportScapZip(rawRoot, packName),
            PackFormat.Gpo => ImportGpoZip(rawRoot, packName),
            _ => ImportStigZip(rawRoot, packName) // Default to STIG for backward compatibility
        };

        await _packs.SaveAsync(pack, ct);
        if (parsed.Count > 0)
            await _controls.SaveControlsAsync(pack.PackId, parsed, ct);

        var note = new
        {
            importedZip = Path.GetFileName(zipPath),
            zipHash,
            detectedFormat = format.ToString(),
            parsedControls = parsed.Count,
            timestamp = DateTimeOffset.Now
        };

        var notePath = Path.Combine(packRoot, "import_note.json");
        Directory.CreateDirectory(packRoot);
        File.WriteAllText(notePath, JsonSerializer.Serialize(note, new JsonSerializerOptions { WriteIndented = true }));

        return pack;
    }

    private PackFormat DetectPackFormat(string extractedRoot)
    {
        var allFiles = Directory.GetFiles(extractedRoot, "*.xml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(extractedRoot, "*.admx", SearchOption.AllDirectories))
            .Select(Path.GetFileName)
            .ToList();

        var hasXccdf = allFiles.Any(f => f.IndexOf("xccdf", StringComparison.OrdinalIgnoreCase) >= 0);
        var hasOval = allFiles.Any(f => f.IndexOf("oval", StringComparison.OrdinalIgnoreCase) >= 0);
        var hasAdmx = allFiles.Any(f => f.IndexOf("admx", StringComparison.OrdinalIgnoreCase) >= 0);

        // SCAP bundles have both XCCDF and OVAL
        if (hasXccdf && hasOval)
            return PackFormat.Scap;

        // GPO packages have ADMX files
        if (hasAdmx)
            return PackFormat.Gpo;

        // STIG packages have XCCDF only
        if (hasXccdf)
            return PackFormat.Stig;

        return PackFormat.Unknown;
    }

    private List<ControlRecord> ImportStigZip(string rawRoot, string packName)
    {
        var xccdfFiles = Directory.GetFiles(rawRoot, "*.xml", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p).IndexOf("xccdf", StringComparison.OrdinalIgnoreCase) >= 0)
            .Take(10)
            .ToList();

        var parsed = new List<ControlRecord>();
        foreach (var f in xccdfFiles)
        {
            try
            {
                parsed.AddRange(XccdfParser.Parse(f, packName));
            }
            catch (ParsingException ex)
            {
                Console.WriteLine($"Parsing error in {f}: {ex.Message}");
                // Continue to next file
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing {f}: {ex.Message}");
                // Continue to next file
            }
        }

        return parsed;
    }

    private List<ControlRecord> ImportScapZip(string rawRoot, string packName)
    {
        // For SCAP bundles, we need to find the bundle ZIP or use the extracted directory
        // ScapBundleParser expects a ZIP path, so we need to handle this differently
        
        // Find all XCCDF files and parse them
        var xccdfFiles = Directory.GetFiles(rawRoot, "*.xml", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p).IndexOf("xccdf", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        var parsed = new List<ControlRecord>();
        foreach (var f in xccdfFiles)
        {
            try
            {
                parsed.AddRange(XccdfParser.Parse(f, packName));
            }
            catch (ParsingException ex)
            {
                Console.WriteLine($"Parsing error in {f}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing {f}: {ex.Message}");
            }
        }

        // Parse OVAL files for metadata (reference-only)
        var ovalFiles = Directory.GetFiles(rawRoot, "*.xml", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p).IndexOf("oval", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        foreach (var f in ovalFiles)
        {
            try
            {
                var ovalDefs = OvalParser.Parse(f);
                Console.WriteLine($"Parsed OVAL: {f} ({ovalDefs.Count} definitions)");
                // OVAL definitions stored as metadata - not converted to ControlRecords
            }
            catch (ParsingException ex)
            {
                Console.WriteLine($"Parsing error in {f}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing {f}: {ex.Message}");
            }
        }

        return parsed;
    }

    private List<ControlRecord> ImportGpoZip(string rawRoot, string packName)
    {
        var admxFiles = Directory.GetFiles(rawRoot, "*.admx", SearchOption.AllDirectories)
            .Take(10)
            .ToList();

        var parsed = new List<ControlRecord>();
        foreach (var f in admxFiles)
        {
            try
            {
                parsed.AddRange(GpoParser.Parse(f, packName));
            }
            catch (ParsingException ex)
            {
                Console.WriteLine($"Parsing error in {f}: {ex.Message}");
                // Continue to next file
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing {f}: {ex.Message}");
                // Continue to next file
            }
        }

        return parsed;
    }

    private static DateTimeOffset? GuessReleaseDate(string zipPath, string packName)
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

    private static int? ParseYear(string text)
    {
        for (int i = 0; i < text.Length - 3; i++)
        {
            if (char.IsDigit(text[i]) && char.IsDigit(text[i + 1]) && char.IsDigit(text[i + 2]) && char.IsDigit(text[i + 3]))
            {
                var year = int.Parse(text.Substring(i, 4));
                if (year >= 2000 && year <= 2100) return year;
            }
        }
        return null;
    }

    private static int? ParseMonth(string text)
    {
        var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
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

        foreach (var kv in months)
        {
            if (text.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                return kv.Value;
        }

        return null;
    }
}

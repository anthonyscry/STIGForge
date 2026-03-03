using STIGForge.Core.Models;
using STIGForge.Core.Abstractions;
using STIGForge.Content.Models;

namespace STIGForge.Content.Import;

internal sealed class FormatSpecificImporter
{
    private readonly FormatDetector _formatDetector;

    internal FormatSpecificImporter(FormatDetector formatDetector)
    {
        _formatDetector = formatDetector;
    }

    internal List<ControlRecord> ImportStigZip(string rawRoot, string packName, List<ParsingError> errors)
    {
        var xccdfFiles = _formatDetector.GetXccdfCandidateXmlFiles(rawRoot);

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
                errors.Add(new ParsingError
                {
                    FilePath = Path.GetFileName(f),
                    ErrorMessage = ex.Message,
                    ErrorType = "ParsingException"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing {f}: {ex.Message}");
                errors.Add(new ParsingError
                {
                    FilePath = Path.GetFileName(f),
                    ErrorMessage = ex.Message,
                    ErrorType = ex.GetType().Name
                });
            }
        }

        return parsed;
    }

    internal List<ControlRecord> ImportScapZip(string rawRoot, string packName, List<ParsingError> errors)
    {
        var xccdfFiles = _formatDetector.GetXccdfCandidateXmlFiles(rawRoot);

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
                errors.Add(new ParsingError
                {
                    FilePath = Path.GetFileName(f),
                    ErrorMessage = ex.Message,
                    ErrorType = "ParsingException"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing {f}: {ex.Message}");
                errors.Add(new ParsingError
                {
                    FilePath = Path.GetFileName(f),
                    ErrorMessage = ex.Message,
                    ErrorType = ex.GetType().Name
                });
            }
        }

        var ovalFiles = Directory.GetFiles(rawRoot, "*.xml", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p).IndexOf("oval", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        foreach (var f in ovalFiles)
        {
            try
            {
                var ovalDefs = OvalParser.Parse(f);
                Console.WriteLine($"Parsed OVAL: {f} ({ovalDefs.Count} definitions)");
            }
            catch (ParsingException ex)
            {
                Console.WriteLine($"Parsing error in {f}: {ex.Message}");
                errors.Add(new ParsingError
                {
                    FilePath = Path.GetFileName(f),
                    ErrorMessage = ex.Message,
                    ErrorType = "ParsingException (OVAL metadata)"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing {f}: {ex.Message}");
                errors.Add(new ParsingError
                {
                    FilePath = Path.GetFileName(f),
                    ErrorMessage = ex.Message,
                    ErrorType = $"{ex.GetType().Name} (OVAL metadata)"
                });
            }
        }

        return parsed;
    }

    internal List<ControlRecord> ImportGpoZip(string rawRoot, string packName, List<ParsingError> errors)
    {
        try
        {
            var result = GpoParser.ParsePackage(rawRoot, packName);
            foreach (var warning in result.Warnings)
            {
                errors.Add(new ParsingError
                {
                    FilePath = rawRoot,
                    ErrorMessage = warning,
                    ErrorType = "GpoParseWarning"
                });
            }

            return result.Controls.ToList();
        }
        catch (Exception ex)
        {
            errors.Add(new ParsingError
            {
                FilePath = rawRoot,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            });
            return new List<ControlRecord>();
        }
    }
}

internal readonly record struct ImportProcessingResult(PackFormat Format, int ParsedCount, object Compatibility);

internal sealed class ImportProcessingHandler
{
    private readonly FormatDetector _formatDetector;
    private readonly FormatSpecificImporter _formatSpecificImporter;
    private readonly IContentPackRepository _packs;
    private readonly IControlRepository _controls;
    private readonly ImportManifestBuilder _manifestBuilder;

    internal ImportProcessingHandler(
        FormatDetector formatDetector,
        FormatSpecificImporter formatSpecificImporter,
        IContentPackRepository packs,
        IControlRepository controls,
        ImportManifestBuilder manifestBuilder)
    {
        _formatDetector = formatDetector;
        _formatSpecificImporter = formatSpecificImporter;
        _packs = packs;
        _controls = controls;
        _manifestBuilder = manifestBuilder;
    }

    internal async Task<ImportProcessingResult> ParseValidateAndPersistAsync(
        ContentPack pack,
        string rawRoot,
        string packName,
        ImportCheckpoint checkpoint,
        string packRoot,
        bool includeConflictDetails,
        CancellationToken ct)
    {
        var sourceStats = _formatDetector.CountSourceArtifacts(rawRoot);
        var formatResult = _formatDetector.DetectPackFormatWithConfidence(rawRoot, sourceStats);
        var format = formatResult.Format;
        var usedFallbackParser = format == PackFormat.Unknown;

        var parsingErrors = new List<ParsingError>();
        var parsed = format switch
        {
            PackFormat.Stig => _formatSpecificImporter.ImportStigZip(rawRoot, packName, parsingErrors),
            PackFormat.Scap => _formatSpecificImporter.ImportScapZip(rawRoot, packName, parsingErrors),
            PackFormat.Gpo => _formatSpecificImporter.ImportGpoZip(rawRoot, packName, parsingErrors),
            _ => _formatSpecificImporter.ImportStigZip(rawRoot, packName, parsingErrors)
        };

        checkpoint.Stage = ImportStage.Validating;
        checkpoint.ParsedControlCount = parsed.Count;
        checkpoint.Save(packRoot);

        var validationErrors = ControlRecordContractValidator.Validate(parsed);
        if (validationErrors.Count > 0)
        {
            checkpoint.Stage = ImportStage.Failed;
            checkpoint.ErrorMessage = $"Validation failed: {validationErrors.Count} errors";
            checkpoint.Save(packRoot);
            throw new ParsingException(
                $"[IMPORT-CONTRACT-001] Parsed controls failed canonical validation ({validationErrors.Count} errors): " +
                string.Join(" | ", validationErrors.Take(10)));
        }

        var conflictDetector = new ConflictDetector(_packs, _controls);
        var conflicts = await conflictDetector.DetectConflictsAsync(pack.PackId, parsed, ct).ConfigureAwait(false);
        var errorConflicts = conflicts.Where(c => c.Severity == ConflictSeverity.Error).ToList();
        if (errorConflicts.Count > 0)
        {
            checkpoint.Stage = ImportStage.Failed;
            checkpoint.ErrorMessage = $"Conflicts detected: {errorConflicts.Count} ERROR-level conflicts";
            checkpoint.Save(packRoot);

            var message = includeConflictDetails
                ? $"[IMPORT-CONFLICT-001] Import blocked due to {errorConflicts.Count} control conflicts with existing data. " +
                  $"Conflicting controls: {string.Join(", ", errorConflicts.Select(c => c.ControlId).Take(10))}. " +
                  "Review compatibility_matrix.json for details."
                : $"[IMPORT-CONFLICT-001] Import blocked due to {errorConflicts.Count} control conflicts.";

            throw new ParsingException(message);
        }

        checkpoint.Stage = ImportStage.Persisting;
        checkpoint.Save(packRoot);

        foreach (var control in parsed)
            control.SourcePackId = pack.PackId;

        await _packs.SaveAsync(pack, ct).ConfigureAwait(false);
        if (parsed.Count > 0)
            await _controls.SaveControlsAsync(pack.PackId, parsed, ct).ConfigureAwait(false);

        var compatibility = _manifestBuilder.BuildCompatibilityMatrix(
            formatResult,
            parsed.Count,
            sourceStats,
            usedFallbackParser,
            parsingErrors,
            conflicts);

        return new ImportProcessingResult(format, parsed.Count, compatibility);
    }
}

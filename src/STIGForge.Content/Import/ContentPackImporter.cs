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

public enum DetectionConfidence
{
    Low,
    Medium,
    High
}

public sealed class FormatDetectionResult
{
    public PackFormat Format { get; set; }
    public DetectionConfidence Confidence { get; set; }
    public List<string> Reasons { get; set; } = new();
}

public sealed class ParsingError
{
    public string FilePath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
}

public sealed class ContentPackImporter
{
    private readonly IPathBuilder _paths;
    private readonly IHashingService _hash;
    private readonly IContentPackRepository _packs;
    private readonly IControlRepository _controls;
    private readonly IAuditTrailService? _audit;

    public ContentPackImporter(IPathBuilder paths, IHashingService hash, IContentPackRepository packs, IControlRepository controls, IAuditTrailService? audit = null)
    {
        _paths = paths;
        _hash = hash;
        _packs = packs;
        _controls = controls;
        _audit = audit;
    }

    /// <summary>
    /// Finds incomplete imports (crashed during import) and returns their checkpoint info.
    /// These can be cleaned up or retried.
    /// </summary>
    public List<ImportCheckpoint> FindIncompleteImports()
    {
        var incomplete = new List<ImportCheckpoint>();
        
        // Get all pack directories
        var packsRoot = Path.GetDirectoryName(_paths.GetPackRoot("dummy"))!;
        if (!Directory.Exists(packsRoot))
            return incomplete;

        var packDirs = Directory.GetDirectories(packsRoot);
        
        foreach (var packDir in packDirs)
        {
            var checkpoint = ImportCheckpoint.Load(packDir);
            if (checkpoint != null && 
                checkpoint.Stage != ImportStage.Complete && 
                checkpoint.Stage != ImportStage.Failed)
            {
                incomplete.Add(checkpoint);
            }
        }

        return incomplete;
    }

    public async Task<ContentPack> ImportZipAsync(string zipPath, string packName, string sourceLabel, CancellationToken ct)
    {
        var packId = Guid.NewGuid().ToString("n");
        var packRoot = _paths.GetPackRoot(packId);
        var rawRoot = Path.Combine(packRoot, "raw");
        Directory.CreateDirectory(rawRoot);

        // Initialize checkpoint for crash recovery
        var checkpoint = new ImportCheckpoint
        {
            PackId = packId,
            ZipPath = zipPath,
            PackName = packName,
            Stage = ImportStage.Extracting,
            StartedAt = DateTimeOffset.Now
        };
        checkpoint.Save(packRoot);

        try
        {
            // Extract ZIP
            ZipFile.ExtractToDirectory(zipPath, rawRoot);
            
            checkpoint.Stage = ImportStage.Parsing;
            checkpoint.Save(packRoot);

            var zipHash = await _hash.Sha256FileAsync(zipPath, ct);

        var pack = new ContentPack
        {
            PackId = packId,
            Name = packName,
            ImportedAt = DateTimeOffset.Now,
            ReleaseDate = GuessReleaseDate(zipPath, packName),
            SourceLabel = sourceLabel,
            HashAlgorithm = "sha256",
            ManifestSha256 = zipHash,
            SchemaVersion = CanonicalContract.Version
        };

        // Detect format and import accordingly
        var sourceStats = CountSourceArtifacts(rawRoot);
        var formatResult = DetectPackFormatWithConfidence(rawRoot, sourceStats);
        var format = formatResult.Format;
        var usedFallbackParser = format == PackFormat.Unknown;
        
        var parsingErrors = new List<ParsingError>();
        var parsed = format switch
        {
            PackFormat.Stig => ImportStigZip(rawRoot, packName, parsingErrors),
            PackFormat.Scap => ImportScapZip(rawRoot, packName, parsingErrors),
            PackFormat.Gpo => ImportGpoZip(rawRoot, packName, parsingErrors),
            _ => ImportStigZip(rawRoot, packName, parsingErrors) // Default to STIG for backward compatibility
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

            // Detect conflicts before saving
            var conflictDetector = new ConflictDetector(_packs, _controls);
            var conflicts = await conflictDetector.DetectConflictsAsync(pack.PackId, parsed, ct);
            
            // Block import if any ERROR-level conflicts exist
            var errorConflicts = conflicts.Where(c => c.Severity == ConflictSeverity.Error).ToList();
            if (errorConflicts.Count > 0)
            {
                checkpoint.Stage = ImportStage.Failed;
                checkpoint.ErrorMessage = $"Conflicts detected: {errorConflicts.Count} ERROR-level conflicts";
                checkpoint.Save(packRoot);
                
                throw new ParsingException(
                    $"[IMPORT-CONFLICT-001] Import blocked due to {errorConflicts.Count} control conflicts with existing data. " +
                    $"Conflicting controls: {string.Join(", ", errorConflicts.Select(c => c.ControlId).Take(10))}. " +
                    "Review compatibility_matrix.json for details.");
            }

            checkpoint.Stage = ImportStage.Persisting;
            checkpoint.Save(packRoot);

            // Atomic save: both pack and controls in a logical transaction
            await _packs.SaveAsync(pack, ct);
            if (parsed.Count > 0)
                await _controls.SaveControlsAsync(pack.PackId, parsed, ct);

            var note = new
            {
                importedZip = Path.GetFileName(zipPath),
                zipHash,
                schemaVersion = CanonicalContract.Version,
                detectedFormat = format.ToString(),
                parsedControls = parsed.Count,
                timestamp = DateTimeOffset.Now
            };

            var notePath = Path.Combine(packRoot, "import_note.json");
            Directory.CreateDirectory(packRoot);
            File.WriteAllText(notePath, JsonSerializer.Serialize(note, new JsonSerializerOptions { WriteIndented = true }));

            var compatibility = BuildCompatibilityMatrix(formatResult, parsed.Count, sourceStats, usedFallbackParser, parsingErrors, conflicts);
            var compatibilityPath = Path.Combine(packRoot, "compatibility_matrix.json");
            File.WriteAllText(compatibilityPath, JsonSerializer.Serialize(compatibility, new JsonSerializerOptions { WriteIndented = true }));

            checkpoint.Stage = ImportStage.Complete;
            checkpoint.CompletedAt = DateTimeOffset.Now;
            checkpoint.Save(packRoot);

            if (_audit != null)
            {
              try
              {
                await _audit.RecordAsync(new AuditEntry
                {
                  Action = "import-pack",
                  Target = packName,
                  Result = "success",
                  Detail = $"PackId={packId}, Format={format}, Controls={parsed.Count}",
                  User = Environment.UserName,
                  Machine = Environment.MachineName,
                  Timestamp = DateTimeOffset.Now
                }, ct).ConfigureAwait(false);
              }
              catch { /* audit failure should not block import */ }
            }

            return pack;
        }
        catch (Exception ex)
        {
            // Mark checkpoint as failed for forensics
            checkpoint.Stage = ImportStage.Failed;
            checkpoint.ErrorMessage = ex.Message;
            checkpoint.CompletedAt = DateTimeOffset.Now;
            checkpoint.Save(packRoot);
            
            throw; // Re-throw to preserve original exception
        }
    }

    private static object BuildCompatibilityMatrix(FormatDetectionResult formatResult, int parsedControls, SourceArtifactStats sourceStats, bool usedFallbackParser, List<ParsingError> parsingErrors, List<ControlConflict> conflicts)
    {
        var format = formatResult.Format;
        var supportsXccdf = format is PackFormat.Stig or PackFormat.Scap;
        var supportsOvalMetadata = format is PackFormat.Scap;
        var supportsAdmx = format is PackFormat.Gpo;

        var lossy = new List<string>();
        var unsupported = new List<string>();
        
        // Format-specific lossy mappings with detailed explanations
        if (format == PackFormat.Scap)
        {
            lossy.Add("OVAL definitions are ingested as metadata only and not converted into canonical controls.");
            lossy.Add("OVAL test definitions (state/object/variable elements) are logged but not executed during apply phase.");
            lossy.Add("SCAP datastream components beyond XCCDF benchmarks are reference-only.");
        }
        if (format == PackFormat.Gpo)
        {
            lossy.Add("GPO/ADMX controls map to canonical controls with partial applicability context.");
            lossy.Add("ADMX policy categories outside security baseline scope are skipped.");
            lossy.Add("Registry-based GPO policies map to canonical control applicability but may lose presentation metadata.");
        }
        if (usedFallbackParser)
        {
            lossy.Add("Unknown format defaults to STIG parser behavior - non-XCCDF content may be ignored.");
            lossy.Add("Fallback parser does not attempt OVAL or ADMX processing.");
        }
        
        // Unsupported artifact mismatches
        if (sourceStats.OvalXmlCount > 0 && format != PackFormat.Scap)
            unsupported.Add($"OVAL XML files present ({sourceStats.OvalXmlCount}) but selected format does not support OVAL processing.");
        if (sourceStats.AdmxCount > 0 && format != PackFormat.Gpo)
            unsupported.Add($"ADMX files present ({sourceStats.AdmxCount}) but selected format does not support ADMX processing.");
        
        // Detect silent data loss (source files > parsed controls)
        var expectedFileCount = format switch
        {
            PackFormat.Scap => sourceStats.XccdfXmlCount,
            PackFormat.Gpo => sourceStats.AdmxCount,
            _ => sourceStats.XccdfXmlCount
        };
        
        if (expectedFileCount > 0 && parsedControls == 0)
            unsupported.Add($"Format detected as {format} with {expectedFileCount} source files, but zero controls parsed - possible format mismatch.");

        return new
        {
            schemaVersion = CanonicalContract.Version,
            detectedFormat = format.ToString(),
            detectionConfidence = formatResult.Confidence.ToString(),
            detectionReasons = formatResult.Reasons,
            parsedControls,
            usedFallbackParser,
            sourceArtifacts = new
            {
                sourceStats.XccdfXmlCount,
                sourceStats.OvalXmlCount,
                sourceStats.AdmxCount,
                sourceStats.TotalXmlCount,
                expectedFormatFiles = expectedFileCount
            },
            support = new
            {
                xccdf = supportsXccdf,
                ovalMetadata = supportsOvalMetadata,
                admx = supportsAdmx
            },
            lossyMappings = lossy,
            unsupportedMappings = unsupported,
            parsingErrors = parsingErrors.Select(e => new
            {
                file = e.FilePath,
                error = e.ErrorMessage,
                errorType = e.ErrorType
            }).ToList(),
            conflicts = new
            {
                total = conflicts.Count,
                bySeverity = new
                {
                    info = conflicts.Count(c => c.Severity == ConflictSeverity.Info),
                    warning = conflicts.Count(c => c.Severity == ConflictSeverity.Warning),
                    error = conflicts.Count(c => c.Severity == ConflictSeverity.Error)
                },
                details = conflicts.Select(c => new
                {
                    controlId = c.ControlId,
                    severity = c.Severity.ToString(),
                    reason = c.Reason,
                    existingPackId = c.ExistingPackId,
                    newPackId = c.NewPackId,
                    differences = c.Differences
                }).ToList()
            }
        };
    }

    private static SourceArtifactStats CountSourceArtifacts(string extractedRoot)
    {
        var xmlFiles = Directory.GetFiles(extractedRoot, "*.xml", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var xccdf = xmlFiles.Count(f => f!.IndexOf("xccdf", StringComparison.OrdinalIgnoreCase) >= 0);
        var oval = xmlFiles.Count(f => f!.IndexOf("oval", StringComparison.OrdinalIgnoreCase) >= 0);
        var admx = Directory.GetFiles(extractedRoot, "*.admx", SearchOption.AllDirectories).Length;

        return new SourceArtifactStats
        {
            XccdfXmlCount = xccdf,
            OvalXmlCount = oval,
            AdmxCount = admx,
            TotalXmlCount = xmlFiles.Count
        };
    }

    private sealed class SourceArtifactStats
    {
        public int XccdfXmlCount { get; set; }
        public int OvalXmlCount { get; set; }
        public int AdmxCount { get; set; }
        public int TotalXmlCount { get; set; }
    }

    private FormatDetectionResult DetectPackFormatWithConfidence(string extractedRoot, SourceArtifactStats stats)
    {
        var result = new FormatDetectionResult
        {
            Format = PackFormat.Unknown,
            Confidence = DetectionConfidence.Low
        };

        var hasXccdf = stats.XccdfXmlCount > 0;
        var hasOval = stats.OvalXmlCount > 0;
        var hasAdmx = stats.AdmxCount > 0;

        // SCAP bundles: XCCDF + OVAL
        if (hasXccdf && hasOval)
        {
            result.Format = PackFormat.Scap;
            result.Confidence = DetectionConfidence.High;
            result.Reasons.Add($"Found {stats.XccdfXmlCount} XCCDF and {stats.OvalXmlCount} OVAL files - characteristic SCAP bundle signature.");
            return result;
        }

        // GPO packages: ADMX files
        if (hasAdmx)
        {
            result.Format = PackFormat.Gpo;
            result.Confidence = hasXccdf || hasOval ? DetectionConfidence.Medium : DetectionConfidence.High;
            result.Reasons.Add($"Found {stats.AdmxCount} ADMX files - GPO policy format.");
            if (hasXccdf || hasOval)
                result.Reasons.Add("Warning: XCCDF/OVAL files also present - possible mixed-format bundle.");
            return result;
        }

        // STIG packages: XCCDF only (no OVAL, no ADMX)
        if (hasXccdf)
        {
            result.Format = PackFormat.Stig;
            result.Confidence = DetectionConfidence.High;
            result.Reasons.Add($"Found {stats.XccdfXmlCount} XCCDF files with no OVAL - standalone STIG benchmark.");
            return result;
        }

        // Unknown: No recognizable artifacts
        result.Format = PackFormat.Unknown;
        result.Confidence = DetectionConfidence.Low;
        result.Reasons.Add($"No XCCDF, OVAL, or ADMX files detected in {stats.TotalXmlCount} total XML files.");
        result.Reasons.Add("Will attempt STIG parser as fallback with low confidence.");
        return result;
    }

    private List<ControlRecord> ImportStigZip(string rawRoot, string packName, List<ParsingError> errors)
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

    private List<ControlRecord> ImportScapZip(string rawRoot, string packName, List<ParsingError> errors)
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

    private List<ControlRecord> ImportGpoZip(string rawRoot, string packName, List<ParsingError> errors)
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

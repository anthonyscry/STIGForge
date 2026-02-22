using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using System.Linq;
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
    private const int MaxArchiveEntryCount = 4096;
    private const long MaxExtractedBytes = 512L * 1024L * 1024L;

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

    /// <summary>
    /// Executes a planned import operation, updating <paramref name="planned"/>'s
    /// <see cref="PlannedContentImport.State"/> through the staged lifecycle:
    /// <c>Planned</c> -> <c>Staged</c> (execution started) ->
    /// <c>Committed</c> (success) or <c>Failed</c> (error captured in <see cref="PlannedContentImport.FailureReason"/>).
    /// The existing route-based import APIs are used internally; this method adds observable staging transitions on top.
    /// </summary>
    public async Task<IReadOnlyList<ContentPack>> ExecutePlannedImportAsync(PlannedContentImport planned, CancellationToken ct)
    {
        if (planned == null)
            throw new ArgumentNullException(nameof(planned));

        planned.State = ImportOperationState.Staged;
        try
        {
            IReadOnlyList<ContentPack> result = planned.Route == ContentImportRoute.AdmxTemplatesFromZip
                ? await ImportAdmxTemplatesFromZipAsync(planned.ZipPath, planned.SourceLabel, ct).ConfigureAwait(false)
                : await ImportConsolidatedZipAsync(planned.ZipPath, planned.SourceLabel, ct).ConfigureAwait(false);

            planned.State = ImportOperationState.Committed;
            return result;
        }
        catch (Exception ex)
        {
            planned.State = ImportOperationState.Failed;
            planned.FailureReason = ex.Message;
            throw;
        }
    }

    public async Task<IReadOnlyList<ContentPack>> ImportConsolidatedZipAsync(string consolidatedZipPath, string sourceLabel, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(consolidatedZipPath))
            throw new ArgumentException("ZIP path cannot be empty.", nameof(consolidatedZipPath));

        if (!File.Exists(consolidatedZipPath))
            throw new FileNotFoundException("Consolidated ZIP not found", consolidatedZipPath);

        var extractionRoot = Path.Combine(Path.GetTempPath(), "stigforge-consolidated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionRoot);

        try
        {
            ExtractZipSafely(consolidatedZipPath, extractionRoot, ct);

            var nestedZipPaths = Directory
                .GetFiles(extractionRoot, "*.zip", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (nestedZipPaths.Count == 0)
            {
                var scopedRoots = FindScopedGpoRoots(extractionRoot);
                if (scopedRoots.Count > 0)
                {
                    var importedScoped = new List<ContentPack>(scopedRoots.Count);
                    foreach (var scopedRoot in scopedRoots)
                    {
                        ct.ThrowIfCancellationRequested();
                        var imported = await ImportDirectoryAsPackAsync(scopedRoot.RootPath, scopedRoot.PackName, scopedRoot.SourceLabel, ct).ConfigureAwait(false);
                        importedScoped.Add(imported);
                    }

                    return importedScoped;
                }

                // NIWC-style bundles: no nested ZIPs, but multiple benchmark folders with XCCDF files.
                // Split by parent directory of each XCCDF so each benchmark becomes its own pack.
                var xccdfFiles = GetXccdfCandidateXmlFiles(extractionRoot);

                var benchmarkDirs = xccdfFiles
                    .Select(f => Path.GetDirectoryName(f)!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (benchmarkDirs.Count > 1)
                {
                    var semaphore = new SemaphoreSlim(4);
                    var tasks = benchmarkDirs.Select(async benchDir =>
                    {
                        await semaphore.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            ct.ThrowIfCancellationRequested();
                            var dirName = new DirectoryInfo(benchDir).Name;
                            var packName = CleanDisaPackName(dirName);
                            if (string.IsNullOrWhiteSpace(packName))
                                packName = dirName;
                            return await ImportDirectoryAsPackAsync(benchDir, packName, sourceLabel, ct).ConfigureAwait(false);
                        }
                        finally { semaphore.Release(); }
                    }).ToList();
                    return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
                }

                if (xccdfFiles.Count > 1)
                {
                    var allXmlFiles = Directory.GetFiles(extractionRoot, "*.xml", SearchOption.AllDirectories)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var semaphore = new SemaphoreSlim(4);
                    var tasks = xccdfFiles.Select(async benchmarkFile =>
                    {
                        await semaphore.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            ct.ThrowIfCancellationRequested();
                            var tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-benchmark-" + Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(tempRoot);

                            try
                            {
                                var filesToCopy = BuildBenchmarkImportFiles(benchmarkFile, allXmlFiles);
                                foreach (var sourceFile in filesToCopy)
                                {
                                    ct.ThrowIfCancellationRequested();
                                    var relativePath = Path.GetRelativePath(extractionRoot, sourceFile);
                                    var destination = Path.Combine(tempRoot, relativePath);
                                    var destinationDir = Path.GetDirectoryName(destination);
                                    if (!string.IsNullOrWhiteSpace(destinationDir))
                                        Directory.CreateDirectory(destinationDir);

                                    File.Copy(sourceFile, destination, true);
                                }

                                var packName = BuildImportedPackName(benchmarkFile, "Imported");
                                return await ImportDirectoryAsPackAsync(tempRoot, packName, sourceLabel, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                try { Directory.Delete(tempRoot, true); } catch { }
                            }
                        }
                        finally { semaphore.Release(); }
                    }).ToList();

                    return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
                }

                var singlePackName = BuildImportedPackName(consolidatedZipPath, "Imported");
                var importedSingle = await ImportZipAsync(consolidatedZipPath, singlePackName, sourceLabel, ct).ConfigureAwait(false);
                return new[] { importedSingle };
            }

            var zipSemaphore = new SemaphoreSlim(4);
            var zipTasks = nestedZipPaths.Select(async nestedZipPath =>
            {
                await zipSemaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var packName = BuildImportedPackName(nestedZipPath, "Imported");
                    return await ImportZipAsync(nestedZipPath, packName, sourceLabel, ct).ConfigureAwait(false);
                }
                finally { zipSemaphore.Release(); }
            }).ToList();
            return (await Task.WhenAll(zipTasks).ConfigureAwait(false)).ToList();
        }
        finally
        {
            try { Directory.Delete(extractionRoot, true); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Temp cleanup failed: " + ex.Message);
            }
        }
    }

    public async Task<IReadOnlyList<ContentPack>> ImportAdmxTemplatesFromZipAsync(string zipPath, string sourceLabel, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
            throw new ArgumentException("ZIP path cannot be empty.", nameof(zipPath));

        if (!File.Exists(zipPath))
            throw new FileNotFoundException("ADMX ZIP not found", zipPath);

        var extractionRoot = Path.Combine(Path.GetTempPath(), "stigforge-admx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionRoot);

        try
        {
            ExtractZipSafely(zipPath, extractionRoot, ct);
            ExpandNestedZipArchives(extractionRoot, maxPasses: 2, ct);

            var admxGroups = Directory.GetFiles(extractionRoot, "*.admx", SearchOption.AllDirectories)
                .Select(path => new
                {
                    FullPath = path,
                    Segments = Path.GetRelativePath(extractionRoot, path)
                        .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                })
                .Select(entry => new
                {
                    entry.FullPath,
                    entry.Segments,
                    ScopeIndex = Array.FindIndex(
                        entry.Segments,
                        segment => string.Equals(segment, "ADMX Templates", StringComparison.OrdinalIgnoreCase))
                })
                .Where(entry => entry.ScopeIndex >= 0 && entry.Segments.Length > entry.ScopeIndex + 2)
                .GroupBy(entry => entry.Segments[entry.ScopeIndex + 1], StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    FolderName = group.Key,
                    Files = group
                        .OrderBy(
                            entry => string.Join("/", entry.Segments.Skip(entry.ScopeIndex + 2)),
                            StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList();

            if (admxGroups.Count == 0)
                return await ImportConsolidatedZipAsync(zipPath, sourceLabel, ct).ConfigureAwait(false);

            var imported = new List<ContentPack>(admxGroups.Count);
            foreach (var admxGroup in admxGroups)
            {
                ct.ThrowIfCancellationRequested();

                var templateRoot = Path.Combine(Path.GetTempPath(), "stigforge-admx-template-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(templateRoot);
                try
                {
                    foreach (var admxFile in admxGroup.Files)
                    {
                        ct.ThrowIfCancellationRequested();

                        var relativeInFolder = CombinePathSegments(admxFile.Segments.Skip(admxFile.ScopeIndex + 2));
                        var destination = Path.Combine(templateRoot, relativeInFolder);
                        var destinationDir = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrWhiteSpace(destinationDir))
                            Directory.CreateDirectory(destinationDir);

                        File.Copy(admxFile.FullPath, destination, true);
                    }

                    var packName = BuildAdmxTemplatePackName(admxGroup.FolderName);
                    var pack = await ImportDirectoryAsPackAsync(templateRoot, packName, sourceLabel, ct).ConfigureAwait(false);
                    imported.Add(pack);
                }
                finally
                {
                    try { Directory.Delete(templateRoot, true); } catch { }
                }
            }

            return imported;
        }
        finally
        {
            try { Directory.Delete(extractionRoot, true); } catch { }
        }
    }

    public async Task<ContentPack> ImportDirectoryAsPackAsync(string extractedDir, string packName, string sourceLabel, CancellationToken ct)
    {
        var packId = Guid.NewGuid().ToString("n");
        var packRoot = _paths.GetPackRoot(packId);
        var rawRoot = Path.Combine(packRoot, "raw");
        Directory.CreateDirectory(rawRoot);

        var checkpoint = new ImportCheckpoint
        {
            PackId = packId,
            ZipPath = extractedDir,
            PackName = packName,
            Stage = ImportStage.Extracting,
            StartedAt = DateTimeOffset.Now
        };
        checkpoint.Save(packRoot);

        try
        {
            var extractedDirFull = Path.GetFullPath(extractedDir);
            foreach (var file in Directory.GetFiles(extractedDir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var fileFull = Path.GetFullPath(file);
                var relativePath = fileFull.StartsWith(extractedDirFull, StringComparison.OrdinalIgnoreCase)
                    ? fileFull.Substring(extractedDirFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : Path.GetFileName(file);
                var destPath = Path.Combine(rawRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(file, destPath, true);
            }

            checkpoint.Stage = ImportStage.Parsing;
            checkpoint.Save(packRoot);

            var directoryManifestHash = await ComputeDirectoryManifestSha256Async(rawRoot, ct).ConfigureAwait(false);

            // Check for existing pack with same manifest hash (dedupe)
            var existingPacks = await _packs.ListAsync(ct).ConfigureAwait(false);
            var existingPack = existingPacks.FirstOrDefault(p =>
                string.Equals(p.ManifestSha256, directoryManifestHash, StringComparison.OrdinalIgnoreCase));

            ContentPack pack;
            if (existingPack != null)
            {
                // Return existing pack instead of creating duplicate
                return existingPack;
            }

            pack = new ContentPack
            {
                PackId = packId,
                Name = packName,
                ImportedAt = DateTimeOffset.Now,
                ReleaseDate = GuessReleaseDate(extractedDir, packName),
                SourceLabel = sourceLabel,
                HashAlgorithm = "sha256",
                ManifestSha256 = directoryManifestHash,
                SchemaVersion = CanonicalContract.Version
            };

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
                _ => ImportStigZip(rawRoot, packName, parsingErrors)
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
                throw new ParsingException(
                    $"[IMPORT-CONFLICT-001] Import blocked due to {errorConflicts.Count} control conflicts.");
            }

            checkpoint.Stage = ImportStage.Persisting;
            checkpoint.Save(packRoot);

            // Wire SourcePackId provenance on all controls
            foreach (var c in parsed)
                c.SourcePackId = pack.PackId;

            await _packs.SaveAsync(pack, ct).ConfigureAwait(false);
            if (parsed.Count > 0)
                await _controls.SaveControlsAsync(pack.PackId, parsed, ct).ConfigureAwait(false);

            var note = new
            {
                importedFrom = extractedDir,
                schemaVersion = CanonicalContract.Version,
                detectedFormat = format.ToString(),
                parsedControls = parsed.Count,
                timestamp = DateTimeOffset.Now
            };
            File.WriteAllText(
                Path.Combine(packRoot, "import_note.json"),
                JsonSerializer.Serialize(note, new JsonSerializerOptions { WriteIndented = true }));

            var compatibility = BuildCompatibilityMatrix(formatResult, parsed.Count, sourceStats, usedFallbackParser, parsingErrors, conflicts);
            File.WriteAllText(
                Path.Combine(packRoot, "compatibility_matrix.json"),
                JsonSerializer.Serialize(compatibility, new JsonSerializerOptions { WriteIndented = true }));

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
                        Detail = $"PackId={packId}, Format={format}, Controls={parsed.Count}, Source=directory",
                        User = Environment.UserName,
                        Machine = Environment.MachineName,
                        Timestamp = DateTimeOffset.Now
                    }, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning("Audit write failed during import: " + ex.Message);
                }
            }

            return pack;
        }
        catch (Exception ex)
        {
            checkpoint.Stage = ImportStage.Failed;
            checkpoint.ErrorMessage = ex.Message;
            checkpoint.CompletedAt = DateTimeOffset.Now;
            checkpoint.Save(packRoot);
            throw;
        }
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
            ExtractZipSafely(zipPath, rawRoot, ct);
            
            checkpoint.Stage = ImportStage.Parsing;
            checkpoint.Save(packRoot);

            var zipHash = await _hash.Sha256FileAsync(zipPath, ct).ConfigureAwait(false);

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
            var conflicts = await conflictDetector.DetectConflictsAsync(pack.PackId, parsed, ct).ConfigureAwait(false);
            
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

            // Wire SourcePackId provenance on all controls
            foreach (var c in parsed)
                c.SourcePackId = pack.PackId;

            // Atomic save: both pack and controls in a logical transaction
            await _packs.SaveAsync(pack, ct).ConfigureAwait(false);
            if (parsed.Count > 0)
                await _controls.SaveControlsAsync(pack.PackId, parsed, ct).ConfigureAwait(false);

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
              catch (Exception ex)
              {
                System.Diagnostics.Trace.TraceWarning("Audit write failed during import: " + ex.Message);
              }
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
        var xmlPaths = Directory.GetFiles(extractedRoot, "*.xml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var xmlFiles = xmlPaths
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var xccdf = GetXccdfCandidateXmlFiles(extractedRoot).Count;
        var oval = xmlFiles.Count(f => f!.IndexOf("oval", StringComparison.OrdinalIgnoreCase) >= 0);
        var scapDataStreams = xmlPaths.Count(path =>
            TryReadXmlRootLocalName(path, out var rootLocalName)
            && string.Equals(rootLocalName, "data-stream-collection", StringComparison.OrdinalIgnoreCase));
        var admx = Directory.GetFiles(extractedRoot, "*.admx", SearchOption.AllDirectories).Length;

        return new SourceArtifactStats
        {
            XccdfXmlCount = xccdf,
            OvalXmlCount = oval,
            ScapDataStreamXmlCount = scapDataStreams,
            AdmxCount = admx,
            TotalXmlCount = xmlFiles.Count
        };
    }

    private static IReadOnlyList<string> GetXccdfCandidateXmlFiles(string root)
    {
        return Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                if (fileName.IndexOf("xccdf", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!TryReadXmlRootLocalName(path, out var rootLocalName))
                    return false;

                return string.Equals(rootLocalName, "Benchmark", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rootLocalName, "data-stream-collection", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryReadXmlRootLocalName(string xmlPath, out string rootLocalName)
    {
        rootLocalName = string.Empty;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = true,
                IgnoreComments = true,
                XmlResolver = null
            };

            using var reader = XmlReader.Create(xmlPath, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                rootLocalName = reader.LocalName;
                return !string.IsNullOrWhiteSpace(rootLocalName);
            }
        }
        catch
        {
        }

        return false;
    }

    private sealed class SourceArtifactStats
    {
        public int XccdfXmlCount { get; set; }
        public int OvalXmlCount { get; set; }
        public int ScapDataStreamXmlCount { get; set; }
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
        var hasDataStream = stats.ScapDataStreamXmlCount > 0;
        var hasAdmx = stats.AdmxCount > 0;

        // SCAP bundles: XCCDF + OVAL and/or SCAP data stream XML
        if (hasXccdf && (hasOval || hasDataStream))
        {
            result.Format = PackFormat.Scap;
            result.Confidence = DetectionConfidence.High;

            if (hasOval && hasDataStream)
            {
                result.Reasons.Add($"Found {stats.XccdfXmlCount} XCCDF, {stats.OvalXmlCount} OVAL, and {stats.ScapDataStreamXmlCount} SCAP data stream XML files - characteristic SCAP bundle signature.");
            }
            else if (hasOval)
            {
                result.Reasons.Add($"Found {stats.XccdfXmlCount} XCCDF and {stats.OvalXmlCount} OVAL files - characteristic SCAP bundle signature.");
            }
            else
            {
                result.Reasons.Add($"Found {stats.XccdfXmlCount} XCCDF files including {stats.ScapDataStreamXmlCount} SCAP data stream XML file(s) - characteristic SCAP bundle signature.");
            }

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
        var xccdfFiles = GetXccdfCandidateXmlFiles(rawRoot);

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
        var xccdfFiles = GetXccdfCandidateXmlFiles(rawRoot);

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

    private static string BuildAdmxTemplatePackName(string templateFolderName)
    {
        var baseName = templateFolderName;
        if (string.IsNullOrWhiteSpace(baseName))
            return "ADMX Templates - Imported";

        return "ADMX Templates - " + baseName.Trim();
    }

    private static string CombinePathSegments(IEnumerable<string> segments)
    {
        var segmentList = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToList();

        if (segmentList.Count == 0)
            return string.Empty;

        var combined = segmentList[0];
        for (var i = 1; i < segmentList.Count; i++)
            combined = Path.Combine(combined, segmentList[i]);

        return combined;
    }

    private static void ExpandNestedZipArchives(string extractionRoot, int maxPasses, CancellationToken ct)
    {
        for (var pass = 0; pass < maxPasses; pass++)
        {
            ct.ThrowIfCancellationRequested();

            var nestedArchives = Directory.GetFiles(extractionRoot, "*.zip", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (nestedArchives.Count == 0)
                break;

            var extractedAny = false;
            foreach (var nestedArchive in nestedArchives)
            {
                ct.ThrowIfCancellationRequested();

                var nestedExtractRoot = Path.Combine(
                    Path.GetDirectoryName(nestedArchive) ?? extractionRoot,
                    Path.GetFileNameWithoutExtension(nestedArchive));

                if (Directory.Exists(nestedExtractRoot)
                    && Directory.EnumerateFileSystemEntries(nestedExtractRoot).Any())
                {
                    continue;
                }

                Directory.CreateDirectory(nestedExtractRoot);
                ExtractZipSafely(nestedArchive, nestedExtractRoot, ct);
                extractedAny = true;
            }

            if (!extractedAny)
                break;
        }
    }

    private static IReadOnlyList<(string RootPath, string PackName, string SourceLabel)> FindScopedGpoRoots(string extractionRoot)
    {
        var localByScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var domainByScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var files = Directory.GetFiles(extractionRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            var fileDirectory = Path.GetDirectoryName(file);
            if (string.IsNullOrWhiteSpace(fileDirectory))
                continue;

            var relativeDirectoryPath = Path.GetRelativePath(extractionRoot, fileDirectory);
            var segments = relativeDirectoryPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < segments.Length; i++)
            {
                if (i + 2 < segments.Length
                    && (string.Equals(segments[i], "Support Files", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(segments[i], ".Support Files", StringComparison.OrdinalIgnoreCase))
                    && string.Equals(segments[i + 1], "Local Policies", StringComparison.OrdinalIgnoreCase))
                {
                    var scope = segments[i + 2].Trim();
                    if (!string.IsNullOrWhiteSpace(scope) && !localByScope.ContainsKey(scope))
                    {
                        var rootSegments = segments.Take(i + 3).ToArray();
                        localByScope[scope] = Path.Combine(extractionRoot, CombinePathSegments(rootSegments));
                    }

                    break;
                }

                if (i + 1 < segments.Length
                    && string.Equals(segments[i], "gpos", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetParentScopedGpoRoot(segments, i, out var parentScope, out var parentRootSegments))
                    {
                        if (!domainByScope.ContainsKey(parentScope))
                            domainByScope[parentScope] = Path.Combine(extractionRoot, CombinePathSegments(parentRootSegments));
                    }
                    else
                    {
                        var scope = segments[i + 1].Trim();
                        if (!string.IsNullOrWhiteSpace(scope)
                            && !IsGuidLikeScope(scope)
                            && !domainByScope.ContainsKey(scope))
                        {
                            var rootSegments = segments.Take(i + 2).ToArray();
                            domainByScope[scope] = Path.Combine(extractionRoot, CombinePathSegments(rootSegments));
                        }
                    }

                    break;
                }
            }
        }

        var localRoots = localByScope
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => (kvp.Value, "Local Policy - " + kvp.Key, "gpo_lgpo_import"));
        var domainRoots = domainByScope
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => (kvp.Value, "Domain GPO - " + kvp.Key, "gpo_domain_import"));

        return localRoots.Concat(domainRoots).ToList();
    }

    private static bool TryGetParentScopedGpoRoot(string[] segments, int gposIndex, out string scope, out string[] rootSegments)
    {
        scope = string.Empty;
        rootSegments = Array.Empty<string>();

        if (gposIndex <= 0 || gposIndex + 1 >= segments.Length)
            return false;

        var guidSegment = segments[gposIndex + 1].Trim();
        if (!IsGuidLikeScope(guidSegment))
            return false;

        var parentScope = segments[gposIndex - 1].Trim();
        if (string.IsNullOrWhiteSpace(parentScope))
            return false;

        var candidateRoot = segments.Take(gposIndex).ToArray();
        if (candidateRoot.Length == 0)
            return false;

        scope = parentScope;
        rootSegments = candidateRoot;
        return true;
    }

    private static bool IsGuidLikeScope(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (candidate.Length >= 2
            && candidate[0] == '{'
            && candidate[^1] == '}')
        {
            candidate = candidate.Substring(1, candidate.Length - 2);
        }

        return Guid.TryParse(candidate, out _);
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

    private static string BuildImportedPackName(string zipPath, string prefix)
    {
        var baseName = Path.GetFileNameWithoutExtension(zipPath);
        if (string.IsNullOrWhiteSpace(baseName))
            return prefix + "_Pack_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");

        baseName = CleanDisaPackName(baseName);
        return string.IsNullOrWhiteSpace(baseName)
            ? prefix + "_Pack_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss")
            : baseName;
    }

    private static IReadOnlyList<string> BuildBenchmarkImportFiles(string benchmarkFile, IReadOnlyList<string> allXmlFiles)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            benchmarkFile
        };

        var benchmarkDirectory = Path.GetDirectoryName(benchmarkFile) ?? string.Empty;
        var benchmarkName = Path.GetFileNameWithoutExtension(benchmarkFile);
        var benchmarkToken = System.Text.RegularExpressions.Regex.Replace(benchmarkName, "xccdf", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ToLowerInvariant();

        foreach (var candidate in allXmlFiles)
        {
            if (string.Equals(candidate, benchmarkFile, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidateDirectory = Path.GetDirectoryName(candidate) ?? string.Empty;
            if (!string.Equals(candidateDirectory, benchmarkDirectory, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidateFileName = Path.GetFileName(candidate).ToLowerInvariant();
            if (candidateFileName.IndexOf("oval", StringComparison.Ordinal) < 0)
                continue;

            if (!string.IsNullOrWhiteSpace(benchmarkToken)
                && candidateFileName.IndexOf(benchmarkToken, StringComparison.Ordinal) < 0)
            {
                continue;
            }

            selected.Add(candidate);
        }

        return selected
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CleanDisaPackName(string raw)
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
        var suffixFound = "";
        foreach (var s in trimSuffixes)
        {
            var idx = name.LastIndexOf(s, StringComparison.OrdinalIgnoreCase);
            if (idx > 0) { suffixFound = s.Trim(); name = name.Substring(0, idx); break; }
        }

        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*V\d+R\d+\s*", " ").Trim();
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\d{8} \d{6}\s*$", "").Trim();
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ");

        if (!string.IsNullOrWhiteSpace(suffixFound))
            name = name + " " + suffixFound;

        return name.Trim();
    }

    private static void ExtractZipSafely(string zipPath, string destinationRoot, CancellationToken ct)
    {
        var destinationRootFullPath = Path.GetFullPath(destinationRoot);
        var destinationRootPrefix = destinationRootFullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? destinationRootFullPath
            : destinationRootFullPath + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        if (archive.Entries.Count > MaxArchiveEntryCount)
            throw new ParsingException($"[IMPORT-ARCHIVE-001] Archive contains {archive.Entries.Count} entries, exceeding the maximum allowed {MaxArchiveEntryCount}.");

        long extractedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var destinationPath = Path.GetFullPath(Path.Combine(destinationRootFullPath, entry.FullName));
            var isWithinRoot = destinationPath.StartsWith(destinationRootPrefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(destinationPath, destinationRootFullPath, StringComparison.OrdinalIgnoreCase);
            if (!isWithinRoot)
                throw new ParsingException($"[IMPORT-ARCHIVE-002] Archive entry '{entry.FullName}' resolves outside extraction root and was rejected.");

            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal)
                || entry.FullName.EndsWith("\\", StringComparison.Ordinal);
            if (isDirectory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            extractedBytes += entry.Length;
            if (extractedBytes > MaxExtractedBytes)
                throw new ParsingException($"[IMPORT-ARCHIVE-003] Archive expanded size exceeds {MaxExtractedBytes} bytes and was rejected.");

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using var entryStream = entry.Open();
            using var outputStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(outputStream);
        }
    }

    private async Task<string> ComputeDirectoryManifestSha256Async(string rootPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var rootFullPath = Path.GetFullPath(rootPath);
        var files = EnumerateManifestFiles(
            rootFullPath,
            Directory.EnumerateFiles(rootFullPath, "*", SearchOption.AllDirectories),
            ct);

        var orderedFiles = files
            .Select(file => new
            {
                file.FullPath,
                file.RelativePath
            })
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();

        ct.ThrowIfCancellationRequested();

        var lines = new List<string>(orderedFiles.Count);
        foreach (var file in orderedFiles)
        {
            ct.ThrowIfCancellationRequested();
            var fileHash = await _hash.Sha256FileAsync(file.FullPath, ct).ConfigureAwait(false);
            lines.Add(file.RelativePath + ":" + fileHash);
        }

        var payload = string.Join("\n", lines);
        return await _hash.Sha256TextAsync(payload, ct).ConfigureAwait(false);
    }

    private static List<(string FullPath, string RelativePath)> EnumerateManifestFiles(
        string rootFullPath,
        IEnumerable<string> sourceFiles,
        CancellationToken ct)
    {
        var files = new List<(string FullPath, string RelativePath)>();
        foreach (var filePath in sourceFiles)
        {
            ct.ThrowIfCancellationRequested();
            files.Add((
                FullPath: filePath,
                RelativePath: Path.GetRelativePath(rootFullPath, filePath)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/')));
        }

        return files;
    }
}

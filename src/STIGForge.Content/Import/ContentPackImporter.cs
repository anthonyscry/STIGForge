using System.Text.Json;
using STIGForge.Core;
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

    private readonly FormatDetector _formatDetector;
    private readonly ImportZipHandler _zipHandler;
    private readonly FormatSpecificImporter _formatSpecificImporter;
    private readonly ImportProcessingHandler _processingHandler;
    private readonly ImportNameResolver _nameResolver;
    private readonly ImportManifestBuilder _manifestBuilder;
    public ContentPackImporter(IPathBuilder paths, IHashingService hash, IContentPackRepository packs, IControlRepository controls, IAuditTrailService? audit = null)
    {
        _paths = paths;
        _hash = hash;
        _packs = packs;
        _controls = controls;
        _audit = audit;
        _formatDetector = new FormatDetector();
        _zipHandler = new ImportZipHandler();
        _formatSpecificImporter = new FormatSpecificImporter(_formatDetector);
        _nameResolver = new ImportNameResolver();
        _manifestBuilder = new ImportManifestBuilder(_hash);
        _processingHandler = new ImportProcessingHandler(_formatDetector, _formatSpecificImporter, _packs, _controls, _manifestBuilder);
    }

    public List<ImportCheckpoint> FindIncompleteImports()
    {
        var incomplete = new List<ImportCheckpoint>();

        var packsRoot = Path.GetDirectoryName(_paths.GetPackRoot("dummy"))!;
        if (!Directory.Exists(packsRoot))
            return incomplete;

        var packDirs = Directory.EnumerateDirectories(packsRoot);
        foreach (var packDir in packDirs)
        {
            var checkpoint = ImportCheckpoint.Load(packDir);
            if (checkpoint != null
                && checkpoint.Stage != ImportStage.Complete
                && checkpoint.Stage != ImportStage.Failed)
            {
                incomplete.Add(checkpoint);
            }
        }

        return incomplete;
    }

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
            await _zipHandler.ExtractZipSafelyAsync(consolidatedZipPath, extractionRoot, ct).ConfigureAwait(false);

            var nestedZipPaths = Directory
                .EnumerateFiles(extractionRoot, "*.zip", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (nestedZipPaths.Count == 0)
            {
                var scopedRoots = _zipHandler.FindScopedGpoRoots(extractionRoot);
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

                var xccdfFiles = _formatDetector.GetXccdfCandidateXmlFiles(extractionRoot);
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
                            var packName = _nameResolver.CleanDisaPackName(dirName);
                            if (string.IsNullOrWhiteSpace(packName))
                                packName = dirName;
                            return await ImportDirectoryAsPackAsync(benchDir, packName, sourceLabel, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }).ToList();

                    return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
                }

                if (xccdfFiles.Count > 1)
                {
                    var allXmlFiles = Directory.EnumerateFiles(extractionRoot, "*.xml", SearchOption.AllDirectories)
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

                                var packName = _nameResolver.BuildImportedPackName(benchmarkFile, "Imported");
                                return await ImportDirectoryAsPackAsync(tempRoot, packName, sourceLabel, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                try { Directory.Delete(tempRoot, true); } catch (Exception) { }
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }).ToList();

                    return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
                }

                var singlePackName = _nameResolver.BuildImportedPackName(consolidatedZipPath, "Imported");
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
                    var packName = _nameResolver.BuildImportedPackName(nestedZipPath, "Imported");
                    return await ImportZipAsync(nestedZipPath, packName, sourceLabel, ct).ConfigureAwait(false);
                }
                finally
                {
                    zipSemaphore.Release();
                }
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
            await _zipHandler.ExtractZipSafelyAsync(zipPath, extractionRoot, ct).ConfigureAwait(false);
            await _zipHandler.ExpandNestedZipArchivesAsync(extractionRoot, maxPasses: 2, ct).ConfigureAwait(false);

            var admxGroups = Directory.EnumerateFiles(extractionRoot, "*.admx", SearchOption.AllDirectories)
                .Select(path => new
                {
                    FullPath = path,
                    Segments = _zipHandler.GetRelativePathCompat(extractionRoot, path)
                        .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                })
                .Select(entry => new
                {
                    entry.FullPath,
                    entry.Segments,
                    ScopeIndex = FindSegmentIndex(entry.Segments, "ADMX Templates")
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

                        var relativeInFolder = _zipHandler.CombinePathSegments(admxFile.Segments.Skip(admxFile.ScopeIndex + 2));
                        var destination = Path.Combine(templateRoot, relativeInFolder);
                        var destinationDir = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrWhiteSpace(destinationDir))
                            Directory.CreateDirectory(destinationDir);

                        File.Copy(admxFile.FullPath, destination, true);
                    }

                    var packName = _nameResolver.BuildAdmxTemplatePackName(admxGroup.FolderName);
                    var pack = await ImportDirectoryAsPackAsync(templateRoot, packName, sourceLabel, ct).ConfigureAwait(false);
                    imported.Add(pack);
                }
                finally
                {
                    try { Directory.Delete(templateRoot, true); } catch (Exception) { }
                }
            }

            return imported;
        }
        finally
        {
            try { Directory.Delete(extractionRoot, true); } catch (Exception) { }
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
            foreach (var file in Directory.EnumerateFiles(extractedDir, "*", SearchOption.AllDirectories))
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

            var directoryManifestHash = await _manifestBuilder.ComputeDirectoryManifestSha256Async(rawRoot, ct).ConfigureAwait(false);

            var existingPack = await _packs.GetByManifestHashAsync(directoryManifestHash, ct).ConfigureAwait(false);
            if (existingPack != null)
                return existingPack;

            var pack = new ContentPack
            {
                PackId = packId,
                Name = packName,
                ImportedAt = DateTimeOffset.Now,
                ReleaseDate = _nameResolver.GuessReleaseDate(extractedDir, packName),
                SourceLabel = sourceLabel,
                HashAlgorithm = "sha256",
                ManifestSha256 = directoryManifestHash,
                SchemaVersion = CanonicalContract.Version
            };

            var processing = await _processingHandler.ParseValidateAndPersistAsync(
                pack,
                rawRoot,
                packName,
                checkpoint,
                packRoot,
                includeConflictDetails: false,
                ct).ConfigureAwait(false);

            var note = new
            {
                importedFrom = extractedDir,
                schemaVersion = CanonicalContract.Version,
                detectedFormat = processing.Format.ToString(),
                parsedControls = processing.ParsedCount,
                timestamp = DateTimeOffset.Now
            };
            await File.WriteAllTextAsync(
                Path.Combine(packRoot, "import_note.json"),
                JsonSerializer.Serialize(note, JsonOptions.Indented),
                ct).ConfigureAwait(false);

            var compatibility = processing.Compatibility;
            await File.WriteAllTextAsync(
                Path.Combine(packRoot, "compatibility_matrix.json"),
                JsonSerializer.Serialize(compatibility, JsonOptions.Indented),
                ct).ConfigureAwait(false);

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
                        Detail = $"PackId={packId}, Format={processing.Format}, Controls={processing.ParsedCount}, Source=directory",
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
            await _zipHandler.ExtractZipSafelyAsync(zipPath, rawRoot, ct).ConfigureAwait(false);

            checkpoint.Stage = ImportStage.Parsing;
            checkpoint.Save(packRoot);

            var zipHash = await _hash.Sha256FileAsync(zipPath, ct).ConfigureAwait(false);
            var pack = new ContentPack
            {
                PackId = packId,
                Name = packName,
                ImportedAt = DateTimeOffset.Now,
                ReleaseDate = _nameResolver.GuessReleaseDate(zipPath, packName),
                SourceLabel = sourceLabel,
                HashAlgorithm = "sha256",
                ManifestSha256 = zipHash,
                SchemaVersion = CanonicalContract.Version
            };

            var processing = await _processingHandler.ParseValidateAndPersistAsync(
                pack,
                rawRoot,
                packName,
                checkpoint,
                packRoot,
                includeConflictDetails: true,
                ct).ConfigureAwait(false);

            var note = new
            {
                importedZip = Path.GetFileName(zipPath),
                zipHash,
                schemaVersion = CanonicalContract.Version,
                detectedFormat = processing.Format.ToString(),
                parsedControls = processing.ParsedCount,
                timestamp = DateTimeOffset.Now
            };

            var notePath = Path.Combine(packRoot, "import_note.json");
            Directory.CreateDirectory(packRoot);
            await File.WriteAllTextAsync(notePath, JsonSerializer.Serialize(note, JsonOptions.Indented), ct).ConfigureAwait(false);

            var compatibility = processing.Compatibility;
            var compatibilityPath = Path.Combine(packRoot, "compatibility_matrix.json");
            await File.WriteAllTextAsync(compatibilityPath, JsonSerializer.Serialize(compatibility, JsonOptions.Indented), ct).ConfigureAwait(false);

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
                        Detail = $"PackId={packId}, Format={processing.Format}, Controls={processing.ParsedCount}",
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

    private static int FindSegmentIndex(IReadOnlyList<string> segments, string value)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            if (string.Equals(segments[i], value, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}

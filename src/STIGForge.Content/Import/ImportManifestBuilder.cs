using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Content.Models;

namespace STIGForge.Content.Import;

internal sealed class ImportManifestBuilder
{
    private readonly IHashingService _hash;

    internal ImportManifestBuilder(IHashingService hash)
    {
        _hash = hash;
    }

    internal async Task<string> ComputeDirectoryManifestSha256Async(string rootPath, CancellationToken ct)
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

    internal object BuildCompatibilityMatrix(
        FormatDetectionResult formatResult,
        int parsedControls,
        SourceArtifactStats sourceStats,
        bool usedFallbackParser,
        List<ParsingError> parsingErrors,
        List<ControlConflict> conflicts)
    {
        var format = formatResult.Format;
        var supportsXccdf = format is PackFormat.Stig or PackFormat.Scap;
        var supportsOvalMetadata = format is PackFormat.Scap;
        var supportsAdmx = format is PackFormat.Gpo;

        var lossy = new List<string>();
        var unsupported = new List<string>();

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

        if (sourceStats.OvalXmlCount > 0 && format != PackFormat.Scap)
            unsupported.Add($"OVAL XML files present ({sourceStats.OvalXmlCount}) but selected format does not support OVAL processing.");

        if (sourceStats.AdmxCount > 0 && format != PackFormat.Gpo)
            unsupported.Add($"ADMX files present ({sourceStats.AdmxCount}) but selected format does not support ADMX processing.");

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

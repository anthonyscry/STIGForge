using System.IO.Compression;
using STIGForge.Content.Models;

namespace STIGForge.Content.Import;

internal sealed class ImportZipHandler
{
    private const int MaxArchiveEntryCount = 4096;
    private const long MaxExtractedBytes = 512L * 1024L * 1024L;

    internal void ExtractZipSafely(string zipPath, string destinationRoot, CancellationToken ct)
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

    internal void ExpandNestedZipArchives(string extractionRoot, int maxPasses, CancellationToken ct)
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

    internal IReadOnlyList<(string RootPath, string PackName, string SourceLabel)> FindScopedGpoRoots(string extractionRoot)
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

            var relativeDirectoryPath = GetRelativePathCompat(extractionRoot, fileDirectory);
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

    internal string GetRelativePathCompat(string relativeTo, string path)
    {
        if (string.IsNullOrWhiteSpace(relativeTo))
            return path ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var relativeRoot = Path.GetFullPath(relativeTo);
        var targetPath = Path.GetFullPath(path);

        if (!relativeRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            && !relativeRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            relativeRoot += Path.DirectorySeparatorChar;
        }

        var relativeRootUri = new Uri(relativeRoot, UriKind.Absolute);
        var targetPathUri = new Uri(targetPath, UriKind.Absolute);
        if (!string.Equals(relativeRootUri.Scheme, targetPathUri.Scheme, StringComparison.OrdinalIgnoreCase))
            return path;

        var relative = Uri.UnescapeDataString(relativeRootUri.MakeRelativeUri(targetPathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);

        return relative;
    }

    internal string CombinePathSegments(IEnumerable<string> segments)
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

    private static bool TryGetParentScopedGpoRoot(string[] segments, int gposIndex, out string scope, out string[] rootSegments)
    {
        scope = string.Empty;
        rootSegments = [];

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
}

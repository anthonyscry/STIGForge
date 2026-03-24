using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;

namespace STIGForge.Evidence;

/// <summary>
/// Compiles raw evidence artifacts from disk into FINDING_DETAILS (machine-grade) and
/// COMMENTS (human-grade) text for CKL export. Builds and caches the evidence index
/// per bundleRoot on first call. Cache is process-scoped (singleton lifetime in DI).
/// If evidence artifacts change mid-process, create a new instance to pick up changes.
/// </summary>
public sealed class EvidenceCompiler : IEvidenceCompiler
{
    private const int MaxArtifactChars = 4000;
    private const string TruncationMarker = "[truncated]";
    private const int MaxEvidenceEntries = 50;
    private const int MaxTotalOutputChars = 100_000;

    private readonly ConcurrentDictionary<string, EvidenceIndex?> _indexCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<EvidenceCompiler>? _logger;

    public EvidenceCompiler(ILogger<EvidenceCompiler>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public CompiledEvidence? CompileEvidence(EvidenceCompilationInput input, string bundleRoot)
    {
        if (string.IsNullOrWhiteSpace(input.VulnId) && string.IsNullOrWhiteSpace(input.RuleId))
            return null;

        try
        {
            var index = GetOrBuildIndex(bundleRoot);
            if (index == null)
                return null;

            var entries = CollectEntries(index, input.VulnId, input.RuleId);
            if (entries.Count == 0)
                return null;

            var findingDetails = BuildFindingDetails(input, entries, bundleRoot);
            var artifactFileNames = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.RelativePath))
                .Select(e => Path.GetFileName(e.RelativePath))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Use the first entry as the key evidence summary
            var firstEntry = entries[0];
            var keyEvidence = !string.IsNullOrWhiteSpace(firstEntry.Title)
                ? firstEntry.Title
                : null;

            var comments = CommentTemplateEngine.Generate(
                status: input.Status,
                keyEvidence: keyEvidence,
                toolName: input.Tool,
                verifiedAt: input.VerifiedAt,
                artifactFileNames: artifactFileNames);

            return new CompiledEvidence(findingDetails, comments);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "EvidenceCompiler failed to compile evidence for VulnId={VulnId} RuleId={RuleId}", input.VulnId, input.RuleId);
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private EvidenceIndex? GetOrBuildIndex(string bundleRoot)
    {
        var index = _indexCache.GetOrAdd(bundleRoot, root =>
        {
            try
            {
                var svc = new EvidenceIndexService(root);
                return Task.Run(() => svc.BuildIndexAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "EvidenceCompiler failed to build evidence index for bundleRoot={BundleRoot}", root);
                return null;
            }
        });

        // Do not permanently cache null (transient failures should be retryable).
        if (index == null)
            _indexCache.TryRemove(bundleRoot, out _);

        return index;
    }

    private static List<EvidenceIndexEntry> CollectEntries(EvidenceIndex index, string? vulnId, string? ruleId)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<EvidenceIndexEntry>();

        if (!string.IsNullOrWhiteSpace(vulnId))
        {
            foreach (var entry in EvidenceIndexService.GetEvidenceForControl(index, vulnId))
            {
                if (seen.Add(entry.EvidenceId))
                    result.Add(entry);
            }
        }

        if (!string.IsNullOrWhiteSpace(ruleId))
        {
            foreach (var entry in EvidenceIndexService.GetEvidenceForControl(index, ruleId))
            {
                if (seen.Add(entry.EvidenceId))
                    result.Add(entry);
            }
        }

        return result;
    }

    private string BuildFindingDetails(
        EvidenceCompilationInput input,
        List<EvidenceIndexEntry> entries,
        string bundleRoot)
    {
        var sb = new StringBuilder();
        var compiledAt = DateTimeOffset.UtcNow.ToString("o");

        // Header
        sb.AppendLine("=== STIGForge Evidence Report ===");
        sb.AppendFormat("Control: {0} ({1})", input.VulnId ?? "(none)", input.RuleId ?? "(none)");
        sb.AppendLine();
        sb.AppendFormat("Compiled: {0}", compiledAt);
        sb.AppendLine();

        // Raw Evidence
        sb.AppendLine();
        sb.AppendLine("--- Raw Evidence ---");
        var processedEntries = 0;
        foreach (var entry in entries)
        {
            if (processedEntries >= MaxEvidenceEntries || sb.Length >= MaxTotalOutputChars)
            {
                sb.AppendLine($"[Output truncated: {entries.Count - processedEntries} additional entries omitted]");
                break;
            }

            sb.AppendFormat("[{0}] Collected: {1} (Source: {2})", entry.Type, entry.TimestampUtc, entry.Source);
            sb.AppendLine();

            var content = ReadArtifactContent(entry, bundleRoot);
            if (content != null)
            {
                sb.AppendLine(content);
            }

            sb.AppendLine();
            processedEntries++;
        }

        // Apply History
        var applyEntries = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.StepName))
            .ToList();

        if (applyEntries.Count > 0)
        {
            sb.AppendLine("--- Apply History ---");
            foreach (var entry in applyEntries)
            {
                sb.AppendFormat("Step: {0}", entry.StepName);
                sb.AppendLine();
                sb.AppendFormat("Applied: {0}", entry.TimestampUtc);
                sb.AppendLine();
                sb.AppendLine();
            }
        }

        // Verification
        sb.AppendLine("--- Verification ---");
        sb.AppendFormat("Tool: {0}", input.Tool ?? "(none)");
        sb.AppendLine();
        sb.AppendFormat("Scanned: {0}", input.VerifiedAt?.ToString("o") ?? "(none)");
        sb.AppendLine();
        sb.AppendFormat("Result: {0}", input.Status ?? "(none)");
        sb.AppendLine();

        return sb.ToString();
    }

    private string? ReadArtifactContent(EvidenceIndexEntry entry, string bundleRoot)
    {
        if (string.IsNullOrWhiteSpace(entry.RelativePath))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(bundleRoot, "Evidence", entry.RelativePath));
            var allowedPath = Path.GetFullPath(Path.Combine(bundleRoot, "Evidence")) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase))
                return null;

            if (!File.Exists(fullPath))
                return null;

            // Read only what we need to avoid loading arbitrarily large artifacts into memory.
            using var reader = new StreamReader(fullPath);
            var buffer = new char[MaxArtifactChars];
            int charsRead = reader.Read(buffer, 0, buffer.Length);

            if (charsRead < MaxArtifactChars)
                return new string(buffer, 0, charsRead);

            return new string(buffer, 0, charsRead) + TruncationMarker;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "EvidenceCompiler failed to read artifact for EvidenceId={EvidenceId} RelativePath={RelativePath}", entry.EvidenceId, entry.RelativePath);
            return null;
        }
    }
}

using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using STIGForge.Core.Abstractions;

namespace STIGForge.Cli.Commands;

/// <summary>Downloads, extracts, and caches source archives for air-gap transfer scenarios.</summary>
internal static class AirGapDownloadService
{
    private const int MaxArchiveEntryCount = 4096;
    private const long MaxArchiveExtractedBytes = 512 * 1024 * 1024; // 512 MB

    public static string GetAirGapTransferRoot(IPathBuilder paths)
    {
        var root = Path.Combine(paths.GetAppDataRoot(), "airgap-transfer");
        Directory.CreateDirectory(root);
        return root;
    }

    public static void ExtractZipSafely(string zipPath, string destinationRoot)
    {
        var destFull = Path.GetFullPath(destinationRoot);
        var destPrefix = destFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? destFull
            : destFull + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        if (archive.Entries.Count > MaxArchiveEntryCount)
            throw new InvalidOperationException(
                $"Archive contains {archive.Entries.Count} entries, exceeding maximum {MaxArchiveEntryCount}.");

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var entryPath = Path.GetFullPath(Path.Combine(destFull, entry.FullName));
            if (!entryPath.StartsWith(destPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(entryPath, destFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Archive entry '{entry.FullName}' resolves outside extraction root.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)
                || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(entryPath);
                continue;
            }

            var dir = Path.GetDirectoryName(entryPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            using var src = entry.Open();
            using var dst = new FileStream(entryPath, FileMode.Create, FileAccess.Write, FileShare.None);
            src.CopyTo(dst);
            totalBytes += dst.Length;

            if (totalBytes > MaxArchiveExtractedBytes)
                throw new InvalidOperationException(
                    $"Archive expanded size exceeds {MaxArchiveExtractedBytes} bytes.");
        }
    }

    public static async Task<string> DownloadSourceZipAsync(
        string sourceUrl, string sourceName, string airGapTransferRoot, CancellationToken ct)
    {
        var resolvedUrl = await ResolveDownloadUrlAsync(sourceUrl, ct).ConfigureAwait(false);

        if (!resolvedUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && !resolvedUrl.Contains(".zip?", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Source URL must resolve to a .zip file: " + resolvedUrl);

        if (!Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException("Invalid source URL: " + sourceUrl);

        var downloadRoot = CreateDownloadSessionFolder(airGapTransferRoot, sourceName);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = sourceName + ".zip";
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) fileName += ".zip";

        var destination = Path.Combine(downloadRoot, fileName);
        using var http = CreateHttpClient();
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var remote = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var local = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await remote.CopyToAsync(local, ct).ConfigureAwait(false);

        return destination;
    }

    public static async Task<(string? ModulePath, string? ArchivePath)> DownloadAndExtractPowerStigModuleAsync(
        string sourceUrl, string airGapTransferRoot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return (null, null);

        var zipPath = await DownloadSourceZipAsync(sourceUrl, "powerstig", airGapTransferRoot, ct).ConfigureAwait(false);
        var extractRoot = CreateDownloadSessionFolder(airGapTransferRoot, "powerstig-extracted");
        Directory.CreateDirectory(extractRoot);
        ExtractZipSafely(zipPath, extractRoot);

        var psd1Candidates = Directory
            .GetFiles(extractRoot, "*.psd1", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Equals("PowerSTIG.psd1", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .ToList();

        return psd1Candidates.Count == 0 ? (null, zipPath) : (psd1Candidates[0], zipPath);
    }

    private static async Task<string> ResolveDownloadUrlAsync(string sourceUrl, CancellationToken ct)
    {
        var raw = (sourceUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Source URL is required.");

        if (raw.Contains("github.com/microsoft/PowerStig", StringComparison.OrdinalIgnoreCase)
            && !raw.Contains(".zip", StringComparison.OrdinalIgnoreCase))
            return "https://github.com/microsoft/PowerStig/archive/refs/heads/master.zip";

        if (raw.Contains("github.com/niwc-atlantic/scap-content-library", StringComparison.OrdinalIgnoreCase)
            && !raw.Contains(".zip", StringComparison.OrdinalIgnoreCase))
            return "https://github.com/niwc-atlantic/scap-content-library/archive/refs/heads/main.zip";

        if (raw.Contains("cyber.mil/stigs/downloads", StringComparison.OrdinalIgnoreCase)
            && !raw.Contains(".zip", StringComparison.OrdinalIgnoreCase))
            return await ResolveFirstZipFromHtmlAsync(raw, ct).ConfigureAwait(false);

        return raw;
    }

    private static async Task<string> ResolveFirstZipFromHtmlAsync(string pageUrl, CancellationToken ct)
    {
        using var http = CreateHttpClient();
        var html = await http.GetStringAsync(pageUrl, ct).ConfigureAwait(false);
        var regex = new Regex(
            "href\\s*=\\s*[\"'](?<u>[^\"'#>]+?\\.zip(?:\\?[^\"'#>]*)?)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var baseUri = new Uri(pageUrl);
        var links = new List<string>();

        foreach (Match match in regex.Matches(html))
        {
            var candidate = match.Groups["u"].Value;
            if (!string.IsNullOrWhiteSpace(candidate) && Uri.TryCreate(baseUri, candidate, out var absolute))
                links.Add(absolute.ToString());
        }

        if (links.Count == 0)
            throw new ArgumentException("No downloadable .zip links found at source page: " + pageUrl);

        var scapPreferred = links.FirstOrDefault(l => l.IndexOf("scap", StringComparison.OrdinalIgnoreCase) >= 0);
        return scapPreferred
            ?? links.FirstOrDefault()
            ?? throw new ArgumentException("Unable to resolve absolute .zip links from source page: " + pageUrl);
    }

    private static string CreateDownloadSessionFolder(string airGapTransferRoot, string sourceName)
    {
        var safeSourceName = SanitizeFileSegment(sourceName);
        var session = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var folder = Path.Combine(airGapTransferRoot, safeSourceName, session);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string SanitizeFileSegment(string value)
    {
        var input = string.IsNullOrWhiteSpace(value) ? "source" : value.Trim();
        var chars = input.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars);
        return string.IsNullOrWhiteSpace(sanitized) ? "source" : sanitized;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("STIGForge/1.0 (+mission-autopilot)");
        return client;
    }
}

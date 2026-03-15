using System.Security.Cryptography;

namespace STIGForge.Core.Services;

/// <summary>
/// Verifies the SHA-256 hash manifest written by BundleBuilder to detect
/// tampered bundle files before apply or orchestration runs.
/// </summary>
public static class BundleIntegrityVerifier
{
    /// <summary>
    /// Reads <c>Manifest/file_hashes.sha256</c> inside <paramref name="bundleRoot"/>
    /// and re-hashes every listed file. Throws <see cref="InvalidOperationException"/>
    /// if the manifest is missing or any file hash does not match.
    /// </summary>
    public static async Task VerifyAsync(string bundleRoot, CancellationToken ct)
    {
        var manifestPath = Path.Combine(bundleRoot, "Manifest", "file_hashes.sha256");

        if (!File.Exists(manifestPath))
            throw new InvalidOperationException(
                $"Bundle integrity manifest not found at '{manifestPath}'. " +
                "The bundle may be incomplete or was built with an older version of STIGForge.");

        var lines = await File.ReadAllLinesAsync(manifestPath, ct).ConfigureAwait(false);
        var mismatches = new List<string>();

        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();

            // Format: "<sha256hex>  <relative-path>" (two spaces, sha256sum convention)
            var spaceIdx = line.IndexOf("  ", StringComparison.Ordinal);
            if (spaceIdx < 0) continue;

            var expectedHash = line[..spaceIdx].Trim().ToLowerInvariant();
            var relativePath = line[(spaceIdx + 2)..].Trim();

            if (string.IsNullOrWhiteSpace(expectedHash) || string.IsNullOrWhiteSpace(relativePath))
                continue;

            // Normalise path separators written on Windows when verifying on Linux (and vice versa)
            relativePath = relativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            var filePath = Path.Combine(bundleRoot, relativePath);

            if (!File.Exists(filePath))
            {
                mismatches.Add($"MISSING: {relativePath}");
                continue;
            }

            var actualHash = await ComputeSha256Async(filePath, ct).ConfigureAwait(false);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                mismatches.Add($"TAMPERED: {relativePath} (expected {expectedHash[..8]}…, actual {actualHash[..8]}…)");
        }

        if (mismatches.Count > 0)
            throw new InvalidOperationException(
                $"Bundle integrity check failed — {mismatches.Count} file(s) have been modified or removed:\n" +
                string.Join("\n", mismatches));
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var fs = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        var bytes = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

using System.Text.Json;

namespace STIGForge.Apply;

/// <summary>
/// Tracks completed apply operations to enable idempotent retries.
/// Prevents re-execution of already-successful steps during resume/retry scenarios.
/// </summary>
public sealed class IdempotencyTracker
{
    private readonly string _trackerPath;
    private IdempotencyState _state;

    public IdempotencyTracker(string bundleRoot)
    {
        var applyRoot = Path.Combine(bundleRoot, "Apply");
        Directory.CreateDirectory(applyRoot);
        _trackerPath = Path.Combine(applyRoot, "idempotency_tracker.json");
        _state = Load();
    }

    /// <summary>
    /// Checks if an operation with the given key has already been completed successfully.
    /// </summary>
    public bool IsCompleted(string operationKey)
    {
        return _state.CompletedOperations.ContainsKey(operationKey);
    }

    /// <summary>
    /// Marks an operation as completed with a fingerprint for verification.
    /// Fingerprint should be a hash of operation inputs to detect changes.
    /// </summary>
    public void MarkCompleted(string operationKey, string fingerprint, string description)
    {
        _state.CompletedOperations[operationKey] = new CompletedOperation
        {
            Key = operationKey,
            Fingerprint = fingerprint,
            Description = description,
            CompletedAt = DateTimeOffset.Now
        };
        Save();
    }

    /// <summary>
    /// Checks if an operation's fingerprint matches the recorded fingerprint.
    /// Returns false if operation not found or fingerprint differs (indicating inputs changed).
    /// </summary>
    public bool FingerprintMatches(string operationKey, string currentFingerprint)
    {
        if (!_state.CompletedOperations.TryGetValue(operationKey, out var op))
            return false;

        return string.Equals(op.Fingerprint, currentFingerprint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Clears all completed operations - use for fresh apply runs.
    /// </summary>
    public void Reset()
    {
        _state = new IdempotencyState
        {
            CreatedAt = DateTimeOffset.Now,
            CompletedOperations = new Dictionary<string, CompletedOperation>()
        };
        Save();
    }

    /// <summary>
    /// Gets summary of completed operations for logging/reporting.
    /// </summary>
    public IReadOnlyList<CompletedOperation> GetCompletedOperations()
    {
        return _state.CompletedOperations.Values.OrderBy(op => op.CompletedAt).ToList();
    }

    private IdempotencyState Load()
    {
        if (!File.Exists(_trackerPath))
        {
            return new IdempotencyState
            {
                CreatedAt = DateTimeOffset.Now,
                CompletedOperations = new Dictionary<string, CompletedOperation>()
            };
        }

        try
        {
            var json = File.ReadAllText(_trackerPath);
            return JsonSerializer.Deserialize<IdempotencyState>(json) ?? new IdempotencyState
            {
                CreatedAt = DateTimeOffset.Now,
                CompletedOperations = new Dictionary<string, CompletedOperation>()
            };
        }
        catch
        {
            // Corrupted tracker - start fresh
            return new IdempotencyState
            {
                CreatedAt = DateTimeOffset.Now,
                CompletedOperations = new Dictionary<string, CompletedOperation>()
            };
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
        
        // Atomic write pattern
        var tempPath = _trackerPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(_trackerPath))
            File.Delete(_trackerPath);
        File.Move(tempPath, _trackerPath);
    }

    private sealed class IdempotencyState
    {
        public DateTimeOffset CreatedAt { get; set; }
        public Dictionary<string, CompletedOperation> CompletedOperations { get; set; } = new();
    }
}

public sealed class CompletedOperation
{
    public string Key { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; }
}

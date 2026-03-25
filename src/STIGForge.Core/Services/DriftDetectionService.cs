using System.Text;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class DriftDetectionService
{
  private readonly IDriftRepository _repo;
  private readonly IClock _clock;
  private readonly IReadOnlyDictionary<string, IRemediationHandler> _remediationHandlers;

  public DriftDetectionService(
    IDriftRepository repo,
    IEnumerable<IRemediationHandler>? remediationHandlers = null,
    IClock? clock = null)
  {
    _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    _clock = clock ?? new SystemClock();
    _remediationHandlers = (remediationHandlers ?? [])
      .GroupBy(h => h.RuleId, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
  }

  public async Task<DriftCheckResult> CheckBundleAsync(string bundleRoot, bool autoRemediate, CancellationToken ct)
  {
    var currentComplianceState = LoadCurrentComplianceState(bundleRoot);
    return await CheckAsync(bundleRoot, currentComplianceState, autoRemediate, ct).ConfigureAwait(false);
  }

  public async Task<DriftCheckResult> CheckAsync(
    string bundleRoot,
    IReadOnlyDictionary<string, string> currentComplianceState,
    bool autoRemediate,
    CancellationToken ct)
  {
    if (string.IsNullOrEmpty(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));
    if (currentComplianceState is null) throw new ArgumentNullException(nameof(currentComplianceState));

    var normalizedCurrent = NormalizeStateMap(currentComplianceState);
    var latestByRule = (await _repo.GetLatestByRuleAsync(bundleRoot, ct).ConfigureAwait(false))
      .ToDictionary(s => s.RuleId, s => s, StringComparer.OrdinalIgnoreCase);

    var detectedAt = _clock.Now;
    var driftEvents = new List<DriftSnapshot>();

    foreach (var kvp in normalizedCurrent.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
    {
      DriftSnapshot? previous;
      if (!latestByRule.TryGetValue(kvp.Key, out previous))
      {
        driftEvents.Add(new DriftSnapshot
        {
          SnapshotId = Guid.NewGuid().ToString("N"),
          BundleRoot = bundleRoot,
          RuleId = kvp.Key,
          PreviousState = null,
          CurrentState = kvp.Value,
          ChangeType = DriftChangeTypes.BaselineEstablished,
          DetectedAt = detectedAt
        });
        continue;
      }

      var previousState = NormalizeStatus(previous.CurrentState);
      if (!string.Equals(previousState, kvp.Value, StringComparison.Ordinal))
      {
        driftEvents.Add(new DriftSnapshot
        {
          SnapshotId = Guid.NewGuid().ToString("N"),
          BundleRoot = bundleRoot,
          RuleId = kvp.Key,
          PreviousState = previousState,
          CurrentState = kvp.Value,
          ChangeType = DriftChangeTypes.StateChanged,
          DetectedAt = detectedAt
        });
      }
    }

    foreach (var kvp in latestByRule.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
    {
      if (normalizedCurrent.ContainsKey(kvp.Key))
        continue;

      driftEvents.Add(new DriftSnapshot
      {
        SnapshotId = Guid.NewGuid().ToString("N"),
        BundleRoot = bundleRoot,
        RuleId = kvp.Key,
        PreviousState = NormalizeStatus(kvp.Value.CurrentState),
        CurrentState = "Missing",
        ChangeType = DriftChangeTypes.MissingInCurrentScan,
        DetectedAt = detectedAt
      });
    }

    if (driftEvents.Count > 0)
      await _repo.SaveBatchAsync(driftEvents, ct).ConfigureAwait(false);

    var remediatedRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var remediationErrors = new List<string>();

    if (autoRemediate)
      await AutoRemediateAsync(bundleRoot, driftEvents, remediatedRules, remediationErrors, ct).ConfigureAwait(false);

    return new DriftCheckResult
    {
      BundleRoot = bundleRoot,
      CheckedAt = detectedAt,
      CurrentRuleCount = normalizedCurrent.Count,
      BaselineRuleCount = latestByRule.Count,
      DriftEvents = driftEvents,
      AutoRemediatedRuleIds = remediatedRules.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList(),
      RemediationErrors = remediationErrors
    };
  }

  public Task<IReadOnlyList<DriftSnapshot>> GetHistoryAsync(string bundleRoot, string? ruleId, int limit, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));
    if (limit < 1)
      limit = 100;

    return _repo.GetDriftHistoryAsync(bundleRoot, ruleId, limit, ct);
  }

  public IDisposable SchedulePeriodicChecks(
    string bundleRoot,
    TimeSpan interval,
    Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> currentComplianceStateProvider,
    bool autoRemediate = false,
    Action<DriftCheckResult>? onCompleted = null,
    Action<Exception>? onError = null,
    CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));
    if (interval <= TimeSpan.Zero) throw new ArgumentException("Value must be greater than TimeSpan.Zero.", nameof(interval));
    if (currentComplianceStateProvider is null) throw new ArgumentNullException(nameof(currentComplianceStateProvider));

    return new PeriodicDriftScheduler(this, bundleRoot, interval, currentComplianceStateProvider, autoRemediate, onCompleted, onError, ct);
  }

  private async Task AutoRemediateAsync(
    string bundleRoot,
    IReadOnlyList<DriftSnapshot> driftEvents,
    HashSet<string> remediatedRules,
    List<string> remediationErrors,
    CancellationToken ct)
  {
    if (_remediationHandlers.Count == 0 || driftEvents.Count == 0)
      return;

    foreach (var driftEvent in driftEvents.Where(e => e.ChangeType == DriftChangeTypes.StateChanged))
    {
      IRemediationHandler? handler;
      if (!_remediationHandlers.TryGetValue(driftEvent.RuleId, out handler))
        continue;

      try
      {
        var result = await handler.ApplyAsync(BuildRemediationContext(bundleRoot, driftEvent.RuleId), ct).ConfigureAwait(false);
        if (result.Success)
          remediatedRules.Add(driftEvent.RuleId);
        else
          remediationErrors.Add(driftEvent.RuleId + ": " + (result.ErrorMessage ?? "remediation failed"));
      }
      catch (Exception ex)
      {
        remediationErrors.Add(driftEvent.RuleId + ": " + ex.Message);
      }
    }
  }

  private static RemediationContext BuildRemediationContext(string bundleRoot, string ruleId)
  {
    return new RemediationContext
    {
      BundleRoot = bundleRoot,
      Mode = HardeningMode.Safe,
      DryRun = false,
      Control = new ControlRecord
      {
        ControlId = ruleId,
        ExternalIds = new ExternalIds
        {
          RuleId = ruleId
        },
        Title = ruleId,
        Severity = "unknown"
      }
    };
  }

  private static IReadOnlyDictionary<string, string> NormalizeStateMap(IReadOnlyDictionary<string, string> map)
  {
    var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var kvp in map)
    {
      if (string.IsNullOrWhiteSpace(kvp.Key))
        continue;

      normalized[kvp.Key.Trim()] = NormalizeStatus(kvp.Value);
    }

    return normalized;
  }

  private static IReadOnlyDictionary<string, string> LoadCurrentComplianceState(string bundleRoot)
  {
    if (string.IsNullOrEmpty(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));

    var verifyRoot = Path.Combine(bundleRoot, "Verify");
    if (!Directory.Exists(verifyRoot))
      throw new DirectoryNotFoundException("Verify output directory not found: " + verifyRoot);

    var reportPath = Directory
      .GetFiles(verifyRoot, "consolidated-results.json", SearchOption.AllDirectories)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(reportPath))
      throw new FileNotFoundException("No consolidated verify report found under bundle Verify directory.", verifyRoot);

    using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));
    var rootIndex = BuildCaseInsensitiveIndex(doc.RootElement);
    JsonElement resultsElement;
    if (!rootIndex.TryGetValue("results", out resultsElement)
      || resultsElement.ValueKind != JsonValueKind.Array)
    {
      throw new InvalidOperationException("Verify report is missing a results array: " + reportPath);
    }

    var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in resultsElement.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.Object)
        continue;

      var itemIndex = BuildCaseInsensitiveIndex(item);
      var ruleId = ReadStringFromIndex(itemIndex, "ruleId");
      var vulnId = ReadStringFromIndex(itemIndex, "vulnId");
      var key = string.IsNullOrWhiteSpace(ruleId)
        ? vulnId
        : ruleId;

      if (string.IsNullOrWhiteSpace(key))
        continue;

      var status = NormalizeStatus(ReadStringFromIndex(itemIndex, "status"));
      state[key.Trim()] = status;
    }

    return state;
  }

  private static string NormalizeStatus(string? status)
  {
    var token = NormalizeToken(status);
    if (token.Length == 0)
      return "Open";

    if (token == "pass" || token == "notafinding" || token == "compliant" || token == "closed")
      return "Pass";

    if (token == "notapplicable" || token == "na")
      return "NotApplicable";

    if (token == "fail" || token == "noncompliant")
      return "Fail";

    if (token == "open" || token == "notreviewed" || token == "notchecked" || token == "unknown" || token == "error" || token == "informational")
      return "Open";

    return "Open";
  }

  private static string NormalizeToken(string? status)
  {
    if (string.IsNullOrWhiteSpace(status))
      return string.Empty;

    var source = status.Trim().ToLowerInvariant();
    var sb = new StringBuilder(source.Length);
    foreach (var ch in source)
    {
      if (char.IsLetterOrDigit(ch))
        sb.Append(ch);
    }

    return sb.ToString();
  }

  private static Dictionary<string, JsonElement> BuildCaseInsensitiveIndex(JsonElement obj)
  {
    var index = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    foreach (var prop in obj.EnumerateObject())
      index.TryAdd(prop.Name, prop.Value);
    return index;
  }

  private static string? ReadStringFromIndex(IReadOnlyDictionary<string, JsonElement> index, string propertyName)
  {
    if (!index.TryGetValue(propertyName, out var value))
      return null;
    return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
  }

  private static string? ReadStringProperty(JsonElement element, string propertyName)
  {
    JsonElement value;
    if (!TryGetPropertyCaseInsensitive(element, propertyName, out value))
      return null;

    return value.ValueKind == JsonValueKind.String
      ? value.GetString()
      : null;
  }

  private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
  {
    if (element.ValueKind != JsonValueKind.Object)
    {
      value = default;
      return false;
    }

    if (element.TryGetProperty(propertyName, out value))
      return true;

    foreach (var property in element.EnumerateObject())
    {
      if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
      {
        value = property.Value;
        return true;
      }
    }

    value = default;
    return false;
  }

  private sealed class PeriodicDriftScheduler : IDisposable
  {
    private readonly DriftDetectionService _service;
    private readonly string _bundleRoot;
    private readonly Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> _currentComplianceStateProvider;
    private readonly bool _autoRemediate;
    private readonly Action<DriftCheckResult>? _onCompleted;
    private readonly Action<Exception>? _onError;
    private readonly CancellationTokenSource _cts;
    private readonly Timer _timer;
    private bool _disposed;

    public PeriodicDriftScheduler(
      DriftDetectionService service,
      string bundleRoot,
      TimeSpan interval,
      Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> currentComplianceStateProvider,
      bool autoRemediate,
      Action<DriftCheckResult>? onCompleted,
      Action<Exception>? onError,
      CancellationToken ct)
    {
      _service = service;
      _bundleRoot = bundleRoot;
      _currentComplianceStateProvider = currentComplianceStateProvider;
      _autoRemediate = autoRemediate;
      _onCompleted = onCompleted;
      _onError = onError;
      _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      _timer = new Timer(OnTick, null, TimeSpan.Zero, interval);
    }

    public void Dispose()
    {
      if (_disposed)
        return;

      _disposed = true;
      _cts.Cancel();
      _timer.Dispose();
      _cts.Dispose();
    }

    private void OnTick(object? state)
    {
      if (_cts.IsCancellationRequested)
        return;

      _ = RunTickAsync();
    }

    private async Task RunTickAsync()
    {
      try
      {
        var currentState = await _currentComplianceStateProvider(_cts.Token).ConfigureAwait(false);
        var result = await _service
          .CheckAsync(_bundleRoot, currentState, _autoRemediate, _cts.Token)
          .ConfigureAwait(false);

        _onCompleted?.Invoke(result);
      }
      catch (OperationCanceledException) when (_cts.IsCancellationRequested)
      {
      }
      catch (Exception ex)
      {
        _onError?.Invoke(ex);
      }
    }
  }
}

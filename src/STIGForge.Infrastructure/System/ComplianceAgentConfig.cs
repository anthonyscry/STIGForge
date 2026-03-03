using System.Text.Json;

namespace STIGForge.Infrastructure.System;

public sealed class ComplianceAgentConfig
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
  };

  public string BundleRoot { get; set; } = string.Empty;
  public int CheckIntervalMinutes { get; set; } = 1440;
  public bool AutoRemediate { get; set; }
  public bool EnableAuditForwarding { get; set; } = true;
  public int MaxDriftEventsToForward { get; set; } = 10;

  public static async Task<ComplianceAgentConfig> LoadFromFileAsync(string configPath)
  {
    if (string.IsNullOrWhiteSpace(configPath))
      throw new ArgumentException("Value cannot be null or empty.", nameof(configPath));
    if (!File.Exists(configPath))
      throw new FileNotFoundException("Compliance agent config file not found.", configPath);

    await using var stream = File.OpenRead(configPath);
    var config = await JsonSerializer.DeserializeAsync<ComplianceAgentConfig>(stream, JsonOptions).ConfigureAwait(false);
    if (config == null)
      throw new InvalidOperationException("Compliance agent config payload is empty.");

    Validate(config, configPath);
    return config;
  }

  public static async Task SaveToFileAsync(ComplianceAgentConfig config, string path)
  {
    ArgumentNullException.ThrowIfNull(config);
    if (string.IsNullOrWhiteSpace(path))
      throw new ArgumentException("Value cannot be null or empty.", nameof(path));

    Validate(config, path);

    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(directory))
      Directory.CreateDirectory(directory);

    await using var stream = File.Create(path);
    await JsonSerializer.SerializeAsync(stream, config, JsonOptions).ConfigureAwait(false);
  }

  private static void Validate(ComplianceAgentConfig config, string configPath)
  {
    if (string.IsNullOrWhiteSpace(config.BundleRoot))
      throw new InvalidOperationException($"Compliance agent config '{configPath}' must define BundleRoot.");
    if (config.CheckIntervalMinutes <= 0)
      throw new InvalidOperationException($"Compliance agent config '{configPath}' has invalid CheckIntervalMinutes '{config.CheckIntervalMinutes}'.");
    if (config.MaxDriftEventsToForward < 0)
      throw new InvalidOperationException($"Compliance agent config '{configPath}' has invalid MaxDriftEventsToForward '{config.MaxDriftEventsToForward}'.");
  }
}

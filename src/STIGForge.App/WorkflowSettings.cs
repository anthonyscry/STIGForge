using System;
using System.IO;
using System.Text.Json;

namespace STIGForge.App;

public class WorkflowSettings
{
    public string ImportFolderPath { get; set; } = string.Empty;
    public string EvaluateStigToolPath { get; set; } = string.Empty;
    public string SccToolPath { get; set; } = string.Empty;
    public string OutputFolderPath { get; set; } = string.Empty;
    public string MachineTarget { get; set; } = "localhost";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "STIGForge", "workflow-settings.json");

    public static WorkflowSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return new WorkflowSettings();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WorkflowSettings>(json) ?? new WorkflowSettings();
    }

    public static void Save(WorkflowSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }
}

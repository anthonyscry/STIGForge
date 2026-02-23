using System.CommandLine;
using System.Text.Json;
using STIGForge.Evidence;

namespace STIGForge.Cli.Commands;

internal static class EvidenceCommands
{
  public static void Register(RootCommand rootCmd, Func<Microsoft.Extensions.Hosting.IHost> buildHost)
  {
    RegisterEvidenceIndex(rootCmd);
  }

  private static void RegisterEvidenceIndex(RootCommand rootCmd)
  {
    var cmd = new Command("evidence-index", "Build and query the evidence index for a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var controlOpt = new Option<string>("--control", () => string.Empty, "Filter to specific control key");
    var typeOpt = new Option<string>("--type", () => string.Empty, "Filter by evidence type");
    var runOpt = new Option<string>("--run", () => string.Empty, "Filter by run ID");
    var rebuildOpt = new Option<bool>("--rebuild", "Force rebuild even if index exists");
    var jsonOpt = new Option<bool>("--json", "Output as JSON");
    cmd.AddOption(bundleOpt);
    cmd.AddOption(controlOpt);
    cmd.AddOption(typeOpt);
    cmd.AddOption(runOpt);
    cmd.AddOption(rebuildOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (bundle, control, type, run, rebuild, json) =>
    {
      var svc = new EvidenceIndexService(bundle);
      EvidenceIndex? index = null;

      if (!rebuild)
        index = await svc.ReadIndexAsync(CancellationToken.None);

      if (index == null)
      {
        index = await svc.BuildIndexAsync(CancellationToken.None);
        await svc.WriteIndexAsync(index, CancellationToken.None);
        Console.WriteLine($"Evidence index built: {index.TotalEntries} entries");
      }
      else
      {
        Console.WriteLine($"Evidence index loaded: {index.TotalEntries} entries");
      }

      // Apply filters
      var entries = index.Entries.AsEnumerable();

      if (!string.IsNullOrWhiteSpace(control))
        entries = EvidenceIndexService.GetEvidenceForControl(index, control);
      else if (!string.IsNullOrWhiteSpace(type))
        entries = EvidenceIndexService.GetEvidenceByType(index, type);
      else if (!string.IsNullOrWhiteSpace(run))
        entries = EvidenceIndexService.GetEvidenceByRun(index, run);

      var filtered = entries.ToList();

      if (json)
      {
        var jsonText = JsonSerializer.Serialize(filtered, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(jsonText);
      }
      else
      {
        Console.WriteLine();
        Console.WriteLine($"{"ControlKey",-25} {"EvidenceId",-45} {"Type",-12} {"SHA-256 (first 16)",-18} {"Timestamp"}");
        Console.WriteLine(new string('-', 120));

        foreach (var entry in filtered)
        {
          var shortHash = entry.Sha256.Length >= 16 ? entry.Sha256[..16] : entry.Sha256;
          Console.WriteLine($"{entry.ControlKey,-25} {entry.EvidenceId,-45} {entry.Type,-12} {shortHash,-18} {entry.TimestampUtc}");
        }

        Console.WriteLine();
        Console.WriteLine($"Total: {filtered.Count} entries");
      }
    }, bundleOpt, controlOpt, typeOpt, runOpt, rebuildOpt, jsonOpt);

    rootCmd.AddCommand(cmd);
  }
}

using System.CommandLine;
using STIGForge.Cli.Commands;

var rootCmd = new RootCommand("STIGForge CLI (offline-first)");

ImportCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
BuildCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
VerifyCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
DiffRebaseCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
BundleCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
AuditCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
ExportCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
ScheduleCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
FleetCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
ProfileCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
OverlayCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);

return await InvokeWithErrorHandlingAsync(rootCmd, args);

static async Task<int> InvokeWithErrorHandlingAsync(RootCommand command, string[] argv)
{
  try
  {
    return await command.InvokeAsync(argv);
  }
  catch (ArgumentException ex)
  {
    Console.Error.WriteLine($"[CLI-ARG-001] {ex.Message}");
    return 2;
  }
  catch (FileNotFoundException ex)
  {
    Console.Error.WriteLine($"[CLI-IO-404] {ex.Message}");
    return 3;
  }
  catch (DirectoryNotFoundException ex)
  {
    Console.Error.WriteLine($"[CLI-IO-404] {ex.Message}");
    return 3;
  }
  catch (Exception ex)
  {
    Console.Error.WriteLine($"[CLI-UNEXPECTED-500] {ex.Message}");
    return 1;
  }
}

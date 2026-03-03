using System.CommandLine;
using STIGForge.Cli.Commands;
using STIGForge.Core.Errors;

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
ComplianceCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
ExceptionCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
ReleaseCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
RemediationCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
SecurityCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
DriftCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
RollbackCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
GpoConflictCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);
PhaseCExpansionCommands.Register(rootCmd, STIGForge.Cli.CliHostFactory.BuildHost);


return await InvokeWithErrorHandlingAsync(rootCmd, args);

static async Task<int> InvokeWithErrorHandlingAsync(RootCommand command, string[] argv)
{
  try
  {
    return await command.InvokeAsync(argv);
  }
  catch (StigForgeException ex)
  {
    Console.Error.WriteLine($"[{ex.ErrorCode}] ({ex.Component}) {ex.Message}");
    return 2;
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

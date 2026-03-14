using FluentAssertions;
using STIGForge.Infrastructure.System;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class ScheduledTaskServiceTests : IDisposable
{
  private readonly string _tempDir;

  public ScheduledTaskServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-sched-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
  }

  // ── Validation: TaskName ───────────────────────────────────────────────────

  [Fact]
  public void Register_ThrowsForEmptyTaskName()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest { TaskName = "", BundleRoot = "C:\\bundle" });
    act.Should().Throw<ArgumentException>().WithMessage("*TaskName*");
  }

  [Fact]
  public void Register_ThrowsForWhitespaceTaskName()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest { TaskName = "   ", BundleRoot = "C:\\bundle" });
    act.Should().Throw<ArgumentException>().WithMessage("*TaskName*");
  }

  [Fact]
  public void Register_ThrowsForTaskNameWithInvalidCharacters()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest { TaskName = "bad name!", BundleRoot = "C:\\bundle" });
    act.Should().Throw<ArgumentException>().WithMessage("*invalid characters*");
  }

  [Fact]
  public void Register_ThrowsForTaskNameTooLong()
  {
    var svc = new ScheduledTaskService();
    var longName = new string('A', 201);
    var act = () => svc.Register(new ScheduledTaskRequest { TaskName = longName, BundleRoot = "C:\\bundle" });
    act.Should().Throw<ArgumentException>().WithMessage("*200*");
  }

  [Fact]
  public void Register_AcceptsValidTaskNameCharacters()
  {
    var svc = new ScheduledTaskService();
    // Should NOT throw on name validation; will throw on CliPath later (FileNotFoundException or ArgumentException)
    var act = () => svc.Register(new ScheduledTaskRequest
    {
      TaskName = "Valid_Task.Name-01",
      BundleRoot = "C:\\bundle",
      CliPath = Path.Combine(_tempDir, "nonexistent.exe")
    });
    act.Should().Throw<Exception>().Which.Should().NotBeOfType<ArgumentException>();
  }

  // ── Validation: BundleRoot ─────────────────────────────────────────────────

  [Fact]
  public void Register_ThrowsForEmptyBundleRoot()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest { TaskName = "test", BundleRoot = "" });
    act.Should().Throw<ArgumentException>().WithMessage("*BundleRoot*");
  }

  // ── Validation: StartTime ──────────────────────────────────────────────────

  [Fact]
  public void Register_ThrowsForInvalidStartTimeFormat()
  {
    var cli = CreateTempCli();
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest
    {
      TaskName = "TestTask",
      BundleRoot = "C:\\bundle",
      StartTime = "9:00",
      CliPath = cli
    });
    act.Should().Throw<ArgumentException>().WithMessage("*StartTime*");
  }

  [Fact]
  public void Register_ThrowsForStartTimeWithWords()
  {
    var cli = CreateTempCli();
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest
    {
      TaskName = "TestTask",
      BundleRoot = "C:\\bundle",
      StartTime = "noon",
      CliPath = cli
    });
    act.Should().Throw<ArgumentException>().WithMessage("*StartTime*");
  }

  // ── Validation: DaysOfWeek ─────────────────────────────────────────────────

  [Fact]
  public void Register_ThrowsForInvalidDayOfWeek()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest
    {
      TaskName = "TestTask",
      BundleRoot = "C:\\bundle",
      DaysOfWeek = "MONDAY"
    });
    act.Should().Throw<ArgumentException>().WithMessage("*DaysOfWeek*");
  }

  [Fact]
  public void Register_ThrowsForPartiallyInvalidDaysOfWeek()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest
    {
      TaskName = "TestTask",
      BundleRoot = "C:\\bundle",
      DaysOfWeek = "MON,BADDAY"
    });
    act.Should().Throw<ArgumentException>().WithMessage("*DaysOfWeek*");
  }

  // ── Validation: CliPath ────────────────────────────────────────────────────

  [Fact]
  public void Register_ThrowsWhenCliPathDoesNotExist()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest
    {
      TaskName = "TestTask",
      BundleRoot = "C:\\bundle",
      CliPath = Path.Combine(_tempDir, "missing.exe")
    });
    act.Should().Throw<FileNotFoundException>();
  }

  [Fact]
  public void Register_ThrowsWhenCliPathHasInjectionCharacters()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest
    {
      TaskName = "TestTask",
      BundleRoot = "C:\\bundle",
      CliPath = "C:\\path;injected.exe"
    });
    act.Should().Throw<ArgumentException>().WithMessage("*invalid characters*");
  }

  [Fact]
  public void Register_ThrowsWhenCliPathHasPipeCharacter()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest
    {
      TaskName = "TestTask",
      BundleRoot = "C:\\bundle",
      CliPath = "C:\\path|injected.exe"
    });
    act.Should().Throw<ArgumentException>().WithMessage("*invalid characters*");
  }

  [Fact]
  public void Register_ThrowsWhenCliPathIsNotExeOrDll()
  {
    var txtCli = Path.Combine(_tempDir, "cli.txt");
    File.WriteAllText(txtCli, "fake");
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest
    {
      TaskName = "TestTask",
      BundleRoot = "C:\\bundle",
      CliPath = txtCli
    });
    act.Should().Throw<ArgumentException>().WithMessage("*.exe or .dll*");
  }

  // ── Successful registration (schtasks may fail on non-Windows, but result is returned) ──

  [Fact]
  public void Register_ReturnsResultWithPrefixedTaskName()
  {
    var cli = CreateTempCli();
    var svc = new ScheduledTaskService();
    var result = svc.Register(new ScheduledTaskRequest
    {
      TaskName = "UnitTest_" + Guid.NewGuid().ToString("N").Substring(0, 8),
      BundleRoot = "C:\\test\\bundle",
      Frequency = "ONCE",
      StartTime = "23:59",
      CliPath = cli
    });

    result.TaskName.Should().StartWith("STIGForge\\");
    result.ScriptPath.Should().NotBeNullOrWhiteSpace();
    ((object)result.ExitCode).Should().BeOfType<int>();

    if (result.Success)
      svc.Unregister(result.TaskName.Replace("STIGForge\\", ""));
  }

  [Fact]
  public void Register_WithDllCliPath_ReturnsResult()
  {
    var cli = Path.Combine(_tempDir, "STIGForge.Cli.dll");
    File.WriteAllText(cli, "fake");
    var svc = new ScheduledTaskService();
    var result = svc.Register(new ScheduledTaskRequest
    {
      TaskName = "UnitTestDll",
      BundleRoot = "C:\\test\\bundle",
      Frequency = "DAILY",
      StartTime = "06:00",
      CliPath = cli
    });

    result.TaskName.Should().Be("STIGForge\\UnitTestDll");
    result.ScriptPath.Should().NotBeNullOrWhiteSpace();

    if (result.Success)
      svc.Unregister("UnitTestDll");
  }

  [Fact]
  public void Register_WritesCompanionScript()
  {
    var cli = CreateTempCli();
    var svc = new ScheduledTaskService();
    var result = svc.Register(new ScheduledTaskRequest
    {
      TaskName = "CompanionScriptTest",
      BundleRoot = "C:\\test\\bundle",
      Frequency = "WEEKLY",
      StartTime = "08:00",
      DaysOfWeek = "MON,WED,FRI",
      CliPath = cli
    });

    // Companion script is always written if CliPath validation passes
    result.ScriptPath.Should().NotBeNullOrWhiteSpace();
    File.Exists(result.ScriptPath).Should().BeTrue();

    // Script contains STIGForge header comment
    var content = File.ReadAllText(result.ScriptPath!);
    content.Should().Contain("STIGForge");

    if (result.Success)
      svc.Unregister("CompanionScriptTest");
  }

  // ── Unregister validation ──────────────────────────────────────────────────

  [Fact]
  public void Unregister_ThrowsForInvalidTaskName()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Unregister("bad name!");
    act.Should().Throw<ArgumentException>().WithMessage("*invalid characters*");
  }

  [Fact]
  public void Unregister_ThrowsForTaskNameTooLong()
  {
    var svc = new ScheduledTaskService();
    var longName = new string('A', 201);
    var act = () => svc.Unregister(longName);
    act.Should().Throw<ArgumentException>().WithMessage("*200*");
  }

  [Fact]
  public void Unregister_ReturnsResultForNonexistentTask()
  {
    var svc = new ScheduledTaskService();
    var result = svc.Unregister("NonExistentTask_" + Guid.NewGuid().ToString("N").Substring(0, 8));

    // On any OS: returns a result (not throws)
    result.Should().NotBeNull();
    result.TaskName.Should().StartWith("STIGForge\\");
  }

  // ── List ───────────────────────────────────────────────────────────────────

  [Fact]
  public void List_ReturnsResult()
  {
    var svc = new ScheduledTaskService();
    var result = svc.List();

    result.Should().NotBeNull();
    result.TaskName.Should().Be("STIGForge");
    result.Message.Should().NotBeNullOrWhiteSpace();
  }

  // ── Result model ──────────────────────────────────────────────────────────

  [Fact]
  public void ScheduledTaskResult_DefaultsAreCorrect()
  {
    var result = new ScheduledTaskResult();
    result.Success.Should().BeFalse();
    result.TaskName.Should().BeEmpty();
    result.Message.Should().BeEmpty();
    result.ScriptPath.Should().BeNull();
    result.ExitCode.Should().Be(0);
  }

  [Fact]
  public void ScheduledTaskRequest_DefaultsAreCorrect()
  {
    var request = new ScheduledTaskRequest();
    request.TaskName.Should().BeEmpty();
    request.BundleRoot.Should().BeEmpty();
    request.CliPath.Should().BeNull();
    request.Frequency.Should().BeNull();
    request.StartTime.Should().BeNull();
    request.DaysOfWeek.Should().BeNull();
    request.IntervalDays.Should().Be(0);
  }

  // ── Helper ─────────────────────────────────────────────────────────────────

  private string CreateTempCli()
  {
    var path = Path.Combine(_tempDir, "STIGForge.Cli.exe");
    File.WriteAllText(path, "fake");
    return path;
  }
}

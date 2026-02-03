using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

public interface IClock
{
  DateTimeOffset Now { get; }
}

public interface IHashingService
{
  Task<string> Sha256FileAsync(string path, CancellationToken ct);
  Task<string> Sha256TextAsync(string content, CancellationToken ct);
}

public interface IPathBuilder
{
  string GetAppDataRoot();
  string GetContentPacksRoot();
  string GetPackRoot(string packId);
  string GetBundleRoot(string bundleId);
  string GetLogsRoot();
  string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts);
}

public interface IClassificationScopeService
{
  CompiledControls Compile(Profile profile, IReadOnlyList<ControlRecord> controls);
}

public sealed class CompiledControl
{
  public CompiledControl(ControlRecord control, ControlStatus status, string? comment, bool needsReview, string? reviewReason)
  {
    Control = control;
    Status = status;
    Comment = comment;
    NeedsReview = needsReview;
    ReviewReason = reviewReason;
  }

  public ControlRecord Control { get; }
  public ControlStatus Status { get; }
  public string? Comment { get; }
  public bool NeedsReview { get; }
  public string? ReviewReason { get; }
}

public sealed class CompiledControls
{
  public CompiledControls(IReadOnlyList<CompiledControl> controls, IReadOnlyList<CompiledControl> reviewQueue)
  {
    Controls = controls;
    ReviewQueue = reviewQueue;
  }

  public IReadOnlyList<CompiledControl> Controls { get; }
  public IReadOnlyList<CompiledControl> ReviewQueue { get; }
}

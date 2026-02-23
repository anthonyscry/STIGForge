using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.Paths;

public sealed class PathBuilder : IPathBuilder
{
  private readonly string _root;

  public PathBuilder()
    : this(AppContext.BaseDirectory, Environment.CurrentDirectory)
  {
  }

  public PathBuilder(string appBaseDirectory, string currentDirectory)
  {
    _root = ResolveRoot(appBaseDirectory, currentDirectory);
  }

  private static string ResolveRoot(string appBaseDirectory, string currentDirectory)
  {
    var appBase = NormalizeDirectory(appBaseDirectory, AppContext.BaseDirectory);
    var current = NormalizeDirectory(currentDirectory, Environment.CurrentDirectory);

    var repoRoot = FindRepositoryRoot(appBase) ?? FindRepositoryRoot(current);
    if (!string.IsNullOrWhiteSpace(repoRoot))
      return Path.Combine(repoRoot, ".stigforge");

    return Path.Combine(current, ".stigforge");
  }

  private static string? FindRepositoryRoot(string startDirectory)
  {
    var dir = new DirectoryInfo(startDirectory);
    while (dir != null)
    {
      var git = Path.Combine(dir.FullName, ".git");
      if (Directory.Exists(git) || File.Exists(git))
        return dir.FullName;
      dir = dir.Parent;
    }

    return null;
  }

  private static string NormalizeDirectory(string? candidate, string fallback)
  {
    var value = string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
    return Path.GetFullPath(value);
  }

  public string GetAppDataRoot() => _root;
  public string GetContentPacksRoot() => Path.Combine(_root, "contentpacks");
  public string GetPackRoot(string packId) => Path.Combine(GetContentPacksRoot(), packId);
  public string GetBundleRoot(string bundleId) => Path.Combine(_root, "bundles", bundleId);
  public string GetLogsRoot() => Path.Combine(_root, "logs");
  public string GetImportRoot() => Path.Combine(_root, "import");
  public string GetImportInboxRoot() => Path.Combine(GetImportRoot(), "inbox");
  public string GetImportIndexPath() => Path.Combine(GetImportRoot(), "inbox_index.json");
  public string GetToolsRoot() => Path.Combine(_root, "tools");

  public string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts)
  {
    string stamp = ts.ToString("yyyyMMdd-HHmm");
    string rootName = "EMASS_" + San(systemName) + San(os) + San(role) + San(profileName) + San(packName) + "_" + stamp;
    return Path.Combine(_root, "exports", rootName);
  }

  private static string San(string s)
  {
    foreach (var c in Path.GetInvalidFileNameChars())
      s = s.Replace(c.ToString(), string.Empty);

    return s.Replace(" ", string.Empty);
  }
}

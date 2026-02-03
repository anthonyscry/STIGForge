using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.Paths;

public sealed class PathBuilder : IPathBuilder
{
  private readonly string _root;

  public PathBuilder()
  {
    _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "STIGForge");
  }

  public string GetAppDataRoot() => _root;
  public string GetContentPacksRoot() => Path.Combine(_root, "contentpacks");
  public string GetPackRoot(string packId) => Path.Combine(GetContentPacksRoot(), packId);
  public string GetBundleRoot(string bundleId) => Path.Combine(_root, "bundles", bundleId);
  public string GetLogsRoot() => Path.Combine(_root, "logs");

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

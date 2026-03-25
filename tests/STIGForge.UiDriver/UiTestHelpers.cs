namespace STIGForge.UiDriver;

public static class UiTestHelpers
{
  public static string LocateRepositoryRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
        return current.FullName;
      current = current.Parent;
    }
    throw new InvalidOperationException("Unable to locate repository root.");
  }

  public static string LocateAppExecutable(string repoRoot)
  {
    var candidates = new[]
    {
      Path.Combine(repoRoot, "src", "STIGForge.App", "bin", "Debug", "net8.0-windows", "STIGForge.App.exe"),
      Path.Combine(repoRoot, "src", "STIGForge.App", "bin", "Release", "net8.0-windows", "STIGForge.App.exe"),
    };
    foreach (var c in candidates)
      if (File.Exists(c)) return c;

    var binRoot = Path.Combine(repoRoot, "src", "STIGForge.App", "bin");
    if (Directory.Exists(binRoot))
    {
      var found = Directory.EnumerateFiles(binRoot, "STIGForge.App.exe", SearchOption.AllDirectories)
        .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
      if (!string.IsNullOrWhiteSpace(found)) return found;
    }
    throw new FileNotFoundException("Could not locate STIGForge.App.exe.");
  }

  public static string GetScreenshotDir(string repoRoot, string testCategory)
    => Path.Combine(repoRoot, ".artifacts", "e2e", testCategory);
}

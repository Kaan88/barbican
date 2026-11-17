namespace Barbican.Testing;

/// <summary>Paths into the repository checkout the tests run from (the folder holding Barbican.slnx).</summary>
public static class TestRepo
{
    private static readonly Lazy<string> RootPath = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Barbican.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No Barbican.slnx above {AppContext.BaseDirectory}");
    });

    public static string Root => RootPath.Value;

    /// <summary>An absolute path from a repo-relative one with forward slashes.</summary>
    public static string PathOf(string relative) => Path.Combine([Root, .. relative.Split('/')]);
}

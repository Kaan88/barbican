namespace Barbican.GenTokens;

/// <summary>Finds the repository root: the nearest ancestor that holds Barbican.slnx.</summary>
public static class RepoRoot
{
    public static string Find(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Barbican.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No Barbican.slnx above {start}");
    }
}

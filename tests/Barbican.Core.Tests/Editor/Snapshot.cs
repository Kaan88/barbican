namespace Barbican.Core.Tests.Editor;

/// <summary>
/// Snapshot comparison without a package: the expected text is a file under <c>tests/fixtures/editor/</c>. A missing or different file fails the test;
/// <c>BARBICAN_SNAPSHOT=update</c> rewrites the file instead (review the diff before committing). Line endings are normalised to LF on both sides.
/// </summary>
internal static class Snapshot
{
    public const string UpdateVariable = "BARBICAN_SNAPSHOT";

    public static void Match(string relativePath, string actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        var path = TestRepo.PathOf("tests/fixtures/editor/" + relativePath);
        if (string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "update", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual.ReplaceLineEndings("\n"));
            return;
        }

        Assert.True(File.Exists(path), $"missing snapshot {relativePath}; run with {UpdateVariable}=update");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.Equal(expected, actual.ReplaceLineEndings("\n"));
    }
}

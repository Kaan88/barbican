// gen-tokens: design/legacy-slint/theme.slint -> XAML theme dictionaries + Presentation token tables.
// Usage: Barbican.GenTokens [--check] [--root <repo>]
//   --check  write nothing; exit 1 when a generated file differs from what is committed.

using Barbican.GenTokens;

var check = args.Contains("--check", StringComparer.Ordinal);
var rootIndex = Array.IndexOf(args, "--root");
var root = rootIndex >= 0 && rootIndex + 1 < args.Length ? args[rootIndex + 1] : RepoRoot.Find(Environment.CurrentDirectory);

var source = await File.ReadAllTextAsync(Path.Combine(root, Emitters.SourcePath)).ConfigureAwait(false);
var tokens = TokenSet.FromSlint(source);
var stale = 0;
foreach (var (relative, content) in Emitters.All(tokens))
{
    var path = Path.Combine(root, relative);
    var existing = File.Exists(path) ? (await File.ReadAllTextAsync(path).ConfigureAwait(false)).Replace("\r\n", "\n", StringComparison.Ordinal) : null;
    if (string.Equals(existing, content, StringComparison.Ordinal))
    {
        Console.WriteLine($"up to date  {relative}");
        continue;
    }

    stale++;
    if (check)
    {
        Console.WriteLine($"STALE       {relative}");
        continue;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
    Console.WriteLine($"written     {relative}");
}

Console.WriteLine($"{tokens.ColorKeys.Count} colours x 8 variants, {tokens.ShapeKeys.Count} shape tokens x 2 styles, {tokens.Motion.Count} motion, {tokens.Ease.Count} easing, {tokens.Icons.Count} icons");
return check && stale > 0 ? 1 : 0;

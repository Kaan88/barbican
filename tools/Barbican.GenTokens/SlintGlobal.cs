namespace Barbican.GenTokens;

/// <summary>An `export global Name { … }` block: its properties in source order.</summary>
public sealed record SlintGlobal(string Name, IReadOnlyList<SlintProperty> Properties)
{
    public SlintProperty? Find(string name) => Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}

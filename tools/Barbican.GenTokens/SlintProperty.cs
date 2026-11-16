namespace Barbican.GenTokens;

/// <summary>One `property` line of an `export global` block.</summary>
/// <param name="Name">Slint name (kebab-case).</param>
/// <param name="Type">Slint type inside the angle brackets (color, length, bool, …).</param>
/// <param name="Expression">The expression after the colon, or null when there is none.</param>
/// <param name="IsInput">True for `in` / `in-out` properties (set from outside: the variant switches).</param>
/// <param name="Line">1-based line in the source, for error messages.</param>
public sealed record SlintProperty(string Name, string Type, string? Expression, bool IsInput, int Line);

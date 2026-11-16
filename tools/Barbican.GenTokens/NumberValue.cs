namespace Barbican.GenTokens;

/// <summary>A plain number (Slint int or float).</summary>
public sealed record NumberValue(double Value) : SlintValue;

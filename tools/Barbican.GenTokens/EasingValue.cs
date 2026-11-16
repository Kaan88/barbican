namespace Barbican.GenTokens;

/// <summary>A cubic-bezier easing curve.</summary>
public sealed record EasingValue(double X1, double Y1, double X2, double Y2) : SlintValue;

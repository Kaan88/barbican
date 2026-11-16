namespace Barbican.Spike.BodyShell;

/// <summary>One captured phase: how many frames, the per-frame classification summed up, and the verdict.</summary>
internal sealed record PhaseResult(string Name, bool Pass, int Frames, double DurationMs, double MaxGapMs, string Verdict, IReadOnlyDictionary<string, double> Metrics);

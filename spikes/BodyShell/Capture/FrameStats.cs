namespace Barbican.Spike.BodyShell.Capture;

/// <summary>One frame, classified: for each region the fraction of its pixels in each colour class ("other" for the rest).</summary>
internal sealed record FrameStats(long Time, bool Located, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Regions)
{
    public double Fraction(string region, string cls) =>
        Regions.TryGetValue(region, out var r) && r.TryGetValue(cls, out var v) ? v : 0;

    /// <summary>The class covering at least <paramref name="min"/> of the region, or "mixed".</summary>
    public string Dominant(string region, double min = 0.9)
    {
        if (!Regions.TryGetValue(region, out var r))
        {
            return "unlocated";
        }

        foreach (var (k, v) in r)
        {
            if (v >= min)
            {
                return k;
            }
        }

        return "mixed";
    }
}

using Windows.UI;

namespace Barbican.Spike.BodyShell.Capture;

/// <summary>
/// Classifies frames as they arrive (they are not kept: a 1000x720 BGRA frame is 2.9 MB) and saves
/// the frames a check asks for as PNG. Thread-safe: frames arrive on the capture thread.
/// </summary>
internal sealed class PhaseRecorder
{
    private readonly Lock _gate = new();
    private readonly List<Region> _regions;
    private readonly List<ColorClass> _classes;
    private readonly Color _marker;
    private readonly double _markerDips;
    private readonly List<FrameStats> _frames = [];
    private (int X, int Y, double Scale)? _origin;
    private CapturedFrame? _last;

    public PhaseRecorder(IEnumerable<Region> regions, IEnumerable<ColorClass> classes, Color marker, double markerDips)
    {
        _regions = [.. regions];
        _classes = [.. classes];
        _marker = marker;
        _markerDips = markerDips;
    }

    /// <summary>Frames to save: the predicate sees each frame's stats; the first match per key is written.</summary>
    public Func<FrameStats, string?>? SaveWhen { get; set; }

    public string? SaveDir { get; set; }

    public double Scale => _origin?.Scale ?? 0;

    public IReadOnlyList<FrameStats> Frames
    {
        get
        {
            lock (_gate)
            {
                return [.. _frames];
            }
        }
    }

    public CapturedFrame? Last
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    private HashSet<string> Saved { get; } = [];

    public void Add(CapturedFrame f)
    {
        _origin ??= FrameLocator.Find(f, _marker, _markerDips);
        var stats = Classify(f);
        string? save = null;
        lock (_gate)
        {
            _frames.Add(stats);
            _last = f;
            var key = SaveWhen?.Invoke(stats);
            if (key is not null && SaveDir is not null && Saved.Add(key))
            {
                save = Path.Combine(SaveDir, key + ".png");
            }
        }

        if (save is not null)
        {
            PngWriter.Save(f, save);
        }
    }

    private FrameStats Classify(CapturedFrame f)
    {
        if (_origin is not { } o)
        {
            return new FrameStats(f.Time, false, new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal));
        }

        var result = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal);
        foreach (var region in _regions)
        {
            var counts = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var c in _classes)
            {
                counts[c.Name] = 0;
            }

            counts["other"] = 0;
            var x0 = o.X + (int)Math.Round(region.Dips.X * o.Scale);
            var y0 = o.Y + (int)Math.Round(region.Dips.Y * o.Scale);
            var x1 = Math.Min(f.Width, o.X + (int)Math.Round((region.Dips.X + region.Dips.Width) * o.Scale));
            var y1 = Math.Min(f.Height, o.Y + (int)Math.Round((region.Dips.Y + region.Dips.Height) * o.Scale));
            var total = 0;
            for (var y = Math.Max(0, y0); y < y1; y += 2)
            {
                for (var x = Math.Max(0, x0); x < x1; x += 2)
                {
                    var i = (y * f.Stride) + (x * 4);
                    total++;
                    var hit = "other";
                    foreach (var c in _classes)
                    {
                        if (c.Matches(f.Bgra[i], f.Bgra[i + 1], f.Bgra[i + 2]))
                        {
                            hit = c.Name;
                            break;
                        }
                    }

                    counts[hit]++;
                }
            }

            foreach (var k in counts.Keys.ToList())
            {
                counts[k] = total == 0 ? 0 : counts[k] / total;
            }

            result[region.Name] = counts;
        }

        return new FrameStats(f.Time, true, result);
    }
}

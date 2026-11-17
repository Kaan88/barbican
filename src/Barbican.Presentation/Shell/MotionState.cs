// Mirrors: Rust motion.rs (Motion.reduce, Motion.rapid, step :69, no per-frame work); design/MOTION.md; spec B-MOT-001..004, B-MOT-009. Owner mailcore.

using Barbican.Core.Settings.Motion;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Shell;

/// <summary>
/// The two motion flags the views bind to: <see cref="Reduce"/> (from the setting or the OS) and <see cref="Rapid"/> (on while the list selection
/// moves faster than an animation). A flag flips on a user action; one single-shot timer clears <see cref="Rapid"/>, nothing runs per frame.
/// </summary>
public sealed class MotionState : ObservableObject, IDisposable
{
    private readonly TimeProvider _time;
    private readonly IDispatcher _dispatcher;
    private readonly Lock _gate = new();
    private ITimer? _calm;
    private long? _lastStep;
    private bool _reduce;
    private bool _rapid;
    private int _generation;

    public MotionState(TimeProvider time, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _time = time;
        _dispatcher = dispatcher;
    }

    /// <summary>Durations are multiplied by 0.</summary>
    public bool Reduce
    {
        get => _reduce;
        private set => SetProperty(ref _reduce, value);
    }

    /// <summary>List feedback snaps instead of animating.</summary>
    public bool Rapid
    {
        get => _rapid;
        private set => SetProperty(ref _rapid, value);
    }

    /// <summary>Applies the stored setting (<c>on</c>, <c>off</c>, <c>system</c>) with the OS animation state.</summary>
    public void Apply(string pref, bool systemAnimationsOff) => Reduce = MotionSettings.ReduceFor(pref, systemAnimationsOff);

    /// <summary>The list selection moved one step with the keyboard: steps closer than 150 ms are a key repeat, so feedback snaps until 180 ms of calm.</summary>
    public void Step()
    {
        var now = _time.GetTimestamp();
        var fast = _lastStep is { } last && _time.GetElapsedTime(last, now) < MotionSettings.RapidWithin;
        _lastStep = now;
        if (!fast)
        {
            return;
        }

        Rapid = true;
        int generation;
        lock (_gate)
        {
            generation = ++_generation;
            _calm?.Dispose();
            _calm = _time.CreateTimer(_ => _dispatcher.Post(() => Calm(generation)), null, MotionSettings.CalmAfter, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _calm?.Dispose();
            _calm = null;
        }
    }

    private void Calm(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }
        }

        Rapid = false;
    }
}

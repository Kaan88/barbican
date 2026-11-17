// Mirrors: Rust autosave.rs (QUIET :12, Queued :17, Leftover :42, leftover_for :53, due :67, reset_leftover :78, start :112, unedited :133, rebase :139, begin/finish :180-197),
// Bulwark components/email/email-composer.tsx (2 s autosave debounce after a real edit, one save at a time; a fresh composer, the quote and the signature are no reason to keep a draft: B-COMP-001..006, L-007..L-009).

namespace Barbican.Core.Compose;

/// <summary>
/// The autosave bookkeeping of one composer window (Rust kept it in thread-locals; here it is an instance the composer owns, D-241): content keys (a 64-bit hash of the fields and the body), the baseline the composer opened with,
/// what the server has, the save in flight, the work queued behind it, and the generation that makes a late save of a replaced composer harmless.
/// </summary>
public sealed class AutosaveState(TimeProvider time)
{
    private readonly Lock _gate = new();
    private ulong _generation;
    private ulong? _baseline;
    private ulong? _saved;
    private ulong? _key;
    private long _changedAt;
    private bool _hasChanged;
    private bool _inFlight;
    private ulong? _flightKey;
    private Queued? _queued;

    /// <summary>Bulwark's autosave delay.</summary>
    public static TimeSpan Quiet { get; } = TimeSpan.FromMilliseconds(2000);

    /// <summary>The tick of the composer's timer.</summary>
    public static TimeSpan Tick { get; } = TimeSpan.FromMilliseconds(500);

    public TimeProvider Time => time;

    public bool InFlight
    {
        get
        {
            lock (_gate)
            {
                return _inFlight;
            }
        }
    }

    public ulong Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    /// <summary>Whether an autosave is due: not in flight, the content differs from both the baseline and what the server has, and the composer was quiet for <see cref="Quiet"/>.</summary>
    public static bool Due(ulong key, ulong? baseline, ulong? saved, TimeSpan quiet, bool inFlight) =>
        !inFlight && key != baseline && key != saved && quiet >= Quiet;

    /// <summary>Pure decision for <see cref="ResetLeftover"/>: <paramref name="unsaved"/> = the old composer holds content the save in flight does not carry. A queued send is kept as a draft instead of a send nobody sees.</summary>
    public static Leftover? LeftoverFor(Queued? queued, bool unsaved) => queued switch
    {
        Queued.Discard => Leftover.Discard,
        Queued.Save or Queued.Send or Queued.Schedule => Leftover.Save,
        _ => unsaved ? Leftover.Save : null,
    };

    /// <summary>A new composer: forget everything about the previous one (the generation advances).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            ResetLocked();
        }
    }

    /// <summary>The composer finished opening: what it holds now (<paramref name="key"/>) counts as unedited; <paramref name="saved"/>: it is also what the server has (a reopened draft).</summary>
    public void Start(ulong key, bool saved)
    {
        lock (_gate)
        {
            _baseline = key;
            _saved = saved ? key : null;
            _key = key;
            _changedAt = time.GetTimestamp();
            _hasChanged = true;
        }
    }

    /// <summary>The composer's content now. Returns whether an autosave is due (the content changed from the baseline and from the server, and has been quiet for <see cref="Quiet"/>).</summary>
    public bool Observe(ulong key)
    {
        lock (_gate)
        {
            if (_key != key || !_hasChanged)
            {
                _key = key;
                _changedAt = time.GetTimestamp();
                _hasChanged = true;
            }

            return Due(key, _baseline, _saved, time.GetElapsedTime(_changedAt), _inFlight);
        }
    }

    /// <summary>Whether the user has not changed anything yet (content equals the opening baseline).</summary>
    public bool Unedited(ulong key)
    {
        lock (_gate)
        {
            return _baseline == key;
        }
    }

    /// <summary>Whether the content is what the server already has.</summary>
    public bool IsSaved(ulong key)
    {
        lock (_gate)
        {
            return _saved == key;
        }
    }

    /// <summary>The system changed the content (a signature was added): still unedited, at the new content.</summary>
    public void Rebase(ulong key)
    {
        lock (_gate)
        {
            if (_saved is not null && _saved == _baseline)
            {
                _saved = key;
            }

            _baseline = key;
        }
    }

    /// <summary>A save or send starts; returns the generation to hand to <see cref="Finish"/>.</summary>
    public ulong Begin()
    {
        lock (_gate)
        {
            _inFlight = true;
            _flightKey = null;
            return _generation;
        }
    }

    /// <summary>A draft save of content <paramref name="key"/> starts.</summary>
    public ulong BeginSave(ulong key)
    {
        lock (_gate)
        {
            _inFlight = true;
            _flightKey = key;
            return _generation;
        }
    }

    /// <summary>Something to do once the save in flight is done (the last request wins).</summary>
    public void Queue(Queued queued)
    {
        ArgumentNullException.ThrowIfNull(queued);
        lock (_gate)
        {
            _queued = queued;
        }
    }

    /// <summary>A save or send of generation <paramref name="generation"/> finished; <paramref name="savedKey"/> is the content now on the server. Returns what was queued behind it (null for a stale generation).</summary>
    public Queued? Finish(ulong generation, ulong? savedKey)
    {
        lock (_gate)
        {
            if (_generation != generation)
            {
                return null;
            }

            _inFlight = false;
            _flightKey = null;
            if (savedKey is not null)
            {
                _saved = savedKey;
            }

            var q = _queued;
            _queued = null;
            return q;
        }
    }

    /// <summary>A new composer replaces one whose save is still in flight: that save's generation and what it must still do, then resets; null when nothing is in flight.</summary>
    public (ulong Generation, Leftover Action)? ResetLeftover(ulong key)
    {
        lock (_gate)
        {
            (ulong, Leftover)? result = null;
            if (_inFlight)
            {
                var unsaved = key != _saved && key != _flightKey && key != _baseline;

                // a send in flight (no key) carries the content itself
                unsaved = unsaved && _flightKey is not null;
                if (LeftoverFor(_queued, unsaved) is { } l)
                {
                    result = (_generation, l);
                }
            }

            ResetLocked();
            return result;
        }
    }

    private void ResetLocked()
    {
        _generation++;
        _baseline = null;
        _saved = null;
        _key = null;
        _hasChanged = false;
        _inFlight = false;
        _flightKey = null;
        _queued = null;
    }
}

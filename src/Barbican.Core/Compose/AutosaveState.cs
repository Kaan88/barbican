// Mirrors: Rust autosave.rs (QUIET :12, due :67, reset_leftover :78, begin/finish :180-197), Bulwark components/email/email-composer.tsx (2 s autosave debounce, one save at a time). Contract stub from the M3 plan section 4.3; owner send-pipeline replaces the bodies.

using Barbican.Core.Common;

// CA1822: the members are stubs; the real ones read and write the instance's state.
#pragma warning disable CA1822

namespace Barbican.Core.Compose;

/// <summary>
/// The autosave bookkeeping of one composer window (Rust kept it in thread-locals; here it is an instance the composer owns): content keys (a 64-bit hash of the fields and the body), the baseline the composer opened with,
/// what the server has, the save in flight, the work queued behind it, and the generation that makes a late save of a replaced composer harmless. Stub until stream send-pipeline lands.
/// </summary>
public sealed class AutosaveState(TimeProvider time)
{
    /// <summary>Bulwark's autosave delay.</summary>
    public static TimeSpan Quiet { get; } = TimeSpan.FromMilliseconds(2000);

    /// <summary>The tick of the composer's timer.</summary>
    public static TimeSpan Tick { get; } = TimeSpan.FromMilliseconds(500);

    public TimeProvider Time => time;

    public bool InFlight => throw new PortPendingException("AutosaveState.InFlight");

    public ulong Generation => throw new PortPendingException("AutosaveState.Generation");

    /// <summary>Whether an autosave is due: not in flight, the content differs from both the baseline and what the server has, and the composer was quiet for <see cref="Quiet"/>.</summary>
    public static bool Due(ulong key, ulong? baseline, ulong? saved, TimeSpan quiet, bool inFlight) => throw new PortPendingException("AutosaveState.Due");

    /// <summary>Pure decision for <see cref="ResetLeftover"/>: <paramref name="unsaved"/> = the old composer holds content the save in flight does not carry. A queued send is kept as a draft instead of a send nobody sees.</summary>
    public static Leftover? LeftoverFor(Queued? queued, bool unsaved) => throw new PortPendingException("AutosaveState.LeftoverFor");

    /// <summary>A new composer: forget everything about the previous one (the generation advances).</summary>
    public void Reset() => throw new PortPendingException("AutosaveState.Reset");

    /// <summary>A save or send starts; returns the generation to hand to <see cref="Finish"/>.</summary>
    public ulong Begin() => throw new PortPendingException("AutosaveState.Begin");

    /// <summary>A draft save of content <paramref name="key"/> starts.</summary>
    public ulong BeginSave(ulong key) => throw new PortPendingException("AutosaveState.BeginSave");

    /// <summary>Something to do once the save in flight is done (the last request wins).</summary>
    public void Queue(Queued queued) => throw new PortPendingException("AutosaveState.Queue");

    /// <summary>A save or send of generation <paramref name="generation"/> finished; <paramref name="savedKey"/> is the content now on the server. Returns what was queued behind it (null for a stale generation).</summary>
    public Queued? Finish(ulong generation, ulong? savedKey) => throw new PortPendingException("AutosaveState.Finish");

    /// <summary>A new composer replaces one whose save is still in flight: that save's generation and what it must still do, then resets; null when nothing is in flight.</summary>
    public (ulong Generation, Leftover Action)? ResetLeftover(ulong key) => throw new PortPendingException("AutosaveState.ResetLeftover");
}

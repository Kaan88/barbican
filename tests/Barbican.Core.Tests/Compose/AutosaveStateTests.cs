using Barbican.Core.Compose;

namespace Barbican.Core.Tests.Compose;

/// <summary>Rust src/autosave.rs tests, ported (M3 stream send-pipeline). The Rust thread-local state became the <see cref="AutosaveState"/> instance (D-241).</summary>
public sealed class AutosaveStateTests
{
    private static AutosaveState New() => new(TimeProvider.System);

    [Fact]
    [Red("send-pipeline")]
    [RustTest("autosave::debounce_rules")]
    public void Debounce_rules()
    {
        var q = AutosaveState.Quiet;

        // unedited: nothing to save
        Assert.False(AutosaveState.Due(1, 1, null, q, false));

        // edited and quiet for 2 s
        Assert.True(AutosaveState.Due(2, 1, null, q, false));

        // still typing
        Assert.False(AutosaveState.Due(2, 1, null, TimeSpan.FromMilliseconds(1500), false));

        // already on the server
        Assert.False(AutosaveState.Due(2, 1, 2, q, false));

        // a save is on its way
        Assert.False(AutosaveState.Due(2, 1, null, q, true));

        // back to the opened content of a reopened draft that was saved meanwhile
        Assert.False(AutosaveState.Due(1, 1, 3, q, false));
    }

    [Fact]
    [Red("send-pipeline")]
    [RustTest("autosave::generations")]
    public void Generations()
    {
        var s = New();
        s.Reset();
        var g = s.Begin();
        s.Queue(new Queued.Send());
        Assert.True(s.InFlight);
        Assert.Equal<Queued?>(new Queued.Send(), s.Finish(g, 7));
        Assert.False(s.InFlight);
        var g2 = s.Begin();
        s.Reset();

        // a save from the previous composer changes nothing
        Assert.Null(s.Finish(g2, 9));
        Assert.False(s.InFlight);
    }

    [Fact]
    [Red("send-pipeline")]
    [RustTest("autosave::reopen_during_a_save_keeps_the_queued_work")]
    public void Reopen_during_a_save_keeps_the_queued_work()
    {
        // queue, then reset, then finish: the queued save survives as a leftover
        var s = New();
        s.Reset();
        var g = s.BeginSave(5);
        s.Queue(new Queued.Save(false));
        var left = s.ResetLeftover(5);
        Assert.Equal<(ulong Generation, Leftover Action)?>((g, Leftover.Save), left);
        Assert.Null(s.Finish(g, 5));

        // typed after the save started, nothing queued: still saved
        s.Reset();
        g = s.BeginSave(5);
        Assert.Equal<(ulong Generation, Leftover Action)?>((g, Leftover.Save), s.ResetLeftover(6));

        // the save in flight already carries everything: nothing more to do
        s.Reset();
        s.BeginSave(5);
        Assert.Null(s.ResetLeftover(5));

        // a discard queued behind it removes the draft the save creates
        s.Reset();
        g = s.BeginSave(5);
        s.Queue(new Queued.Discard());
        Assert.Equal<(ulong Generation, Leftover Action)?>((g, Leftover.Discard), s.ResetLeftover(7));

        // nothing in flight: a plain reset
        s.Reset();
        Assert.Null(s.ResetLeftover(1));
        Assert.Equal<Leftover?>(Leftover.Save, AutosaveState.LeftoverFor(new Queued.Send(), false));
    }
}

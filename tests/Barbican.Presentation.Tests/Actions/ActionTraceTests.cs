// Trace scenarios of M2 (M2-plan §8: "trace-diff ... new traces for delete_undo and move against Rust-derived sequences", mail.rs
// apply_confirmed :978): the JMAP requests a delete, its undo and a move send through the real MailActionService and MailWriter over
// FakeJmap. tools/trace-diff.py compares spec/traces/M2/<name>.cs.txt with the hand-derived <name>.rust.txt. These tests fail when the
// sequence no longer matches the committed .cs.txt; run them with BARBICAN_TRACE_WRITE=1 to rewrite the files, then run
// `python tools/trace-diff.py --all spec/traces/M2`. Undo has no Rust counterpart (main.rs:1354 undo_toast is undo-send only); its
// expected line follows Bulwark stores/email-store.ts:5878 undoMove (one Email/set putting the folders back). Owner: M2 verify.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
namespace Barbican.Presentation.Tests.Actions;

public sealed class ActionTraceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delete_then_undo_send_one_email_set_each()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var t = new Tracer(h);
        t.Phase("delete");
        Assert.True((await h.Actions.DeleteAsync(h.Pick(0), Ct)).IsOk);
        t.Phase("undo");
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Check("delete_undo", t.Done());
    }

    [Fact]
    public async Task A_move_sends_one_email_set()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var t = new Tracer(h);
        t.Phase("move");
        Assert.True((await h.Actions.MoveAsync(h.Pick(0), h.Folder("archive"), Ct)).IsOk);
        Check("move", t.Done());
    }

    // The API requests since the scenario started, one line per request, under `@phase` markers (the M1 trace format; method names only).
    private sealed class Tracer(ActionHarness h)
    {
        private readonly List<string> _lines = [];
        private int _from = h.Server.ApiCalls.Count;

        public void Phase(string name)
        {
            Flush();
            _lines.Add("@phase " + name);
        }

        public List<string> Done()
        {
            Flush();
            return _lines;
        }

        private void Flush()
        {
            var calls = h.Server.ApiCalls;
            for (var i = _from; i < calls.Count; i++)
            {
                _lines.Add(calls[i] + " -> " + calls[i]);
            }

            _from = calls.Count;
        }
    }

    private static void Check(string name, List<string> lines)
    {
        var path = Path.Combine(RepoRoot(), "spec", "traces", "M2", name + ".cs.txt");
        var header = $"# C# trace of scenario \"{name}\", written by tests/Barbican.Presentation.Tests/Actions/ActionTraceTests.cs (FakeJmap).";
        string[] expected = [header, .. lines];
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_TRACE_WRITE"), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join('\n', expected) + "\n");
        }

        Assert.True(File.Exists(path), $"{path} is missing: run the trace scenarios with BARBICAN_TRACE_WRITE=1");
        Assert.Equal(File.ReadAllLines(path), expected, StringComparer.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Barbican.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Barbican.slnx not found above " + AppContext.BaseDirectory);
    }
}

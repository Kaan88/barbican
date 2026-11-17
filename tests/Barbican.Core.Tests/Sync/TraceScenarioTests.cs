// Headless trace scenarios (PORT-PLAN.md §4.6): the open, folder-switch and push sequences of the engine over the fake server on a
// virtual clock, written as BARBICAN_JMAP_TRACE lines with `@phase` markers. tools/trace-diff.py compares spec/traces/M1/<name>.cs.txt
// with the Rust-derived <name>.rust.txt. These tests fail when the engine's sequence no longer matches the committed .cs.txt; run them
// with BARBICAN_TRACE_WRITE=1 to rewrite the files, then run `python tools/trace-diff.py --all spec/traces/M1`. Owner jmap.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sync;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Sync;

public sealed class TraceScenarioTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Scenario(Fake f, MailService service, MailSync sync, RecordingHooks hooks)
    {
        private readonly List<string> _out = [];
        private int _from;

        public Fake F { get; } = f;

        public MailService Service { get; } = service;

        public MailSync Sync { get; } = sync;

        public Task? Run { get; set; }

        public CancellationTokenSource Stop { get; } = new();

        public int Idle => hooks.Seen.Count(h => string.Equals(h.Hook, HookNames.OnSyncStateChange, StringComparison.Ordinal) && h.Payload is SyncStatePayload { State: "idle" });

        public IReadOnlyList<string> Lines
        {
            get
            {
                Flush();
                return _out;
            }
        }

        /// <summary>Drops what was traced so far (the setup of a scenario that starts later).</summary>
        public void Forget()
        {
            lock (F.Trace)
            {
                _from = F.Trace.Count;
            }

            _out.Clear();
        }

        public void Phase(string name)
        {
            Flush();
            _out.Add("@phase " + name);
        }

        public bool Traced(string prefix)
        {
            lock (F.Trace)
            {
                return F.Trace.Skip(_from).Any(l => l.StartsWith(prefix, StringComparison.Ordinal));
            }
        }

        public async Task SettleAsync()
        {
            var idle = Idle;
            ((Microsoft.Extensions.Time.Testing.FakeTimeProvider)F.Time).Advance(MailSync.Coalesce);
            await Poll.UntilAsync(() => Idle > idle, "the coalesced sync");
        }

        public async Task StopAsync()
        {
            await Stop.CancelAsync();
            if (Run is not null)
            {
                // the run loop was started by this scenario; awaiting it here is the shutdown
#pragma warning disable VSTHRD003
                await Run;
#pragma warning restore VSTHRD003
            }

            Stop.Dispose();
            Sync.Dispose();
        }

        private void Flush()
        {
            lock (F.Trace)
            {
                _out.AddRange(F.Trace.Skip(_from));
                _from = F.Trace.Count;
            }
        }
    }

    /// <summary>Mirrors Rust main.rs:2015 finish_login: folders and the inbox, identities, the first new-mail check, then push.</summary>
    private static async Task<Scenario> OpenAsync()
    {
        var clock = Fake.VirtualClock();
        var f = await Fake.StartAsync(time: clock);
        var service = new MailService(f.Client, clock);
        var hooks = new RecordingHooks();
        var s = new Scenario(f, service, new MailSync(service, hooks, clock, new PushBackoff(_ => 0)), hooks);
        await service.GetMailboxesAsync(Ct);
        var inbox = new ListQuery { MailboxId = f.Server.Store.MailboxId("inbox"), PageSize = 50 };
        s.Sync.Watch(inbox, (await service.QueryAsync(inbox, null, Ct)).Value!);
        await service.GetIdentitiesAsync(Ct);
        await s.Sync.NewMail.CheckAsync(Ct);
        s.Phase("catch-up");
        s.Run = s.Sync.RunAsync(s.Stop.Token);
        await Poll.UntilAsync(() => s.Traced("PUSH open"), "the stream to open");
        await s.SettleAsync();
        return s;
    }

    [Fact]
    public async Task Open()
    {
        var s = await OpenAsync();
        Check("open", s.Lines);
        await s.StopAsync();
    }

    [Fact]
    public async Task Switch()
    {
        var s = await OpenAsync();
        s.Forget();
        s.Phase("switch");
        var archive = new ListQuery { MailboxId = s.F.Server.Store.MailboxId("archive"), PageSize = 50 };
        s.Sync.Watch(archive, (await s.Service.QueryAsync(archive, null, Ct)).Value!);
        Check("switch", s.Lines);
        await s.StopAsync();
    }

    [Fact]
    public async Task Push()
    {
        var s = await OpenAsync();
        s.Forget();
        s.Phase("push");
        s.F.Server.Store.Deliver(new FakeMail { Subject = "Pushed", ReceivedAt = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero) });
        await Poll.UntilAsync(() => s.Traced("PUSH state "), "the push");
        s.Phase("refresh");
        await s.SettleAsync();
        Check("push", s.Lines);
        await s.StopAsync();
    }

    private static void Check(string name, IReadOnlyList<string> lines)
    {
        var path = Path.Combine(RepoRoot(), "spec", "traces", "M1", name + ".cs.txt");
        var header = $"# C# engine trace of scenario \"{name}\", written by tests/Barbican.Core.Tests/Sync/TraceScenarioTests.cs (FakeJmap, virtual clock).";
        string[] expected = [header, .. lines];
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_TRACE_WRITE"), "1", StringComparison.Ordinal))
        {
            File.WriteAllText(path, string.Join('\n', expected) + "\n");
        }

        Assert.True(File.Exists(path), $"{path} is missing: run the trace scenarios with BARBICAN_TRACE_WRITE=1");
        var committed = File.ReadAllLines(path);
        Assert.Equal(committed, expected, StringComparer.Ordinal);
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

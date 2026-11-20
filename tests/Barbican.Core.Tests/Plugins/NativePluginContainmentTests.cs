// New tests (M2 plan 6.5 exit tests; fable-review-2 R2-10): the native plugin host contains a throwing and a sleeping plugin within the budget, plugins
// subscribe to catalogue hooks through the bus (on the thread pool), a fail-closed intercept stops the action, and the catalogue's budgets are what
// Bulwark lib/plugin-hooks.ts:62-66, :169, :186-188 says. Not ports of Rust tests (the Rust host had no budgets). Owner unified.

using System.Diagnostics;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Native;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Barbican.Plugins;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins;

public sealed class NativePluginContainmentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly EmailDetailsView SpamMail = new([new HeaderView("X-Spam-Status", "Yes, score=7.5 required=5.0")]);

    private static NativePluginHost Host(HookBus? bus, TimeSpan budget, params IBarbicanPlugin[] plugins) =>
        NativePluginHost.FromInstances(
            new NativePluginHostOptions { Bus = bus, DetailsBudget = budget, DataRoot = Path.Combine(Path.GetTempPath(), "barbican-plugin-tests") },
            plugins.Select(p => (p, Path.Combine(Path.GetTempPath(), p.Manifest.Id))));

    [Fact]
    public async Task A_throwing_plugin_adds_no_rows_and_the_others_still_answer()
    {
        using var host = Host(null, TimeSpan.FromSeconds(5), new TestPlugin("thrower") { Details = _ => throw new InvalidOperationException("boom") }, new TestPlugin("rows"));
        var failures = new List<PluginFailedEventArgs>();
        host.PluginFailed += (_, e) => failures.Add(e);
        var rows = await host.EmailDetailsAsync(SpamMail, Ct);
        Assert.Equal("rows", Assert.Single(rows).Label);
        var f = Assert.Single(failures);
        Assert.Equal("thrower", f.PluginId);
        Assert.IsType<InvalidOperationException>(f.Error);
    }

    [Fact]
    public async Task A_plugin_that_blocks_before_its_first_await_is_bounded_by_the_budget()
    {
        // Thread.Sleep before any await: started on the caller's thread it would hold the caller for 5 s (R2-10); on the pool the budget wins
        using var release = new ManualResetEventSlim();
        using var host = Host(null, TimeSpan.FromMilliseconds(200), new TestPlugin("sleeper") { Details = _ => { release.Wait(TimeSpan.FromSeconds(5)); return []; } }, new TestPlugin("rows"));
        var failures = new List<PluginFailedEventArgs>();
        host.PluginFailed += (_, e) => failures.Add(e);
        var watch = Stopwatch.StartNew();
        var rows = await host.EmailDetailsAsync(SpamMail, Ct);
        watch.Stop();
        release.Set();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"the call took {watch.Elapsed}");
        Assert.Equal("rows", Assert.Single(rows).Label);
        Assert.IsType<TimeoutException>(Assert.Single(failures).Error);
    }

    [Fact]
    public async Task Archive_deadline_leaves_slow_cancellation_callbacks_in_the_teardown_drain()
    {
        var time = new FakeTimeProvider();
        using var host = NativePluginHost.CreateArchiveHost(new() { Time = time, DataRoot = Path.GetTempPath() });
        var identity = new PluginPackageIdentity("fixture", "1.0.0", new string('a', 64));
        await using var binding = PluginRuntimeBinding.Create("fixture",
            new(identity, 1, true, [], [], PluginExecutionTier.NativeFullTrust), PluginFrameKind.Background);
        var plugin = new LoadedPlugin(new(identity.Id, "Fixture", identity.Version, []), new TestPlugin(identity.Id), null, binding);
        plugin.Publish();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCancellation = new ManualResetEventSlim();
        var budget = TimeSpan.FromSeconds(1);

        var invocation = host.RunArchiveAsync(plugin, NativePluginHost.EmailDetailsHook, budget, async token =>
        {
            _ = token.Register(() =>
            {
                cancellationEntered.TrySetResult();
                releaseCancellation.Wait(TimeSpan.FromSeconds(10));
                throw new InvalidOperationException("Synthetic cancellation failure.");
            });
            entered.TrySetResult();
#pragma warning disable VSTHRD003 // The test explicitly releases this pool task without the test synchronization context.
            await completeWork.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            return true;
        }, Ct);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), Ct);
            time.Advance(budget);
            await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(3), Ct);
            Assert.False((await invocation.WaitAsync(TimeSpan.FromSeconds(3), Ct)).Done);
            completeWork.TrySetResult();
            var drain = plugin.DrainCallsAsync();
            Assert.False(drain.IsCompleted);
            Assert.False(plugin.TryEnterCall());
        }
        finally
        {
            completeWork.TrySetResult();
            releaseCancellation.Set();
            await plugin.DrainCallsAsync().WaitAsync(TimeSpan.FromSeconds(3), Ct);
        }
    }

    [Fact]
    public async Task A_disabled_plugin_is_listed_and_never_called()
    {
        var called = false;
        var plugin = new TestPlugin("off") { Details = _ => { called = true; return []; } };
        using var host = NativePluginHost.FromInstances(
            new NativePluginHostOptions { Disabled = new HashSet<string>(StringComparer.Ordinal) { "off" } },
            [(plugin, Path.GetTempPath())]);
        Assert.False(Assert.Single(host.Plugins).Enabled);
        Assert.Empty(await host.EmailDetailsAsync(SpamMail, Ct));
        Assert.False(called);
        Assert.False(plugin.Initialized);
    }

    [Fact]
    public async Task A_plugin_observes_a_catalogue_hook_with_a_json_payload()
    {
        var bus = new HookBus();
        var seen = new TaskCompletionSource<HookEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new TestPlugin("watcher", HookNames.OnAccountRemove)
        {
            Subscribe = hooks => hooks.Observe(HookNames.OnAccountRemove, (e, _) =>
            {
                seen.TrySetResult(e);
                return Task.CompletedTask;
            }),
        };
        using var host = Host(bus, TimeSpan.FromSeconds(5), plugin);
        await host.EmailDetailsAsync(SpamMail, Ct); // waits for the start
        Assert.True(bus.HasHandlers(HookNames.OnAccountRemove));
        await bus.NotifyAsync(HookNames.OnAccountRemove, new { AccountId = "a1", Email = "me@example.org" }, Ct);
        var e = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(HookNames.OnAccountRemove, e.Hook);
        Assert.Contains("\"accountId\":\"a1\"", e.PayloadJson, StringComparison.Ordinal);

        // unloading the host takes its subscriptions away
        host.Dispose();
        Assert.False(bus.HasHandlers(HookNames.OnAccountRemove));
    }

    [Fact]
    public async Task A_plugin_cannot_subscribe_to_a_hook_its_manifest_does_not_list_and_is_then_never_called()
    {
        var bus = new HookBus();
        var plugin = new TestPlugin("sneaky") { Subscribe = hooks => hooks.Observe(HookNames.OnLogin, (_, _) => Task.CompletedTask) };
        using var host = Host(bus, TimeSpan.FromSeconds(5), plugin);
        var failures = new List<PluginFailedEventArgs>();
        host.PluginFailed += (_, e) => failures.Add(e);
        Assert.Empty(await host.EmailDetailsAsync(SpamMail, Ct));
        Assert.False(bus.HasHandlers(HookNames.OnLogin));
    }

    [Fact]
    public async Task A_fail_closed_intercept_stops_the_action_when_the_plugin_throws_and_an_ordinary_one_lets_it_through()
    {
        var bus = new HookBus();
        var plugin = new TestPlugin("guard", HookNames.OnBeforeEmailSend, HookNames.OnBeforeEmailDelete)
        {
            Subscribe = hooks =>
            {
                hooks.Intercept(HookNames.OnBeforeEmailSend, (_, _) => throw new InvalidOperationException("signing service down"));
                hooks.Intercept(HookNames.OnBeforeEmailDelete, (_, _) => throw new InvalidOperationException("broken"));
            },
        };
        using var host = Host(bus, TimeSpan.FromSeconds(5), plugin);
        await host.EmailDetailsAsync(SpamMail, Ct);

        // Bulwark lib/plugin-hooks.ts:460 onBeforeEmailSend is failClosed: a send cannot be taken back
        Assert.True(HookPolicies.For(HookNames.OnBeforeEmailSend).FailClosed);
        Assert.False(await bus.InterceptAsync(HookNames.OnBeforeEmailSend, new { Subject = "x" }, Ct));
        Assert.True(await bus.InterceptAsync(HookNames.OnBeforeEmailDelete, new { Id = "e1" }, Ct));
    }

    [Fact]
    public async Task A_fail_closed_intercept_that_runs_out_of_time_stops_the_action()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var bus = new HookBus(clock);
        var never = new TaskCompletionSource<bool>();
        using var a = bus.InterceptAny(HookNames.OnBeforeEmailSend, (_, t) => new ValueTask<bool>(never.Task.WaitAsync(t)));
        var asked = bus.InterceptAsync(HookNames.OnBeforeEmailSend, new { Subject = "x" }, Ct).AsTask();
        await Task.Delay(20, Ct);

        // the send hook waits up to 10 minutes (a signing dialog); one second short of it the answer is still open
        clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        await Task.Delay(20, Ct);
        Assert.False(asked.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(await asked.WaitAsync(TimeSpan.FromSeconds(10), Ct));
    }

    [Fact]
    public async Task An_observer_that_blocks_on_the_pool_is_abandoned_after_its_budget()
    {
        var bus = new HookBus(handlerTimeout: TimeSpan.FromMilliseconds(200));
        using var release = new ManualResetEventSlim();
        var plugin = new TestPlugin("blocker", HookNames.OnAccountRemove)
        {
            Subscribe = hooks => hooks.Observe(HookNames.OnAccountRemove, (_, _) =>
            {
                release.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
                return Task.CompletedTask;
            }),
        };
        using var host = Host(bus, TimeSpan.FromSeconds(5), plugin);
        await host.EmailDetailsAsync(SpamMail, Ct);
        var watch = Stopwatch.StartNew();
        await bus.NotifyAsync(HookNames.OnAccountRemove, new { AccountId = "a1" }, Ct);
        watch.Stop();
        release.Set();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"the hook held the caller for {watch.Elapsed}");
    }

    [Fact]
    public void The_catalogue_budgets_follow_bulwark()
    {
        var rows = RepoFiles.ReadCsv("spec/plugins/hooks.csv");
        foreach (var row in rows)
        {
            var policy = HookPolicies.For(row["name"]);
            Assert.Equal(TimeSpan.FromMilliseconds(int.Parse(row["budget_ms"], System.Globalization.CultureInfo.InvariantCulture)), policy.Budget);
            Assert.Equal(string.Equals(row["fail_closed"], "true", StringComparison.Ordinal), policy.FailClosed);
            var expected = row["name"] switch
            {
                "onBeforeEmailSend" or "onComposeSend" => 600_000,
                "onBeforeBlobUpload" => 900_000,
                "onBeforeCalendarAlert" => 5_000,
                _ => row["kind"] switch { "intercept" => 60_000, "takeover" => 600_000, _ => 5_000 },
            };
            Assert.Equal(expected, (int)policy.Budget.TotalMilliseconds);
        }

        Assert.Equal(["onBeforeEmailSend", "onComposeSend"], rows.Where(r => string.Equals(r["fail_closed"], "true", StringComparison.Ordinal)).Select(r => r["name"]).ToList());
        Assert.Equal(HookPolicies.Default, HookPolicies.For("notAHook"));
    }

    [Fact]
    public async Task A_plugin_whose_manifest_throws_is_skipped_and_the_others_load()
    {
        using var host = NativePluginHost.FromInstances(
            new NativePluginHostOptions(),
            [(new BrokenManifestPlugin(), Path.Combine(Path.GetTempPath(), "broken")), (new TestPlugin("rows"), Path.Combine(Path.GetTempPath(), "rows"))]);
        Assert.Equal("rows", Assert.Single(host.Plugins).Id);
        Assert.Contains(host.LoadErrors, e => e.StartsWith("broken:", StringComparison.Ordinal));
        Assert.Equal("rows", Assert.Single(await host.EmailDetailsAsync(SpamMail, Ct)).Label);
    }

    private sealed class BrokenManifestPlugin : IBarbicanPlugin
    {
#pragma warning disable CA1065 // a getter that throws is what this test plugin is for
        public PluginManifest Manifest => throw new InvalidOperationException("no manifest");
#pragma warning restore CA1065

        public Task InitializeAsync(PluginContext context, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<EmailDetailRow>> OnEmailDetailsAsync(EmailDetailsView email, CancellationToken ct) => Task.FromResult<IReadOnlyList<EmailDetailRow>>([]);
    }

    private sealed class TestPlugin(string id, params string[] hooks) : IBarbicanPlugin
    {
        public PluginManifest Manifest { get; } = new(1, id, id, "1.0", "test", [NativePluginHost.EmailDetailsHook, .. hooks]);

        public Func<EmailDetailsView, IReadOnlyList<EmailDetailRow>> Details { get; init; } = _ => [new EmailDetailRow(id, "value", "neutral")];

        public Action<IPluginHooks>? Subscribe { get; init; }

        public bool Initialized { get; private set; }

        public Task InitializeAsync(PluginContext context, CancellationToken ct)
        {
            Initialized = true;
            Subscribe?.Invoke(context.Hooks);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmailDetailRow>> OnEmailDetailsAsync(EmailDetailsView email, CancellationToken ct) => Task.FromResult(Details(email));
    }
}

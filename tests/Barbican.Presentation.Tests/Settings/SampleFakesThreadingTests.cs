// D-M6-00 (M6 plan section 3.5): every Sample* fake raises its events from a pool thread and completes its calls off the caller's context, as the real services do (the M5 sample stores raised on the UI thread and hid two bugs). Green in the plan commit. Owner integrator.

using System.Text.Json.Nodes;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Folders;
using Barbican.Presentation.Settings.Sample;

namespace Barbican.Presentation.Tests.Settings;

public sealed class SampleFakesThreadingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task AssertRaisedOnPoolAsync(Action<Action<bool>> subscribe, Func<Task> write)
    {
        var raised = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscribe(onPool => raised.TrySetResult(onPool));
        await write();
        Assert.True(await raised.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct), "the event was not raised on a pool thread");
    }

    [Fact]
    public async Task Setting_values_raise_changed_on_a_pool_thread()
    {
        var fake = new SampleSettingValues();
        await AssertRaisedOnPoolAsync(on => fake.Changed += (_, _) => on(Thread.CurrentThread.IsThreadPoolThread), () => fake.SetAsync("theme", JsonValue.Create("dark"), Ct));
        Assert.Equal("dark", (string?)fake.Stored("theme"));
        Assert.Null(fake.Stored("other"));
    }

    [Fact]
    public async Task Identities_raise_changed_on_a_pool_thread()
    {
        var fake = new SampleAccountIdentities();
        await AssertRaisedOnPoolAsync(on => fake.Changed += (_, _) => on(Thread.CurrentThread.IsThreadPoolThread), () => fake.CreateAsync("a", new AccountIdentityEdit("N", "n@example.org", null, null, null, null), Ct));
        Assert.Equal(3, (await fake.ListAsync("a", Ct)).Value!.Count);
    }

    [Fact]
    public async Task Filters_raise_changed_on_a_pool_thread()
    {
        var fake = new SampleFilterService();
        var loaded = await fake.LoadAsync("a", Ct);
        await AssertRaisedOnPoolAsync(on => fake.Changed += (_, _) => on(Thread.CurrentThread.IsThreadPoolThread), () => fake.SaveAsync("a", [], loaded.Value!.State, Ct));
        Assert.Empty((await fake.LoadAsync("a", Ct)).Value!.Rules);
    }

    [Fact]
    public async Task Folder_writes_raise_changed_on_a_pool_thread()
    {
        var fake = new SampleFolderAdmin();
        await AssertRaisedOnPoolAsync(on => fake.Changed += (_, _) => on(Thread.CurrentThread.IsThreadPoolThread), () => fake.SetPrefAsync("a", "f1", FolderAdminPref.Pin, "true", Ct));
        Assert.Contains("f1", (await fake.PrefsAsync("a", Ct)).Value!.Pinned);
    }
}

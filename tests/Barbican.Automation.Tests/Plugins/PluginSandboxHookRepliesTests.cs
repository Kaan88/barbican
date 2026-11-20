using System.Text.Json;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginSandboxHookRepliesTests
{
    private static PluginRuntimeBinding Binding() => PluginRuntimeBinding.Create("auto-8931", new PluginGrantSnapshot(new("fixture", "1.0.0", new string('a', 64)), 1, true, [], []), PluginFrameKind.Background);
    private static PluginValue Reply(string id, bool ok = true)
    {
        using var json = JsonDocument.Parse($$"""{"type":"hook-result","id":"{{id}}","ok":{{(ok ? "true" : "false")}},"error":"failure"}""");
        return PluginValue.FromJson(json.RootElement);
    }

    [Fact]
    public async Task Only_posted_owned_pending_reply_completes_once()
    {
        var owner = Binding(); var sibling = Binding();
        using var replies = new PluginSandboxHookReplies(owner, 1);
        Assert.True(replies.TryBegin(CancellationToken.None, out var id, out var task));
        Assert.False(replies.TryBegin(CancellationToken.None, out _, out _));
        Assert.False(replies.TryComplete(owner, id, Reply(id)));
        Assert.True(replies.TryMarkPosted(id));
        Assert.False(replies.TryMarkPosted(id));
        Assert.False(replies.TryComplete(sibling, id, Reply(id)));
        Assert.True(replies.TryComplete(owner, id, Reply(id, false)));
        Assert.False((await task).Property("ok").BooleanValue);
        Assert.False(replies.TryComplete(owner, id, Reply(id)));
        Assert.True(replies.TryBegin(CancellationToken.None, out var next, out _));
        Assert.NotEqual(id, next, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Cancellation_removes_exact_request_and_close_cancels_waiters()
    {
        var owner = Binding(); using var replies = new PluginSandboxHookReplies(owner);
        using var cancel = new CancellationTokenSource();
        Assert.True(replies.TryBegin(cancel.Token, out var old, out var canceled));
        await cancel.CancelAsync();
#pragma warning disable VSTHRD003 // Observe requests that were started before testing cancellation.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(replies.TryMarkPosted(old));
        Assert.True(replies.TryBegin(CancellationToken.None, out var next, out var pending));
        Assert.False(replies.TryComplete(owner, old, Reply(old)));
        Assert.True(replies.TryMarkPosted(next));
        replies.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(replies.TryBegin(CancellationToken.None, out _, out _));
    }

    [Fact]
    public async Task Binding_disposal_cancels_pending_and_rejects_late_reply()
    {
        var owner = Binding(); using var replies = new PluginSandboxHookReplies(owner);
        Assert.True(replies.TryBegin(CancellationToken.None, out var id, out var pending));
        Assert.True(replies.TryMarkPosted(id));
        await owner.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(replies.TryComplete(owner, id, Reply(id)));
        Assert.False(replies.TryBegin(CancellationToken.None, out _, out _));
    }
#pragma warning restore VSTHRD003
}

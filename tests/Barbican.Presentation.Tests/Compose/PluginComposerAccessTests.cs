using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Core.Editor.Spell;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Editor;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Compose;

public sealed class PluginComposerAccessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PluginRuntimeBinding Binding() => PluginRuntimeBinding.Create("fixture", new PluginGrantSnapshot(
        new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, [], ["acc"]), PluginFrameKind.Background);

    [Fact]
    public async Task Subject_edits_including_ABA_invalidate_every_previous_revision()
    {
        using var ui = new UiThread();
        var harness = new ComposerHarness();
        using var current = harness.Open(ComposerHarness.Seed(subject: "A"));
        var composer = new TestComposer(ui, current);
        using var access = new PluginComposerAccess(composer, ui, new Sessions());
        await using var binding = Binding();
        Assert.True(access.Expose(binding, current.Id, current.AccountKey));
        var handle = Assert.IsAssignableFrom<IPluginComposerHandle>(access.Resolve(binding, current.Id));
        Assert.Same(handle, access.Resolve(binding, current.Id));
        var first = await handle.GetSnapshotAsync(Ct);
        Assert.Equal("A", first.Subject);
        await ui.InvokeAsync(() => { current.Subject = "B"; return Task.CompletedTask; });
        var second = await handle.GetSnapshotAsync(Ct);
        Assert.Equal("B", second.Subject);
        Assert.NotEqual(first.Revision, second.Revision, StringComparer.Ordinal);
        Assert.False(await handle.SetSubjectAsync("stale", first.Revision, Ct));
        await ui.InvokeAsync(() => { current.Subject = "A"; return Task.CompletedTask; });
        var third = await handle.GetSnapshotAsync(Ct);
        Assert.Equal("A", third.Subject);
        Assert.NotEqual(first.Revision, third.Revision, StringComparer.Ordinal);
        Assert.NotEqual(second.Revision, third.Revision, StringComparer.Ordinal);
        Assert.False(await handle.SetSubjectAsync("stale", first.Revision, Ct));
        Assert.False(await handle.SetSubjectAsync("stale", second.Revision, Ct));
        Assert.True(await handle.SetSubjectAsync("fresh", third.Revision, Ct));
        Assert.Equal("fresh", (await handle.GetSnapshotAsync(Ct)).Subject);
        Assert.Empty(ui.Unhandled);
    }

    [Fact]
    public async Task Exposure_is_binding_specific_and_replacement_revokes_old_handle()
    {
        using var ui = new UiThread();
        var harness = new ComposerHarness();
        using var firstComposer = harness.Open(ComposerHarness.Seed(subject: "first"));
        using var replacement = harness.Open(ComposerHarness.Seed(subject: "replacement"));
        var composer = new TestComposer(ui, firstComposer);
        using var access = new PluginComposerAccess(composer, ui, new Sessions());
        await using var binding = Binding();
        await using var foreign = Binding();
        Assert.False(access.Expose(binding, firstComposer.Id, "foreign-account"));
        Assert.True(access.Expose(binding, firstComposer.Id, firstComposer.AccountKey));
        var handle = Assert.IsAssignableFrom<IPluginComposerHandle>(access.Resolve(binding, firstComposer.Id));
        Assert.Null(access.Resolve(foreign, firstComposer.Id));
        var snapshot = await handle.GetSnapshotAsync(Ct);
        await ui.InvokeAsync(() => { composer.Replace(replacement); return Task.CompletedTask; });
        Assert.Null(access.Resolve(binding, firstComposer.Id));
        Assert.False(await handle.SetSubjectAsync("wrong composer", snapshot.Revision, Ct));
        Assert.Equal("replacement", replacement.Subject);
        Assert.True(access.Expose(binding, replacement.Id, replacement.AccountKey));
        Assert.NotSame(handle, access.Resolve(binding, replacement.Id));
        Assert.Empty(ui.Unhandled);
    }

    [Fact]
    public async Task Reply_snapshot_omits_quote_context_and_inline_original_without_removing_authored_duplicates()
    {
        using var ui = new UiThread();
        var harness = new ComposerHarness();
        var signature = Block.Para("Same words");
        signature.Runs[0] = signature.Runs[0] with { IsComposerContext = true };
        var attribution = Block.Para("Generated reply attribution");
        attribution.Runs[0] = attribution.Runs[0] with { IsComposerContext = true };
        var seed = ComposerHarness.Seed(kind: ComposeKind.Reply) with
        {
            Body = new Doc([Block.Para("Same words"), attribution,
                new Block(new BlockKind.Quoted("<p>original</p>", "original", false, true)), signature]),
            Attachments =
            [
                new DraftAttachment { Name = "quoted image", BlobId = "inline-blob", Cid = "fixture@example.org" },
                new DraftAttachment { Name = "attachment", BlobId = "file-blob", Size = 3 },
            ],
        };
        using var draft = OpenRich(harness, seed);
        using var access = new PluginComposerAccess(new TestComposer(ui, draft), ui, new Sessions());
        await using var binding = Binding();
        Assert.True(access.Expose(binding, draft.Id, draft.AccountKey));
        var handle = Assert.IsAssignableFrom<IPluginComposerHandle>(access.Resolve(binding, draft.Id));
        var snapshot = await handle.GetSnapshotAsync(Ct);
        Assert.Equal("Same words", snapshot.Text);
        var file = Assert.Single(snapshot.Attachments);
        Assert.Equal("attachment", file.Name);
        Assert.NotEqual("file-blob", file.Id, StringComparer.Ordinal);
        Assert.False(snapshot.Pending);
    }

    [Fact]
    public async Task Reopened_body_exposes_only_newly_authored_text()
    {
        using var ui = new UiThread();
        var imported = Block.Para("Unattributed text from saved MIME");
        imported.Runs[0] = imported.Runs[0] with { IsComposerContext = true };
        var seed = ComposerHarness.Seed(draftId: "saved-draft", saved: true) with { Body = new Doc([imported]) };
        using var draft = OpenRich(new ComposerHarness(), seed);
        using var access = new PluginComposerAccess(new TestComposer(ui, draft), ui, new Sessions());
        await using var binding = Binding();
        Assert.True(access.Expose(binding, draft.Id, draft.AccountKey));
        var handle = Assert.IsAssignableFrom<IPluginComposerHandle>(access.Resolve(binding, draft.Id));
        var initial = await handle.GetSnapshotAsync(Ct);
        Assert.Empty(initial.Text);
        await ui.InvokeAsync(() => { draft.Editor!.Session.InsertText("New text"); return Task.CompletedTask; });
        var updated = await handle.GetSnapshotAsync(Ct);
        Assert.Equal("New text", updated.Text);
        Assert.NotEqual(initial.Revision, updated.Revision, StringComparer.Ordinal);
    }

    private static ComposerViewModel OpenRich(ComposerHarness harness, ComposeSeed seed) =>
        new(harness.Context with { Editors = new EditorViewModelFactory(new EditorSessionFactory(), new NoSpellEngine(), new NoRewriteProvider()) },
            seed, ComposerHarness.Identities());

    private sealed class TestComposer(UiThread ui, ComposerViewModel current) : IComposerService
    {
        private ComposerViewModel? _current = current;
        public ComposerViewModel? Current => Volatile.Read(ref _current);
        public bool IsOpen { get { Assert.True(ui.HasThreadAccess); return _current is not null; } }
        public event EventHandler? OpenChanged;
        public void Replace(ComposerViewModel? next) { Assert.True(ui.HasThreadAccess); Volatile.Write(ref _current, next); OpenChanged?.Invoke(this, EventArgs.Empty); }
        public Task OpenAsync(ComposeRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task OpenSeedAsync(ComposeSeed seed, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryCloseAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Sessions : IPluginSessionSource
    {
        private readonly IAccountSession _session = new Session();
        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Ok(new(_session, 1));
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => Result<PluginSessionCapture>.Ok(new(_session, null));
        public bool IsCurrent(PluginSessionCapture capture) => ReferenceEquals(capture.Session, _session);
        public bool TryRunWhileCurrent(IReadOnlyList<PluginSessionCapture> captures, Func<bool> write) =>
            captures.All(IsCurrent) && write();
    }

    private sealed class Session : IAccountSession
    {
        public AccountInfo Account { get; } = new("acc", "sample@example.org", "https://jmap.example.org");
        public IJmapClient Client => throw new NotSupportedException();
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
    }
}

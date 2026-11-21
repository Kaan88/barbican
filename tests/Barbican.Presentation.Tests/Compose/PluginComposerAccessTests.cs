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
using Barbican.Core.Plugins.Protocol;
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

    private static Block Context(string text)
    {
        var block = Block.Para(text);
        block.Runs[0] = block.Runs[0] with { IsComposerContext = true };
        return block;
    }

    private static ComposeSeed ReplySeed() => ComposerHarness.Seed(kind: ComposeKind.Reply) with
    {
        Body = new Doc([Block.Para("Thanks!"), Block.Para(""), Context("On Monday, Bob wrote:"),
            new Block(new BlockKind.Quoted("<p>original</p>", "original", false, true)), Block.Para(""), Context("-- Alice")]),
    };

    private static async Task<(IPluginComposerHandle Handle, PluginComposerAccess Access)> ExposeAsync(UiThread ui, ComposerViewModel draft, PluginAnnotationStore? store = null)
    {
        var access = new PluginComposerAccess(new TestComposer(ui, draft), ui, new Sessions(), store);
        var binding = Binding();
        Assert.True(access.Expose(binding, draft.Id, draft.AccountKey));
        await Task.CompletedTask;
        return (Assert.IsAssignableFrom<IPluginComposerHandle>(access.Resolve(binding, draft.Id)), access);
    }

    [Fact]
    public async Task Body_reads_only_the_own_text_and_replacement_keeps_signature_and_quote_as_one_undo_step()
    {
        using var ui = new UiThread();
        using var draft = OpenRich(new ComposerHarness(), ReplySeed());
        var (handle, access) = await ExposeAsync(ui, draft);
        using var _ = access;
        var body = await handle.ReadBodyAsync(Ct);
        Assert.Equal("Thanks!", body.Text, StringComparer.Ordinal);
        Assert.DoesNotContain("Alice", body.Html, StringComparison.Ordinal);
        var revision = (await handle.GetSnapshotAsync(Ct)).Revision;
        Assert.True(await handle.ReplaceBodyAsync(new("<p>Hi <b>Bob</b></p><p>second</p>", "Hi Bob\nsecond"), revision, Ct));
        var doc = draft.Editor!.Session.Doc;
        Assert.Equal("Hi Bob", doc.Blocks[0].Text, StringComparer.Ordinal);
        Assert.Equal("second", doc.Blocks[1].Text, StringComparer.Ordinal);
        Assert.Contains(doc.Blocks, block => block.Kind is BlockKind.Quoted);
        Assert.Contains(doc.Blocks, block => block.Text.Contains("Alice", StringComparison.Ordinal));
        Assert.Contains(doc.Blocks, block => block.Text.Contains("Bob wrote", StringComparison.Ordinal));
        Assert.True(draft.Editor.Session.CanUndo);
        var undone = false;
        await ui.InvokeAsync(() => { undone = draft.Editor.Session.Undo(); return Task.CompletedTask; });
        Assert.True(undone);
        Assert.Equal("Thanks!", draft.Editor.Session.Doc.Blocks[0].Text, StringComparer.Ordinal);
        Assert.Equal(6, draft.Editor.Session.Doc.Blocks.Count);
    }

    [Fact]
    public async Task Edits_with_a_stale_revision_or_over_protected_text_change_nothing()
    {
        using var ui = new UiThread();
        using var draft = OpenRich(new ComposerHarness(), ReplySeed());
        var (handle, access) = await ExposeAsync(ui, draft);
        using var _ = access;
        var revision = (await handle.GetSnapshotAsync(Ct)).Revision;
        var before = draft.Editor!.Session.Version;
        Assert.False(await handle.ReplaceBodyAsync(new("<p>x</p>", "x"), "stale", Ct));
        Assert.Equal(before, draft.Editor.Session.Version);
        // Select the signature and the quoted original: neither may be replaced or written into.
        await ui.InvokeAsync(() => { draft.Editor.Session.SelectRange(new Pos(2, 0), new Pos(3, 0)); return Task.CompletedTask; });
        revision = (await handle.GetSnapshotAsync(Ct)).Revision;
        Assert.False(await handle.ReplaceSelectionAsync(new("<p>x</p>", "x"), false, revision, Ct));
        Assert.False(await handle.ReplaceSelectionAsync(new("<p>x</p>", "x"), true, revision, Ct));
        await ui.InvokeAsync(() => { draft.Editor.Session.SetCaret(new Pos(5, 0)); return Task.CompletedTask; });
        revision = (await handle.GetSnapshotAsync(Ct)).Revision;
        Assert.False(await handle.ReplaceSelectionAsync(new("<p>x</p>", "x"), false, revision, Ct));
        Assert.Equal(6, draft.Editor.Session.Doc.Blocks.Count);
    }

    [Fact]
    public async Task Selection_is_read_and_replaced_by_one_editor_step_and_below_inserts_after_its_block()
    {
        using var ui = new UiThread();
        using var draft = OpenRich(new ComposerHarness(), ReplySeed());
        var (handle, access) = await ExposeAsync(ui, draft);
        using var _ = access;
        Assert.True((await handle.ReadSelectionAsync(Ct)).Empty);
        await ui.InvokeAsync(() => { draft.Editor!.Session.SelectRange(new Pos(0, 0), new Pos(0, 6)); return Task.CompletedTask; });
        var read = await handle.ReadSelectionAsync(Ct);
        Assert.False(read.Empty);
        Assert.Equal("Thanks", read.Text, StringComparer.Ordinal);
        var revision = (await handle.GetSnapshotAsync(Ct)).Revision;
        Assert.True(await handle.ReplaceSelectionAsync(new("<p>Dear Bob</p>", "Dear Bob"), false, revision, Ct));
        Assert.Equal("Dear Bob!", draft.Editor!.Session.Doc.Blocks[0].Text, StringComparer.Ordinal);
        revision = (await handle.GetSnapshotAsync(Ct)).Revision;
        Assert.True(await handle.ReplaceSelectionAsync(new("<p>below</p>", "below"), true, revision, Ct));
        Assert.Equal("below", draft.Editor.Session.Doc.Blocks[1].Text, StringComparer.Ordinal);
        Assert.Contains(draft.Editor.Session.Doc.Blocks, block => block.Kind is BlockKind.Quoted);
    }

    [Fact]
    public async Task Edits_are_refused_while_the_html_source_view_is_open_or_after_the_composer_is_replaced()
    {
        using var ui = new UiThread();
        using var draft = OpenRich(new ComposerHarness(), ReplySeed());
        using var replacement = OpenRich(new ComposerHarness(), ReplySeed());
        var composer = new TestComposer(ui, draft);
        using var access = new PluginComposerAccess(composer, ui, new Sessions());
        await using var binding = Binding();
        Assert.True(access.Expose(binding, draft.Id, draft.AccountKey));
        var handle = Assert.IsAssignableFrom<IPluginComposerHandle>(access.Resolve(binding, draft.Id));
        await ui.InvokeAsync(() => { draft.Editor!.ToggleSource(); return Task.CompletedTask; });
        var revision = (await handle.GetSnapshotAsync(Ct)).Revision;
        Assert.False(await handle.ReplaceBodyAsync(new("<p>x</p>", "x"), revision, Ct));
        await ui.InvokeAsync(() => { composer.Replace(replacement); return Task.CompletedTask; });
        Assert.False(await handle.ReplaceBodyAsync(new("<p>x</p>", "x"), revision, Ct));
        Assert.Equal("Thanks!", replacement.Editor!.Session.Doc.Blocks[0].Text, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Annotations_apply_only_to_the_text_that_was_checked_and_go_with_the_composer()
    {
        using var ui = new UiThread();
        var seed = ComposerHarness.Seed() with { Body = new Doc([Block.Para("teh quick fox")]) };
        using var draft = OpenRich(new ComposerHarness(), seed);
        var store = new PluginAnnotationStore();
        var (handle, access) = await ExposeAsync(ui, draft, store);
        using var _ = access;
        await using var owner = Binding();
        var registry = new PluginCheckerRegistry(TimeProvider.System);
        var spec = new PluginCheckerRegistration(owner, "lt", "LT", ["spelling"], TimeSpan.FromMilliseconds(800), [], true, false);
        var request = await handle.BeginCheckAsync(spec, Ct);
        Assert.NotNull(request);
        var segment = request.Request.Property("segments").Item(0).Property("id").StringValue;
        using var json = System.Text.Json.JsonDocument.Parse($$"""[{"segmentId":"{{segment}}","offset":0,"length":3,"kind":"spelling","message":"typo","replacements":["the"]}]""");
        var values = PluginValue.FromJson(json.RootElement);
        Assert.Equal(0, await handle.SetAnnotationsAsync(spec, "wrong", values, Ct));
        Assert.Equal(1, await handle.SetAnnotationsAsync(spec, request.Revision, values, Ct));
        // The text moves on: the old result no longer applies.
        await ui.InvokeAsync(() => { draft.Editor!.Session.InsertText("x"); return Task.CompletedTask; });
        Assert.Equal(0, await handle.SetAnnotationsAsync(spec, request.Revision, values, Ct));
        Assert.Empty(store.Current(draft.Id, draft.Editor!.Session.Version, registry));
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
        public Task<ComposerViewModel?> OpenAsync(ComposeRequest request, CancellationToken ct) => throw new NotSupportedException();
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

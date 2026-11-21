using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Editor;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Presentation.Editor;

namespace Barbican.Presentation.Compose;

public sealed partial class ComposerViewModel
{
    private readonly ConditionalWeakTable<AttachmentItem, string> _pluginAttachmentIds = new();
    private readonly HashSet<AttachmentItem> _pluginObservedAttachments = [];
    private long _pluginRevision;
    private bool _pluginSeedQuoted;
    private bool _pluginSeedGeneratedText;

    internal string PluginRevision => string.Create(CultureInfo.InvariantCulture, $"{_pluginRevision}:{Editor?.Session.Version ?? 0}");

    private void TrackPluginRevision()
    {
        _pluginSeedQuoted = _seed.Body.Blocks.Any(block => block.Kind is BlockKind.Quoted);
        _pluginSeedGeneratedText = HasGeneratedText(_seed.Body);
        PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(Subject) or nameof(FallbackBody) or nameof(Editor) or nameof(IsSending) or
                nameof(SelectedIdentityId) or nameof(FromOverrideEnabled) or nameof(FromOverrideEmail) or nameof(FromOverrideName) or
                nameof(SubAddressTag) or nameof(ClassificationChoice) or nameof(PlainText) or nameof(Priority) or nameof(ReadReceipt))
                _pluginRevision++;
        };
        foreach (var field in new[] { To, Cc, Bcc }) field.TextChanged += (_, _) => _pluginRevision++;
        Attachments.CollectionChanged += (_, _) => ObservePluginAttachments();
        ObservePluginAttachments();
    }

    private void ObservePluginAttachments()
    {
        _pluginRevision++;
        foreach (var item in _pluginObservedAttachments.Except(Attachments).ToArray())
        {
            item.PropertyChanged -= OnPluginAttachmentChanged;
            _pluginObservedAttachments.Remove(item);
        }
        foreach (var item in Attachments)
        {
            if (_pluginObservedAttachments.Add(item)) item.PropertyChanged += OnPluginAttachmentChanged;
        }
    }

    private void OnPluginAttachmentChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(AttachmentItem.Attachment) or nameof(AttachmentItem.State)) _pluginRevision++;
    }

    internal PluginComposerSnapshot PluginSnapshot() =>
        new(PluginRevision, Subject, PluginBodyText(), Addresses(To), Addresses(Cc), Addresses(Bcc),
            Attachments.Where(PluginAttachmentReady).Select(item =>
                new PluginComposerAttachment(PluginAttachmentId(item), item.Name, item.Attachment.Type, item.Attachment.Size)),
            Attachments.Any(item => item.Attachment.Cid is null && !PluginAttachmentReady(item)) || HasPendingInline());

    internal DraftAttachment? PluginAttachment(string id) => Attachments
        .FirstOrDefault(item => PluginAttachmentReady(item) && _pluginAttachmentIds.TryGetValue(item, out var known) &&
            string.Equals(id, known, StringComparison.Ordinal))?.Attachment;

    private string PluginAttachmentId(AttachmentItem item) => _pluginAttachmentIds.GetValue(item, _ => Guid.NewGuid().ToString("N"));

    private static bool PluginAttachmentReady(AttachmentItem item) =>
        item is { State: AttachmentState.Done, Attachment: { Cid: null, BlobId.Length: > 0 } };

    private static IEnumerable<string> Addresses(RecipientFieldViewModel field) =>
        RecipientText.Expand(field.Recipients).Select(recipient => recipient.Email);

    private string PluginBodyText()
    {
        var doc = CurrentDoc();
        // Plain fallback and HTML source can flatten seeded metadata. Suppress text if a
        // seeded context no longer has its provenance, since it could contain quoted content.
        if ((_pluginSeedQuoted || _pluginSeedGeneratedText) && !doc.HasComposerProvenance) return string.Empty;
        return string.Join('\n', doc.Blocks.Where(block => block.Kind is not BlockKind.Quoted)
            .Where(block => block.Runs.Count == 0 || block.Runs.Any(run => !run.IsComposerContext))
            .Select(block => string.Concat(block.Runs.Where(run => !run.IsComposerContext).Select(run => run.Text)))).TrimEnd();
    }

    private static bool HasGeneratedText(Doc doc) => doc.Blocks.Any(block => block.Runs.Any(run => run.IsComposerContext));

    // ---- add-on edits (composer.getBody / getSelection / replaceBody / replaceSelection / attach): the editor's own operations, one undo step each ----

    private bool PluginOwnTextLost => (_pluginSeedQuoted || _pluginSeedGeneratedText) && !CurrentDoc().HasComposerProvenance;

    private bool PluginEditable(string revision, out IEditorViewModel editor)
    {
        editor = Editor!;
        return Editor is { SourceOpen: false } && !_closed && !IsSending && !PluginOwnTextLost &&
            string.Equals(revision, PluginRevision, StringComparison.Ordinal);
    }

    private static IReadOnlyList<Block> PluginBlocks(IEditorSession session, PluginComposerContent content) =>
        session.Plain ? DocHtml.ImportText(content.Text) : DocHtml.Import(content.Html);

    internal PluginComposerBody PluginBody() =>
        PluginOwnTextLost ? new(string.Empty, string.Empty) : PluginComposerOwnText.Read(CurrentDoc());

    internal PluginComposerSelection PluginSelection()
    {
        if (Editor is not { SourceOpen: false } editor || PluginOwnTextLost || editor.Session.Sel.IsCollapsed) return new(string.Empty, string.Empty, true);
        var blocks = editor.Session.SelectedBlocks();
        return new(PluginComposerOwnText.Html(blocks), editor.Session.SelectedText(), false);
    }

    internal bool PluginReplaceBody(PluginComposerContent content, string revision)
    {
        if (!PluginEditable(revision, out var editor)) return false;
        var blocks = PluginBlocks(editor.Session, content);
        var from = PluginComposerOwnText.Range(editor.Session.Doc).From;
        editor.Session.ReplaceDoc(PluginComposerOwnText.Replace(editor.Session.Doc, blocks), undo: true);
        if (blocks.Count > 0)
        {
            var last = Math.Min(from + blocks.Count - 1, editor.Session.Doc.Blocks.Count - 1);
            editor.Session.SetCaret(new Pos(last, editor.Session.Doc.Blocks[last].Length));
        }
        return true;
    }

    internal bool PluginReplaceSelection(PluginComposerContent content, bool below, string revision)
    {
        if (!PluginEditable(revision, out var editor)) return false;
        var session = editor.Session;
        var doc = session.Doc;
        var selection = session.Sel;
        // The signature, the reply header and the quote are never replaced or written into.
        for (var index = selection.Start.B; index <= selection.End.B; index++)
            if (PluginComposerOwnText.IsProtected(doc.Blocks[index])) return false;
        var blocks = PluginBlocks(session, content);
        if (!below)
        {
            IEnumerable<Block> images = selection.IsCollapsed ? Enumerable.Empty<Block>() : session.SelectedBlocks().Where(block => block.Kind is BlockKind.Image).Select(block => block.Clone());
            session.InsertBlocks([.. blocks, .. images]);
            return true;
        }
        var at = (doc.TableRange(selection.End.B) is { } table ? table.End : selection.End.B + 1);
        var result = doc.Clone();
        for (var index = 0; index < blocks.Count; index++) result.Blocks.Insert(at + index, blocks[index]);
        session.ReplaceDoc(result, undo: true);
        return true;
    }

    internal long PluginAttachmentTotal() =>
        Attachments.Where(item => item.State == AttachmentState.Done).Sum(item => item.Attachment.Size) + _inline.Sum(item => item.Size);

    internal int PluginAttachmentCount => Attachments.Count;

    internal bool PluginAttach(DraftAttachment attachment, string revision)
    {
        if (_closed || IsSending || !string.Equals(revision, PluginRevision, StringComparison.Ordinal)) return false;
        Attachments.Add(new AttachmentItem(attachment, AttachmentState.Done, null));
        OnPropertyChanged(nameof(HasPendingUploads));
        return true;
    }

    internal long PluginEditorVersion => Editor?.Session.Version ?? 0;

    internal bool PluginCheckable => Editor is { SourceOpen: false };

    internal Doc PluginCheckDoc => CurrentDoc();
}

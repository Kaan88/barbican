// Mirrors: Rust src/rte/mod.rs refresh :244 (state after every change), ctx_info :332, ctx_pick :365 (spelling picks), source_cmd :454 (HTML source view); Bulwark components/email/rich-text-editor.tsx.

using Barbican.Core.Editor;
using Barbican.Core.Editor.Spell;

namespace Barbican.Presentation.Editor;

/// <summary>The view model of one editor: the session, the toolbar state, the source view and the commands.</summary>
public sealed class EditorViewModel : IEditorSurface, IDisposable
{
    private Doc? _sourceDoc;
    private bool _sourceEdited;
    private bool _applyingSource;

    public EditorViewModel(IEditorSession session, ISpellEngine? spell = null, IRewriteProvider? rewrite = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        Spell = spell ?? new NoSpellEngine();
        Rewrite = rewrite ?? new NoRewriteProvider();
        Toolbar = ToolbarStateBuilder.Build(session);
        session.Changed += OnSessionChanged;
    }

    public event EventHandler? FocusRequested;

    public event EventHandler? ToolbarChanged;

    public event EventHandler<EditorViewCommandEventArgs>? ViewCommandRequested;

    public IEditorSession Session { get; }

    public ToolbarState Toolbar { get; private set; }

    public bool SourceOpen { get; private set; }

    public string SourceText { get; private set; } = string.Empty;

    public IMentionSource? Mentions { get; set; }

    public bool Plain => Session.Plain;

    public bool RtlSupport { get; set; }

    public ISpellEngine Spell { get; }

    public IRewriteProvider Rewrite { get; }

    public MisspelledWord? ContextWord { get; set; }

    public void RequestFocus() => FocusRequested?.Invoke(this, EventArgs.Empty);

    public void ToggleSource()
    {
        if (SourceOpen)
        {
            SourceOpen = false;
            SourceText = string.Empty;
            _sourceDoc = null;
            _sourceEdited = false;
            Notify();
            RequestFocus();
            return;
        }

        _sourceDoc = Session.Doc.Clone();
        SourceText = DocHtml.Source(Session.Doc);
        _sourceEdited = false;
        SourceOpen = true;
        Notify();
    }

    public void EditSource(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var doc = DocHtml.ApplySource(Session.Doc, text);
        var first = !_sourceEdited;
        _sourceEdited = true;
        _applyingSource = true;
        try
        {
            Session.ReplaceDoc(doc, first);
        }
        finally
        {
            _applyingSource = false;
        }

        SourceText = text;
        _sourceDoc = Session.Doc.Clone();
    }

    public bool Execute(EditorCommand command, string? argument = null)
    {
        if (command is EditorCommand.ToggleSource)
        {
            ToggleSource();
            return true;
        }

        if (command is EditorCommand.SpellReplace or EditorCommand.SpellAdd or EditorCommand.SpellIgnore)
        {
            return RunSpelling(command, argument);
        }

        if (EditorCommandRunner.NeedsView(command))
        {
            if (Session.Plain && !EditorCommandRunner.AllowedInPlain(command))
            {
                return false;
            }

            ViewCommandRequested?.Invoke(this, new EditorViewCommandEventArgs(command, argument));
            return true;
        }

        return EditorCommandRunner.Run(Session, command, argument);
    }

    public IReadOnlyList<SpellRange> Misspelled(int block, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Spell.IsAvailable || text.Length == 0 || block < 0 || block >= Session.Doc.Blocks.Count)
        {
            return [];
        }

        var b = Session.Doc.Blocks[block];
        if (b.Kind is not (BlockKind.Para or BlockKind.Heading or BlockKind.Quote or BlockKind.Bullet or BlockKind.Ordered or BlockKind.Cell))
        {
            return [];
        }

        var caret = Session.Sel.IsCollapsed ? (Pos?)Session.Sel.Focus : null;
        var result = new List<SpellRange>();
        foreach (var r in Spell.Misspelled(text))
        {
            // the word being typed is not flagged until the caret leaves it
            if (caret is { } c && c.B == block && c.O >= r.Start && c.O <= r.End)
            {
                continue;
            }

            if (MarksAt(b, r.Start) is { } m && (m.Code || m.Link is not null))
            {
                continue;
            }

            result.Add(r);
        }

        return result;
    }

    public MisspelledWord? MisspelledAt(int block, int offset)
    {
        if (!Spell.IsAvailable || block < 0 || block >= Session.Doc.Blocks.Count)
        {
            return null;
        }

        var text = Session.Doc.Blocks[block].Text;
        foreach (var r in Spell.Misspelled(text))
        {
            if (offset >= r.Start && offset <= r.End)
            {
                return new MisspelledWord(block, r.Start, r.End, text.Substring(r.Start, r.Length));
            }
        }

        return null;
    }

    public void RefreshToolbar()
    {
        var onWord = Session.Sel.IsCollapsed && MisspelledAt(Session.Sel.Focus.B, Session.Sel.Focus.O) is not null;
        var next = ToolbarStateBuilder.Build(Session, onWord);
        if (!next.Equals(Toolbar))
        {
            Toolbar = next;
            ToolbarChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => Session.Changed -= OnSessionChanged;

    private static Marks? MarksAt(Block b, int offset)
    {
        var acc = 0;
        foreach (var run in b.Runs)
        {
            if (offset >= acc && offset < acc + run.Text.Length)
            {
                return run.Marks;
            }

            acc += run.Text.Length;
        }

        return null;
    }

    private bool RunSpelling(EditorCommand command, string? argument)
    {
        var word = ContextWord;
        ContextWord = null;
        if (word is null)
        {
            return false;
        }

        switch (command)
        {
            case EditorCommand.SpellReplace when !string.IsNullOrEmpty(argument):
                Session.ReplaceRange(new Pos(word.Block, word.Start), new Pos(word.Block, word.End), argument);
                break;
            case EditorCommand.SpellAdd:
                Spell.Add(word.Text);
                break;
            case EditorCommand.SpellIgnore:
                Spell.Ignore(word.Text);
                break;
            default:
                return false;
        }

        OnSessionChanged(this, EventArgs.Empty);
        return true;
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (SourceOpen && !_applyingSource && _sourceDoc is { } shown && !DocsEqual(shown, Session.Doc))
        {
            // changes made outside the source view (toolbar, undo, a signature) show there, so the next keystroke does not revert them
            SourceText = DocHtml.Source(Session.Doc);
            _sourceDoc = Session.Doc.Clone();
            _sourceEdited = false;
        }

        RefreshToolbar();
    }

    private void Notify() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    private static bool DocsEqual(Doc a, Doc b) =>
        a.Blocks.Count == b.Blocks.Count && a.Blocks.Zip(b.Blocks).All(p => p.First.Kind.Equals(p.Second.Kind) && p.First.Align == p.Second.Align && p.First.Indent == p.Second.Indent && p.First.Runs.SequenceEqual(p.Second.Runs));
}

// Mirrors: Bulwark components/email/search-snippet-text.tsx (the matched terms of a SearchSnippet/get excerpt drawn marked, as text, never as HTML), lib/search-snippet.ts;
// Rust search_ui.rs:145 (rows without a mark keep their plain subject and preview). I-232, L-050. Owner search.
// A row's subject and preview TextBlocks carry three attached properties (the row id, which part, and the plain text). The text is drawn from here: plain when the open search
// has no mark for the row, runs with the marks otherwise. The list is virtualised, so only rows on screen ask the snippet source (it batches one SearchSnippet/get per few
// milliseconds) and a TextBlock redraws only when an arrival names its row. Marked runs are semi-bold in the amber of the flagged star, so they read in every look.

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Search;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;

namespace Barbican.App.Views.List;

/// <summary>Attached properties that draw search marks in a row's subject and preview.</summary>
public static class Snippet
{
    /// <summary>The row's message id.</summary>
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached("Id", typeof(string), typeof(Snippet), new PropertyMetadata(string.Empty, OnChanged));

    /// <summary>"subject" or "preview".</summary>
    public static readonly DependencyProperty PartProperty = DependencyProperty.RegisterAttached("Part", typeof(string), typeof(Snippet), new PropertyMetadata(string.Empty, OnChanged));

    /// <summary>The row's plain text, drawn when there is no mark.</summary>
    public static readonly DependencyProperty PlainProperty = DependencyProperty.RegisterAttached("Plain", typeof(string), typeof(Snippet), new PropertyMetadata(string.Empty, OnChanged));

    private static readonly DependencyProperty WatchingProperty = DependencyProperty.RegisterAttached("Watching", typeof(bool), typeof(Snippet), new PropertyMetadata(false));

    private static readonly HashSet<TextBlock> Live = [];
    private static bool _subscribed;

    public static string GetId(DependencyObject element) => (string)element.GetValue(IdProperty);

    public static void SetId(DependencyObject element, string value) => element.SetValue(IdProperty, value);

    public static string GetPart(DependencyObject element) => (string)element.GetValue(PartProperty);

    public static void SetPart(DependencyObject element, string value) => element.SetValue(PartProperty, value);

    public static string GetPlain(DependencyObject element) => (string)element.GetValue(PlainProperty);

    public static void SetPlain(DependencyObject element, string value) => element.SetValue(PlainProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        if (!(bool)block.GetValue(WatchingProperty))
        {
            block.SetValue(WatchingProperty, true);
            block.Loaded += (_, _) =>
            {
                // an arrival can come before the block is in the tree: draw what is known now
                Live.Add(block);
                Draw(block);
            };
            block.Unloaded += (_, _) => Live.Remove(block);
            Subscribe(block);
        }

        Draw(block);
    }

    // one subscription for all rows; the source raises from a pool thread, so the redraw goes through the UI queue
    private static void Subscribe(TextBlock first)
    {
        if (_subscribed || ViewHost.Find<ISnippetSource>() is not { } source)
        {
            return;
        }

        _subscribed = true;
        var queue = first.DispatcherQueue;
        source.Arrived += (_, arrived) => queue.TryEnqueue(() =>
        {
            foreach (var block in Live.Where(b => arrived.EmailIds.Count == 0 || arrived.EmailIds.Contains(GetId(b), StringComparer.Ordinal)).ToList())
            {
                Draw(block);
            }
        });
    }

    private static void Draw(TextBlock block)
    {
        var plain = GetPlain(block);
        var id = GetId(block);
        IReadOnlyList<TextSegment>? segments = null;
        if (id.Length > 0 && ViewHost.Find<ISnippetSource>() is { } source)
        {
            segments = string.Equals(GetPart(block), "subject", StringComparison.Ordinal) ? source.SubjectFor(id) : source.PreviewFor(id);
        }

        if (segments is null)
        {
            if (block.Inlines.Count > 0)
            {
                block.Inlines.Clear();
            }

            block.Text = plain;
            return;
        }

        block.Inlines.Clear();
        foreach (var s in segments)
        {
            var run = new Run { Text = s.Text };
            if (s.Marked)
            {
                run.FontWeight = FontWeights.SemiBold;
                run.Foreground = AppResources.Brush("AmberFg");
            }

            block.Inlines.Add(run);
        }
    }
}

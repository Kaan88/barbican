// Mirrors: Rust src/rte/mod.rs copy :418, clipboard_fragment :430, paste :440, insert_image_bytes :188 (large images show at most 600 px wide, the sent file keeps full resolution);
// Bulwark components/email/rich-text-editor.tsx (paste and drop of images), plain-text-paste.ts (I-268).

using System.Security.Cryptography;
using Barbican.Core.Editor;

namespace Barbican.Presentation.Editor;

/// <summary>Copy, cut and paste against an <see cref="IEditorClipboard"/> (the App wraps the OS clipboard; tests use a fake and never touch the desktop's).</summary>
public static class EditorClipboardActions
{
    /// <summary>Widest an inserted image is shown (Rust insert_image_bytes).</summary>
    public const int MaxShownWidth = 600;

    /// <summary>Copies the selection as HTML and text; with <paramref name="cut"/> also deletes it. False when nothing is selected.</summary>
    public static bool Copy(IEditorSession session, IEditorClipboard clipboard, bool cut)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        var frag = session.SelectedBlocks();
        if (frag.Count == 0)
        {
            return false;
        }

        var doc = new Doc(frag.Select(b => b.Clone()));
        var html = DocHtml.Export(doc);
        var text = session.SelectedText();
        clipboard.SetContent(html, text.Length == 0 ? DocHtml.ToText(doc) : text);
        if (cut)
        {
            session.Backspace(word: false);
        }

        return true;
    }

    /// <summary>The copied fragment of a CF_HTML clipboard payload (Rust clipboard_fragment).</summary>
    public static string Fragment(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        const string start = "<!--StartFragment-->";
        const string end = "<!--EndFragment-->";
        var a = html.IndexOf(start, StringComparison.Ordinal);
        var b = html.IndexOf(end, StringComparison.Ordinal);
        if (a >= 0 && b > a)
        {
            return html[(a + start.Length)..b];
        }

        if (html.StartsWith("Version:", StringComparison.Ordinal))
        {
            var p = html.IndexOf('<', StringComparison.Ordinal);
            if (p >= 0)
            {
                return html[p..];
            }
        }

        return html;
    }

    /// <summary>
    /// Pastes: image files, then HTML, then an image, then text (lists recognised, I-268). <paramref name="plainOnly"/>, plain-text mode and a code block take the text only.
    /// <paramref name="imageSize"/> gives the natural size of encoded bytes (null: unknown).
    /// </summary>
    public static void Paste(IEditorSession session, IEditorClipboard clipboard, bool plainOnly, Func<byte[], (int Width, int Height)?>? imageSize = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        var focus = session.Doc.Blocks[Math.Clamp(session.Sel.Focus.B, 0, session.Doc.Blocks.Count - 1)];
        var textOnly = plainOnly || session.Plain || focus.Kind is BlockKind.CodeBlock;
        if (!textOnly)
        {
            var images = clipboard.Files.Where(f => IsPictureMime(f.Mime)).ToList();
            if (images.Count > 0)
            {
                foreach (var file in images)
                {
                    InsertImage(session, file.Bytes, file.Mime, file.Name, imageSize);
                }

                return;
            }

            if (clipboard.Html is { Length: > 0 } html)
            {
                var blocks = DocHtml.Import(Fragment(html));
                if (blocks.Count > 0)
                {
                    session.InsertBlocks(blocks);
                    return;
                }
            }

            if (clipboard.Image is { } pasted)
            {
                InsertImage(session, pasted.Bytes, pasted.Mime, pasted.Name, imageSize);
                return;
            }
        }

        if (clipboard.Text is { } text)
        {
            PasteText(session, text.Replace("\r\n", "\n", StringComparison.Ordinal), textOnly);
        }
    }

    /// <summary>Inserts a picture (pasted, dropped or picked) as an inline image with a new cid; composer-ui uploads those that have no blob id.</summary>
    public static void InsertImage(IEditorSession session, byte[] bytes, string mime, string name, Func<byte[], (int Width, int Height)?>? imageSize = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bytes);
        var size = imageSize?.Invoke(bytes);
        var (w, h) = size is { Width: > MaxShownWidth } s ? (MaxShownWidth, (int)(s.Height * (float)MaxShownWidth / s.Width)) : (0, 0);
        session.InsertImage(new ImageData
        {
            Cid = NewCid(),
            Mime = mime,
            Name = name,
            Alt = name,
            Bytes = bytes,
            Width = w,
            Height = h,
        });
    }

    /// <summary>A picture that is not SVG (it can carry script).</summary>
    public static bool IsPictureMime(string mime) => mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !mime.Contains("svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>A new content id for an inline image.</summary>
    public static string NewCid() => "img" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)) + "@barbican";

    private static void PasteText(IEditorSession session, string text, bool textOnly)
    {
        if (!textOnly)
        {
            var parsed = PlainTextListPaste.Parse(text);
            if (parsed.Any(b => b.Kind.IsList))
            {
                session.InsertBlocks(parsed);
                return;
            }
        }

        session.InsertText(text);
    }
}

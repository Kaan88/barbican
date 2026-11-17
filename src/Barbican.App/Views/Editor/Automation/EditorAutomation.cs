#if AUTOMATION
// Mirrors: M3 plan 6.3 step 4 (editor-open, editor-type, editor-key, editor-select, editor-dump, editor-menu, editor-latency); spec/automation-protocol.md. No command produces OS input: typing goes through the
// same entry as the hidden TextBox, keys through the same HandleKey as a real KeyDown, the clipboard is in-process (ADR 0007).

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.App.Views.Menus.Automation;
using Barbican.Automation;
using Barbican.Core.Editor;
using Barbican.Core.Editor.Spell;
using Barbican.Presentation.Editor;
using Barbican.Presentation.Menus;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Views.Editor.Automation;

/// <summary>Registers the editor probes.</summary>
public static class EditorAutomation
{
    private static readonly MemoryClipboardBackend Clipboard = new();

    // MA0051: one case per protocol command, top to bottom (as .editorconfig says for the automation commands).
#pragma warning disable MA0051
    public static IServiceCollection AddBarbicanEditorAutomation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EditorClipboards.Backend = Clipboard;
        services.AddSingleton(_ => EditorTestHost.Overlay);
        Add(services, "editor-open", (sp, a) => Task.FromResult(Open(sp, a)));
        Add(services, "editor-close", (__, _) =>
        {
            EditorTestHost.Instance.Close();
            return Task.FromResult("ok");
        });
        Add(services, "editor-type", (__, a) => WithAsync(v => v.TypeText(a), "ok"));
        Add(services, "editor-key", (__, a) =>
        {
            foreach (var chord in a.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (EditorKeyInput.Parse(chord) is not { } input)
                {
                    return Task.FromResult("error bad chord " + chord);
                }

                if (View() is { } view)
                {
                    _ = view.HandleKey(input);
                }
            }

            return Task.FromResult("ok");
        });
        Add(services, "editor-cmd", (__, a) =>
        {
            var parts = a.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !Enum.TryParse<EditorCommand>(parts[0], ignoreCase: true, out var command))
            {
                return Task.FromResult("error usage: editor-cmd COMMAND [ARG] (" + string.Join('|', Enum.GetNames<EditorCommand>()) + ")");
            }

            return Task.FromResult(Surface()?.Execute(command, parts.Length > 1 ? parts[1] : null) == true ? "ok" : "refused");
        });
        Add(services, "editor-select", (__, a) => Task.FromResult(Select(a)));
        Add(services, "editor-click", async (_, a) =>
        {
            var n = Numbers(a);
            if (n.Length < 2 || View() is not { } view)
            {
                return "error usage: editor-click X Y [COUNT|shift]";
            }

            var count = a.Contains("shift", StringComparison.Ordinal) ? 1 : n.Length > 2 ? (int)n[2] : 1;
            for (var i = 0; i < count; i++)
            {
                view.PointerDown((float)n[0], (float)n[1], a.Contains("shift", StringComparison.Ordinal));
                view.PointerUp((float)n[0], (float)n[1]);
                await Task.Delay(30);
            }

            return "ok";
        });
        Add(services, "editor-drag", (__, a) =>
        {
            var n = Numbers(a);
            if (n.Length < 4 || View() is not { } view)
            {
                return Task.FromResult("error usage: editor-drag X1 Y1 X2 Y2");
            }

            view.PointerDown((float)n[0], (float)n[1], shift: false);
            view.Controller?.PointerDrag((float)n[2], (float)n[3]);
            view.PointerUp((float)n[2], (float)n[3]);
            return Task.FromResult("ok");
        });
        Add(services, "editor-menu", (sp, a) =>
        {
            if (Surface() is not { } surface || View() is not { } view)
            {
                return Task.FromResult("error no editor open");
            }

            sp.GetRequiredService<ActiveEditor>().Activate(surface);
            var n = Numbers(a);
            if (n.Length >= 2 && view.Controller is { } ctl)
            {
                surface.ContextWord = surface.MisspelledAt(ctl.Layout.HitTest((float)n[0], (float)n[1]).Pos.B, ctl.Layout.HitTest((float)n[0], (float)n[1]).Pos.O);
                _ = ctl.RightDown((float)n[0], (float)n[1]);
            }

            surface.RefreshToolbar();
            var rows = EditorMenus.Main(surface);
            return Task.FromResult(string.Join('|', rows.Select(r => r.Kind == MenuItemKind.Separator ? "-" : (r.Enabled ? string.Empty : "!") + r.Label)));
        });
        Add(services, "editor-popup", (sp, a) =>
        {
            if (Surface() is not { } surface || View() is not { } view)
            {
                return Task.FromResult("error no editor open");
            }

            sp.GetRequiredService<ActiveEditor>().Activate(surface);
            var at = Numbers(a);
            if (at.Length >= 2 && view.Controller is { } ctl)
            {
                var pos = ctl.RightDown((float)at[0], (float)at[1]);
                surface.ContextWord = surface.MisspelledAt(pos.B, pos.O);
                surface.RefreshToolbar();
            }

            sp.GetRequiredService<IContextMenuService>().Show(
                new MenuContext(EditorMenus.Menu, [], null, null, null, null, Barbican.Presentation.Commands.CommandSurface.ContextMenu),
                new MenuAnchor(at.ElementAtOrDefault(0) + 330, at.ElementAtOrDefault(1) + 130, null));
            return Task.FromResult("ok");
        });
        Add(services, "editor-scroll", (__, a) =>
        {
            var n = Numbers(a);
            View()?.ScrollTo(n.Length > 0 ? n[0] : 0);
            return Task.FromResult("ok");
        });
        Add(services, "editor-clipboard", (__, a) => Task.FromResult(ClipboardCommand(a)));
        Add(services, "editor-copy", (__, _) => WithAsync(v => Surface()?.Execute(EditorCommand.Copy), "ok"));
        Add(services, "editor-cut", (__, _) => WithAsync(v => Surface()?.Execute(EditorCommand.Cut), "ok"));
        Add(services, "editor-paste", async (_, a) =>
        {
            if (View() is not { } view)
            {
                return "error no editor open";
            }

            await view.PasteAsync(a.Contains("plain", StringComparison.Ordinal));
            return "ok";
        });
        Add(services, "editor-mentions", (__, a) =>
        {
            if (Surface() is not { } surface)
            {
                return Task.FromResult("error no editor open");
            }

            surface.Mentions = string.Equals(a.Trim(), "off", StringComparison.Ordinal) ? null : new SampleMentions();
            return Task.FromResult("ok");
        });
        Add(services, "editor-dump", (__, _) => Task.FromResult(Dump()));
        Add(services, "editor-latency", async (_, a) =>
        {
            var n = Numbers(a);
            return await LatencyAsync(n.Length > 0 ? (int)n[0] : 30);
        });
        Add(services, "editor-ime", (__, a) =>
        {
            // synthetic composition: "start", "update TEXT", "end TEXT" (the real IME is unproven)
            var parts = a.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            return Task.FromResult(View()?.SyntheticComposition(parts.Length > 0 ? parts[0] : string.Empty, parts.Length > 1 ? parts[1] : string.Empty) ?? "error no editor open");
        });
        return services;
    }
#pragma warning restore MA0051

    private static void Add(IServiceCollection services, string name, Func<IServiceProvider, string, Task<string>> run) =>
        services.AddSingleton<IAutomationCommand>(sp => new LambdaAutomationCommand(name, args => run(sp, args)));

    private static EditorView? View() => EditorTestHost.Instance.View;

    private static IEditorSurface? Surface() => View()?.Surface;

    private static Task<string> WithAsync(Action<EditorView> run, string ok)
    {
        if (View() is not { } view)
        {
            return Task.FromResult("error no editor open");
        }

        run(view);
        return Task.FromResult(ok);
    }

    private static double[] Numbers(string args) => [.. args.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).Where(d => !double.IsNaN(d))];

    private static string Open(IServiceProvider sp, string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var fixture = parts.Length > 0 ? parts[0] : "empty";
        var plain = parts.Contains("plain", StringComparer.Ordinal);
        var doc = EditorFixtures.Create(fixture);
        IEditorViewModel vm;
        if (parts.Contains("fakespell", StringComparer.Ordinal))
        {
            vm = new EditorViewModel(sp.GetRequiredService<IEditorSessionFactory>().Create(doc, plain), new SampleSpellEngine(), sp.GetRequiredService<IRewriteProvider>());
        }
        else
        {
            vm = sp.GetRequiredService<IEditorViewModelFactory>().Create(doc, plain);
        }

        var view = EditorTestHost.Instance.Open(vm);
        view.RtlSupport = parts.Contains("rtl", StringComparer.Ordinal);
        sp.GetRequiredService<ActiveEditor>().Activate((IEditorSurface)vm);
        return "ok";
    }

    private static string Select(string args)
    {
        if (Surface()?.Session is not { } s)
        {
            return "error no editor open";
        }

        if (string.Equals(args.Trim(), "all", StringComparison.Ordinal))
        {
            s.SelectAll();
            return "ok";
        }

        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static Pos? P(string t) => t.Split('.') is [var b, var o] && int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bi) && int.TryParse(o, NumberStyles.None, CultureInfo.InvariantCulture, out var oi) ? new Pos(bi, oi) : null;
        if (parts.Length == 0 || P(parts[0]) is not { } anchor)
        {
            return "error usage: editor-select all | B.O [B.O]";
        }

        var focus = parts.Length > 1 ? P(parts[1]) ?? anchor : anchor;
        s.SelectRange(anchor, focus);
        return "ok";
    }

    private static string ClipboardCommand(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        switch (parts.FirstOrDefault())
        {
            case "text":
                Clipboard.Put(null, parts.ElementAtOrDefault(1), [], null);
                return "ok";
            case "html":
                Clipboard.Put(parts.ElementAtOrDefault(1), "html", [], null);
                return "ok";
            case "image":
                var png = EditorFixtures.Create("image").Blocks.Select(b => b.Kind).OfType<BlockKind.Image>().First().Data.Bytes!;
                Clipboard.Put(null, null, [new ClipboardFile("shot.png", "image/png", png)], null);
                return "ok";
            case "get":
                var (html, text) = Clipboard.Content;
                return new JsonObject { ["html"] = html, ["text"] = text }.ToJsonString();
            default:
                return "error usage: editor-clipboard text TEXT | html HTML | image | get";
        }
    }

    private static string Dump()
    {
        if (View() is not { } view || view.Surface is not { } surface || view.Controller is not { } ctl)
        {
            return "error no editor open";
        }

        var s = surface.Session;
        var blocks = new JsonArray();
        for (var i = 0; i < s.Doc.Blocks.Count; i++)
        {
            var b = s.Doc.Blocks[i];
            var runs = new JsonArray();
            foreach (var r in b.Runs)
            {
                runs.Add(new JsonObject
                {
                    ["text"] = r.Text,
                    ["marks"] = string.Join(',', new[] { r.Marks.Bold ? "bold" : null, r.Marks.Italic ? "italic" : null, r.Marks.Underline ? "underline" : null, r.Marks.Strike ? "strike" : null, r.Marks.Code ? "code" : null, r.Marks.Link is null ? null : "link=" + r.Marks.Link, r.Marks.Color is null ? null : "color=" + r.Marks.Color, r.Marks.Highlight is null ? null : "hl=" + r.Marks.Highlight, r.Marks.Size is null ? null : "size=" + r.Marks.Size }.Where(x => x is not null)),
                });
            }

            var geo = i < ctl.Layout.Blocks.Count ? ctl.Layout.Blocks[i] : null;
            blocks.Add(new JsonObject
            {
                ["kind"] = b.Kind.GetType().Name,
                ["align"] = b.Align.ToString(),
                ["indent"] = b.Indent,
                ["text"] = b.Kind is BlockKind.Image or BlockKind.Rule ? string.Empty : b.Text,
                ["runs"] = runs,
                ["y"] = geo?.Y,
                ["h"] = geo?.Height,
            });
        }

        var caret = ctl.Caret();
        var marks = new JsonArray();
        for (var i = 0; i < s.Doc.Blocks.Count; i++)
        {
            foreach (var r in surface.Misspelled(i, s.Doc.Blocks[i].Text))
            {
                marks.Add(new JsonObject { ["block"] = i, ["start"] = r.Start, ["end"] = r.End });
            }
        }

        var t = surface.Toolbar;
        return new JsonObject
        {
            ["blocks"] = blocks,
            ["selection"] = new JsonObject { ["anchor"] = $"{s.Sel.Anchor.B}.{s.Sel.Anchor.O}", ["focus"] = $"{s.Sel.Focus.B}.{s.Sel.Focus.O}", ["collapsed"] = s.Sel.IsCollapsed },
            ["caret"] = new JsonObject { ["x"] = caret.X, ["y"] = caret.Y, ["h"] = caret.Height },
            ["toolbar"] = new JsonObject { ["bold"] = t.Bold, ["italic"] = t.Italic, ["underline"] = t.Underline, ["strike"] = t.Strike, ["code"] = t.Code, ["highlight"] = t.Highlight, ["link"] = t.Link, ["block"] = t.Block.GetType().Name, ["align"] = t.Align.ToString(), ["canUndo"] = t.CanUndo, ["canRedo"] = t.CanRedo, ["inTable"] = t.InTable, ["onImage"] = t.OnImage, ["hasSelection"] = t.HasSelection, ["onMisspelledWord"] = t.OnMisspelledWord },
            ["spell"] = marks,
            ["version"] = s.Version,
            ["sourceOpen"] = surface.SourceOpen,
            ["plain"] = surface.Plain,
            ["scroll"] = view.ScrollOffset,
            ["layoutHeight"] = ctl.Layout.Height,
            ["images"] = s.InlineImages.Count,
        }.ToJsonString();
    }

    private static async Task<string> LatencyAsync(int count)
    {
        if (View() is not { } view)
        {
            return "error no editor open";
        }

        var samples = new List<double>();
        for (var i = 0; i < count; i++)
        {
            var sw = Stopwatch.StartNew();
            view.TypeText("x");
            await view.NextFrameAsync();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        return new JsonObject { ["n"] = count, ["medianMs"] = Math.Round(samples[count / 2], 2), ["p95Ms"] = Math.Round(samples[Math.Min(count - 1, (int)(count * 0.95))], 2), ["maxMs"] = Math.Round(samples[^1], 2) }.ToJsonString();
    }

    private sealed class SampleMentions : IMentionSource
    {
        private static readonly MentionCandidate[] All = [new("Ada Lovelace", "ada@mail.example"), new("Grace Hopper", "grace@mail.example"), new("Alan Turing", "alan@mail.example")];

        public IReadOnlyList<MentionCandidate> Candidates(string query) =>
            [.. All.Where(c => query.Length == 0 || c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Email.Contains(query, StringComparison.OrdinalIgnoreCase))];

        public void Picked(MentionCandidate candidate)
        {
        }
    }

    private sealed class SampleSpellEngine : ISpellEngine
    {
        private static readonly Dictionary<string, string[]> Wrong = new(StringComparer.OrdinalIgnoreCase)
        {
            ["teh"] = ["the", "tea", "ten"],
            ["speling"] = ["spelling", "spieling", "spewing"],
            ["xyzzy"] = [],
        };

        public bool IsAvailable => true;

        public IReadOnlyList<SpellRange> Misspelled(string text)
        {
            var result = new List<SpellRange>();
            var i = 0;
            foreach (var raw in text.Split(' '))
            {
                var word = raw.Trim(',', '.');
                if (Wrong.ContainsKey(word))
                {
                    result.Add(new SpellRange(i, word.Length));
                }

                i += raw.Length + 1;
            }

            return result;
        }

        public IReadOnlyList<string> Suggestions(string word) => Wrong.TryGetValue(word, out var s) ? s : [];

        public void Add(string word) => Wrong.Remove(word);

        public void Ignore(string word) => Wrong.Remove(word);

        public void Reload()
        {
        }
    }
}
#endif

// Mirrors: Bulwark 1.21.2 components/email/reader-selection-menu.tsx:177-198 (ReaderSelectionMenu: a Copy row, a separator, then the add-ons' rows of the
// `reader-selection-menu` surface; `copy` runs navigator.clipboard.writeText on the selected text), :144-175 (the menu opens at the right-click point, only for non-blank text),
// :3-11 (the menu replaces the browser's own, so Copy stays); spec/plugins/slots.csv reader-selection-menu. In Barbican the body WebView has no browser menu, so the menu
// also opens when no add-on contributes a row (Copy alone). RD-08. Owner reader.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Body;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Menus;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Reader;

/// <summary>MENU-SELECTION: the rows and commands of the menu on selected message text.</summary>
public static class ReaderSelectionMenu
{
    /// <summary>The longest add-on key the argument carries (add-on ids are at most 60 characters, plus the owner).</summary>
    public const int MaxKeyLength = 200;

    /// <summary>The selected text of a menu opened for <paramref name="text"/>: cut to the bridge's cap, null when blank.</summary>
    public static string? Usable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Length > ShellMessage.MaxSelection ? text[..ShellMessage.MaxSelection] : text;
    }

    /// <summary>The rows for <paramref name="c"/>: Copy, then (after a separator) one row per add-on; empty when no text is selected.</summary>
    public static IReadOnlyList<MenuItemSpec> Rows(MenuContext c, IReadOnlyList<ReaderSelectionAddOn> addOns)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(addOns);
        if (Usable(c.SelectionText) is not { } text)
        {
            return [];
        }

        var rows = new List<MenuItemSpec> { Item(MenuGlyphs.Copy, "Copy", CommandIds.ReaderSelectionCopy, argument: text) };
        if (addOns.Count > 0)
        {
            rows.Add(Sep());
            rows.AddRange(addOns.Select(a => new MenuItemSpec(MenuItemKind.Item, a.Label, CommandIds.ReaderSelectionAddOn, Encode(a.Key, text), Icon: Glyph(a.Icon), Danger: a.Danger)));
        }

        return rows;
    }

    public static IMenuProvider Provider(Func<IReaderSelectionAddOns?> addOns)
    {
        ArgumentNullException.ThrowIfNull(addOns);
        return new DelegateMenuProvider(MenuIds.ReaderSelection, c => Rows(c, addOns()?.Current() ?? []));
    }

    /// <summary>The two commands: copy the selection, run an add-on's row on it.</summary>
    public static IReadOnlyList<IAppCommand> Commands(IClipboardService clipboard, Func<IReaderSelectionAddOns?> addOns)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(addOns);
        return
        [
            new DelegateAppCommand(CommandIds.ReaderSelectionCopy, c => Usable(c.Argument) is not null, (c, _) =>
            {
                if (Usable(c.Argument) is not { } text)
                {
                    return Task.FromResult(Result<Unit>.Fail(new ValidationError("Nothing is selected.")));
                }

                clipboard.SetText(text);
                return Task.FromResult(Result<Unit>.Ok(default));
            }),
            new DelegateAppCommand(CommandIds.ReaderSelectionAddOn, c => addOns() is not null && TryDecode(c.Argument, out _, out _), async (c, ct) =>
                addOns() is { } host && TryDecode(c.Argument, out var key, out var text)
                    ? await host.RunAsync(key, text, ct).ConfigureAwait(false)
                    : Result<Unit>.Fail(new ValidationError("The add-on action is no longer available."))),
        ];
    }

    /// <summary>"key", a line feed, then the selected text (a key never holds a line feed).</summary>
    public static string Encode(string key, string text) => key + "\n" + text;

    public static bool TryDecode(string? argument, out string key, out string text)
    {
        key = text = string.Empty;
        if (argument is null)
        {
            return false;
        }

        var at = argument.IndexOf('\n', StringComparison.Ordinal);
        if (at is <= 0 or > MaxKeyLength)
        {
            return false;
        }

        key = argument[..at];
        text = argument[(at + 1)..];
        return Usable(text) is not null && text.Length <= ShellMessage.MaxSelection;
    }

    private static string Glyph(string? icon) => icon switch
    {
        "printer" or "print" => MenuGlyphs.Print,
        "folder" => MenuGlyphs.Folder,
        "file" => MenuGlyphs.Copy,
        _ => MenuGlyphs.Add,
    };
}

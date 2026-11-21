// Mirrors: spec/menus.md MENU-READER (the reader's More menu) and MENU-ATT (an attachment chip); Bulwark components/email/email-viewer.tsx:5159 (the "More" menu:
// view source, forward as attachment, download .eml, download all) and reader-attachments.tsx (open, save as, rename, copy). I-200, I-183, I-226, I-227. Owner reader.
// Rows are data. "Open message" (an attached message), "Save to Files" and "Open" are not rows yet: Email/parse has no reader UI, there is no Files client in the
// reader, and opening an untrusted file with the system handler is a security question (docs/FABLE-REVIEW.md, m2-close-b).

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Files;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Rules;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Reader;

/// <summary>The rows of MENU-READER and MENU-ATT.</summary>
public static class ReaderMenus
{
    /// <summary>MENU-READER for the message on screen (<see cref="MenuContext.Targets"/> holds it); empty when none is open.</summary>
    public static IReadOnlyList<MenuItemSpec> Reader(MenuContext c, EmailBody? open, Func<string?, bool> hasJunk)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(hasJunk);
        if (c.Targets.Count != 1 || open is null)
        {
            return [];
        }

        var items = new List<MenuItemSpec>
        {
            Item("Tool", "View source", CommandIds.ViewSource),
            Item(MenuGlyphs.Forward, "Forward as attachment", CommandIds.ForwardAsAttachment),
            Item(MenuGlyphs.Download, "Download (.eml)", CommandIds.DownloadEml),
            Item(MenuGlyphs.Download, "Download all attachments (.zip)", CommandIds.DownloadAllZip, enabled: open.Attachments.Any(a => a.BlobId is not null)),
        };
        items.AddRange(RuleMenus.Section(c, hasJunk(c.Targets[0].AccountKey)));
        return items;
    }

    /// <summary>MENU-ATT for attachment <see cref="MenuContext.AttachmentIndex"/> of the message on screen.</summary>
    public static IReadOnlyList<MenuItemSpec> Attachment(MenuContext c, EmailBody? open)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (open is null || c.AttachmentIndex is not { } index || index < 0 || index >= open.Attachments.Count)
        {
            return [];
        }

        var a = open.Attachments[index];
        var arg = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var items = new List<MenuItemSpec>
        {
            Item(MenuGlyphs.Download, "Save as…", CommandIds.SaveAttachment, argument: arg, enabled: a.BlobId is not null),
            Item(MenuGlyphs.Rename, "Rename…", CommandIds.RenameAttachment, argument: arg),
        };
        if (a.BlobId is not null && FilesPreviewRules.KindOf(a.Type, a.Name ?? string.Empty) is FilesPreviewKind.Image or FilesPreviewKind.Text or FilesPreviewKind.Eml)
        {
            items.Insert(0, Item(MenuGlyphs.Open, "Open", CommandIds.OpenAttachment, argument: arg));
        }

        if (open.Attachments.Count(x => x.BlobId is not null) > 1)
        {
            items.Add(Item(MenuGlyphs.Download, "Download all (.zip)", CommandIds.DownloadAllZip));
        }

        items.Add(Sep());
        items.Add(Item(MenuGlyphs.Copy, "Copy file name", CommandIds.CopyAttachmentName, argument: arg));
        return items;
    }

    public static IEnumerable<MenuItemSpec> ToolbarActions(MenuContext context, IMenuDataSource data) =>
        MailMenus.Main(context, data).Where(r => r.SubmenuId is MenuIds.MailMove or MenuIds.MailTag ||
            r.CommandId is CommandIds.MarkRead or CommandIds.MarkUnread or CommandIds.ReportSpam or CommandIds.NotSpam or CommandIds.ReportPhishing);

    /// <summary>The providers: MENU-READER and MENU-ATT over what is on screen now.</summary>
    public static IReadOnlyList<IMenuProvider> Providers(Func<EmailBody?> open, Func<string?, bool> hasJunk, IMenuDataSource? data = null)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(hasJunk);
        return
        [
            new DelegateMenuProvider(MenuIds.Reader, c =>
            {
                var rows = Reader(c, open(), hasJunk);
                if (rows.Count == 0 || !c.IncludeReaderOverflow || data is null) return rows;
                return [.. ToolbarActions(c, data), Sep(), .. rows];
            }),
            new DelegateMenuProvider(MenuIds.Attachment, c => Attachment(c, open())),
        ];
    }
}

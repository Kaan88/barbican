// Mirrors: M2 plan section 4.1 (CommandIds: the complete list; the owner stream of each id in its comment); Rust ctxmenu.rs pick :830, reader.rs menu :986 and pick :1084, palette.rs commands :92,
// shortcuts.rs tables; Bulwark components/email/message-actions.ts, mailbox-context-menu.tsx, mail-list-menus.tsx, account-switcher.tsx, lib/keyboard-shortcuts/mail.ts. Stage F of M2; owner menus.
// Ids not built in M2 (compose, calendar, contacts, files, settings) are listed so menus and the palette can name them: without a registered handler they show disabled.

namespace Barbican.Presentation.Commands;

/// <summary>One constant per command; every id has the owner stream in its comment.</summary>
public static class CommandIds
{
    // ---- mail actions (owner actions) ----
    public const string MarkRead = "mail.read";
    public const string MarkUnread = "mail.unread";
    public const string Star = "mail.star";
    public const string Unstar = "mail.unstar";
    public const string ToggleStar = "mail.toggle-star";
    public const string ToggleRead = "mail.toggle-read";
    public const string Pin = "mail.pin";
    public const string Unpin = "mail.unpin";
    public const string Tag = "mail.tag";
    public const string Untag = "mail.untag";
    public const string MoveTo = "mail.move";
    public const string MoveToDialog = "mail.move-dialog";
    public const string MoveToInbox = "mail.move-inbox";
    public const string Archive = "mail.archive";
    public const string Delete = "mail.delete";
    public const string DeleteForever = "mail.delete-forever";
    public const string Restore = "mail.restore";
    public const string RemoveFromFolder = "mail.remove-from-folder";
    public const string ReportSpam = "mail.spam";
    public const string NotSpam = "mail.not-spam";
    public const string Undo = "mail.undo";
    public const string CopyToAccount = "mail.copy-to-account";

    // ---- folder and tag actions (owner actions) ----
    public const string MarkFolderRead = "folder.mark-read";
    public const string RefreshFolder = "folder.refresh";
    public const string NewFolder = "folder.new";
    public const string NewSubfolder = "folder.new-sub";
    public const string RenameFolder = "folder.rename";
    public const string DeleteFolder = "folder.delete";
    public const string EmptyTrash = "folder.empty-trash";
    public const string EmptySpam = "folder.empty-spam";
    public const string ImportMessages = "folder.import";
    public const string ExportMbox = "folder.export-mbox";
    public const string ExportEml = "folder.export-eml";
    public const string ShowTagMessages = "tag.show";
    public const string MarkTagRead = "tag.mark-read";
    public const string NewTag = "tag.new";
    public const string NewTagInside = "tag.new-inside";
    public const string RenameTag = "tag.rename";
    public const string SetTagColour = "tag.colour";
    public const string DeleteTag = "tag.delete";
    public const string ManageTags = "tag.manage";

    // ---- list navigation and selection (owner actions) ----
    public const string SelectNext = "list.next";
    public const string SelectPrevious = "list.previous";
    public const string ExtendDown = "list.extend-down";
    public const string ExtendUp = "list.extend-up";
    public const string OpenSelected = "list.open";
    public const string CloseReader = "list.close";
    public const string SelectAll = "list.select-all";
    public const string SelectAllInFolder = "list.select-all-folder";
    public const string ClearSelection = "list.clear";
    public const string ToggleSelect = "list.toggle-select";
    public const string RefreshList = "list.refresh";

    // ---- reader (owner reader) ----
    public const string Reply = "mail.reply";
    public const string ReplyAll = "mail.reply-all";
    public const string Forward = "mail.forward";
    public const string ForwardAsAttachment = "mail.forward-attachment";
    public const string Print = "mail.print";
    public const string ViewSource = "mail.view-source";
    public const string DownloadEml = "mail.download-eml";
    public const string DownloadAllZip = "mail.download-zip";
    public const string OpenMessage = "mail.open-message";
    public const string CopySenderAddress = "mail.copy-sender";
    public const string CopyMessageText = "mail.copy-text";
    public const string CopyAddress = "address.copy";
    public const string NewMessageTo = "address.compose";
    public const string AddToContacts = "address.add-contact";
    public const string FindFromSender = "mail.find-from-sender";
    public const string OpenAttachment = "attachment.open";
    public const string SaveAttachment = "attachment.save";
    public const string SaveAttachmentToFiles = "attachment.save-to-files";
    public const string CopyAttachmentName = "attachment.copy-name";
    public const string Unsubscribe = "mail.unsubscribe";
    public const string SendReadReceipt = "mail.send-receipt";
    public const string CopyVerificationCode = "mail.copy-code";
    public const string TrustSender = "mail.trust-sender";
    public const string ShowRemoteImages = "mail.show-remote-images";

    // ---- quick rules (owner search, wave 2; menus names them) ----
    public const string RuleMoveSender = "rule.move-sender";
    public const string RuleMoveDomain = "rule.move-domain";
    public const string RuleMoveList = "rule.move-list";
    public const string RuleMarkRead = "rule.mark-read";
    public const string RuleTag = "rule.tag";
    public const string RuleBlockSender = "rule.block";
    public const string ManageRules = "rule.manage";

    // ---- search (owner search, wave 2) ----
    public const string FocusSearch = "search.focus";
    public const string GlobalSearch = "search.global";
    public const string OpenSearchHit = "search.open-hit";

    // ---- accounts (owner menus for the UI, unified for the work) ----
    public const string SwitchAccount = "account.switch";
    public const string SignOut = "account.sign-out";
    public const string SignOutAll = "account.sign-out-all";
    public const string SetDefaultAccount = "account.set-default";
    public const string AddAccount = "account.add";
    public const string ManageAccounts = "account.manage";
    public const string UnifiedInbox = "account.unified";

    // ---- shell, palette, shortcuts (owner menus) ----
    public const string OpenPalette = "app.palette";
    public const string ShowShortcuts = "app.shortcuts";
    public const string ToggleTheme = "app.theme";
    public const string GoMail = "go.mail";
    public const string GoCalendar = "go.calendar";
    public const string GoContacts = "go.contacts";
    public const string GoFiles = "go.files";
    public const string GoSettings = "go.settings";
    public const string GoAdmin = "go.admin";
    public const string GoInbox = "go.inbox";
    public const string GoStarred = "go.starred";
    public const string GoSent = "go.sent";
    public const string GoDrafts = "go.drafts";
    public const string GoAll = "go.all";
    public const string SkipToList = "app.skip-to-list";
    public const string CyclePanes = "app.cycle-panes";
    public const string OpenA11y = "app.accessibility";

    // ---- the body (owner menus; the reader view runs them in the WebView) ----
    public const string BodyCopy = "body.copy";
    public const string BodySelectAll = "body.select-all";

    // ---- later milestones, named so menus and the palette can list them disabled ----
    public const string Compose = "compose.new";
    public const string ComposeShowCc = "compose.show-cc";
    public const string ComposeShowBcc = "compose.show-bcc";
}

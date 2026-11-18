// Mirrors: Bulwark components/settings/settings-app.tsx (tab order and groups), src/Barbican.Core/Settings/settings-schema.json. The page list is data: the screen (set-ui) builds its navigation from it, each page factory (ISettingsPageFactory) answers for one id.
// Frozen in the M6 plan commit; owner integrator.

namespace Barbican.Presentation.Settings;

/// <summary>The pages of the settings screen in navigation order.</summary>
public static class SettingsNav
{
    /// <summary>Every page, grouped top, mail, apps, bottom (the schema's groups).</summary>
    public static IReadOnlyList<SettingsPageInfo> Pages { get; } =
    [
        new(SettingsPageIds.Account, "Account", "top", SettingsPageOwner.SetUi, false, "name addresses sign out accounts default order archive push"),
        new(SettingsPageIds.Security, "Password & sign-in", "top", SettingsPageOwner.SetUi, false, "password two-step totp app password api key encryption app lock pin"),
        new(SettingsPageIds.Privacy, "Privacy & security", "top", SettingsPageOwner.SetUi, false, "external content images trusted senders read receipt favicon telemetry avatars"),
        new(SettingsPageIds.Notifications, "Notifications", "top", SettingsPageOwner.SetUi, false, "notify sound badge push inbox only"),
        new(SettingsPageIds.General, "General", "top", SettingsPageOwner.SetUi, false, "language time zone date time format first day of week tour protocol handler default mail app"),
        new(SettingsPageIds.Appearance, "Appearance", "top", SettingsPageOwner.SetUi, false, "theme style density layout labels icons counts tint dark light text scale more contrast"),
        new(SettingsPageIds.Accessibility, "Accessibility", "top", SettingsPageOwner.SetUi, false, "contrast motion focus targets font reading mask cursor simple mode"),
        new(SettingsPageIds.MessageList, "Message list", "mail", SettingsPageOwner.SetUi, false, "list columns swipe order delete archive confirm tags"),
        new(SettingsPageIds.Reading, "Reading", "mail", SettingsPageOwner.SetUi, false, "reading return to list attachments images"),
        new(SettingsPageIds.Composing, "Composing", "mail", SettingsPageOwner.SetUi, false, "compose signature reply under reminder proofreading mentions rtl"),
        new(SettingsPageIds.Identities, "Identities", "mail", SettingsPageOwner.SrvUi, false, "identity sender name bcc signature html order primary"),
        new(SettingsPageIds.Templates, "Templates", "mail", SettingsPageOwner.SrvUi, false, "template placeholder snippet import export"),
        new(SettingsPageIds.Organize, "Folders & tags", "mail", SettingsPageOwner.SrvUi, false, "folder tag subscribe role icon count pin mute mark read rename delete move"),
        new(SettingsPageIds.Filters, "Filters", "mail", SettingsPageOwner.SrvUi, false, "filter rule sieve condition action apply existing mail"),
        new(SettingsPageIds.Vacation, "Out of office", "mail", SettingsPageOwner.SrvUi, false, "out of office vacation auto reply away forward calendar"),
        new(SettingsPageIds.Recover, "Recover deleted items", "mail", SettingsPageOwner.SrvUi, false, "recover deleted archive restore"),
        new(SettingsPageIds.Assistant, "AI assistant", "mail", SettingsPageOwner.SetUi, false, "ai assistant provider model api key actions memory instructions"),
        new(SettingsPageIds.Calendar, "Calendar", "apps", SettingsPageOwner.SetUi, false, "calendar view hours week alerts share import subscribe tasks"),
        new(SettingsPageIds.Contacts, "Contacts", "apps", SettingsPageOwner.SetUi, false, "contacts address book import export sort directory"),
        new(SettingsPageIds.Files, "Files", "apps", SettingsPageOwner.SetUi, false, "files view sort thumbnails hidden download filenames"),
        new(SettingsPageIds.Addons, "Add-ons", "apps", SettingsPageOwner.Later, false, "plugins add-ons"),
        new(SettingsPageIds.Backup, "Backup & import", "bottom", SettingsPageOwner.SetUi, false, "backup export import reset settings"),
        new(SettingsPageIds.Troubleshooting, "Troubleshooting", "bottom", SettingsPageOwner.SetUi, true, "debug logs devtools refresh cache"),
        new(SettingsPageIds.About, "About", "bottom", SettingsPageOwner.SetUi, false, "about version licence links"),
    ];

    /// <summary>The page with this id, or null.</summary>
    public static SettingsPageInfo? Find(string id) => Pages.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
}

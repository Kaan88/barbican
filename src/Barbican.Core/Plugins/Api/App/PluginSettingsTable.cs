// Mirrors: Bulwark lib/settings-schema.ts:154-418 ENTRIES (the settings an add-on may change, with the direction a safety net may move in and the values left out), :420-451 DENIED_SETTINGS (refused on purpose), :453-465 NOT_YET_SETTINGS and
// lib/plugin-sandbox/host-api-app.ts:92-149 SHARED_SETTINGS (what app.getSettings reports). Everything not listed is refused.
// Barbican's table is Bulwark's, cut to the keys that have a row in Barbican's settings catalog (src/Barbican.Core/Settings/settings-schema.json) and that something in Barbican reads (a catalog row marked unwired is refused as unavailable,
// because "applied" would be a lie). Left out for that reason: locale (the language), the hour, days, words and list kinds (calendarDayStartHour/EndHour, calendarWorkingDays, attachmentReminderKeywords, hoverActions), the account
// marker pair (tintListRowsByAccount is a three-way select here, showAccountDot has no row), enableCross*View, protocolOpenMode, showAdvancedSettings, appFont and the other accessibility switches Barbican has no row for.
// Never reachable by name here, as in Bulwark: credentials, server and admin settings, add-on permission state, anything that sends mail or data elsewhere (the push relay), the external content policy, trusted senders, signatures, the
// session timeout and every other security toggle (the DENIED list below; a catalog key that is in neither table is refused as not_allowed).
using System.Collections.Frozen;
using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Plugins.Api.App;

internal static class PluginSettingsTable
{
    /// <summary>Most settings one call names (c.settings.ts:38).</summary>
    internal const int MaxPerCall = 20;

    private static readonly string[] Switches =
    [
        "showToolbarLabels", "colorfulSidebarIcons", "showFolderTotalCount", "sidebarAccountSwitcher", "showRailAccountList", "proInterface", "enableUnifiedMailbox", "unifiedCrossAccount", "includeGroupInUnified",
        "showSharedMailAccounts", "compactSharedMailboxes", "showOnboardingOnNewDevices", "disableThreading", "groupListByDate", "showPreview", "listColumns", "tintListRowsByTag", "showVerificationCodes", "showAttachmentChips",
        "clearSearchOnFolderChange", "returnToListAfterAction", "emailAlwaysLightMode", "attachmentImagePreviewsEnabled", "hideInlineImageAttachments", "filenameLowercase", "filenameStripDiacritics", "filenameCollapseSeparators",
        "plainTextMode", "recipientMentionsEnabled", "rtlEditingSupport", "replyUnderMessage", "quickReplyEnabled", "autoSelectReplyIdentity", "signatureNewMailEnabled", "signatureReplyForwardEnabled", "signatureSeparatorEnabled",
        "sendConfirmation", "attachmentReminderEnabled", "emptySubjectWarningEnabled", "placeholderWarningEnabled", "emailNotificationsEnabled", "emailNotificationSound", "pushNotifyInboxOnly", "faviconUnreadBadge",
        "unreadBadgeAllAccounts", "calendarNotificationsEnabled", "calendarNotificationSound", "doNotDisturb", "visualAlerts", "calendarInvitationParsingEnabled", "showTimeInMonthView", "calendarTintedEvents", "showWeekNumbers",
        "calendarLimitHours", "calendarHideNonWorkingDays", "calendarFreeScroll", "showBirthdayCalendar", "enableCalendarTasks", "showTasksOnCalendar", "groupContactsByLetter", "sortContactsByLastName", "showContactsDirectory",
        "underlineLinks", "colorBlindPalette", "singleKeyShortcuts", "showRowActionButton", "mailFitWidth", "confirmArchive",
    ];

    private static readonly string[] Choices =
    [
        "density", "interfaceLayout", "mailLayout", "toolbarPosition", "dateFormat", "dateLocale", "timeFormat", "firstDayOfWeek", "mvInboxType", "swipeRightAction", "swipeLeftAction", "archiveMode", "emailsPerPage",
        "searchDefaultScope", "markAsReadDelay", "messageSpacing", "mailAttachmentAction", "filenameSpaceReplacement", "mentionAddsRecipientTo", "replyIdentityMatch", "signaturePosition", "notificationSoundChoice", "toastDuration",
        "calendarMonthTimedEvents", "calendarHoverPreview", "fontSize", "plainTextFont", "lineSpacing", "letterSpacing", "wordSpacing", "paragraphSpacing", "readingWidth", "contrast", "reduceMotion", "largeTargets", "mailViewMode",
        "mailTextScale", "mailMinFontSize", "mailOverrideStyles", "unreadCountStyle",
    ];

    // refused on purpose (settings-schema.ts:420-451); the key is not_allowed, never unknown
    private static readonly FrozenSet<string> Denied = new[]
    {
        "externalContentPolicy", "trustedSenders", "trustedSendersAddressBook", "emailDownloadTemplate", "attachmentDownloadTemplate", "bundleDownloadTemplate", "signatureDetails", "defaultReplyMode", "hideAccountSwitcher",
        "subAddressDelimiter", "pushRelayUrl", "proofreading", "proofreadingPlugins", "readAloudVoice", "allMailFolderIds", "hiddenSharedMailAccounts", "sharedMailAccountOrder", "notificationMutes", "hiddenCompanyCalendars",
        "sharedCalendarColors", "folderIcons", "folderCountModes", "preferredIdentityIds", "onboardingCompleted", "tourCompleted", "emailKeywordsChoiceTracked", "mvUnreadSectionCollapsed", "mvEverythingElseCollapsed",
        "vacationCalendarBlocks", "explicitSettings", "proofreadingNoticeSeen", "sessionTimeout", "debugMode", "debugCategories", "settingsSyncDisabled", "emailKeywords", "nestedTags", "expandedFilterView", "sidebarApps",
        "keepAppsLoaded", "messageListOrder", "filesSettings", "activeThemeId", "birthdayCalendarColor", "vacationBlockCalendar", "writingLanguage", "calendarNarrowWeekAllDays", "printTheme",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, PluginSettingRule> Rules = BuildRules();

    /// <summary>The keys app.getSettings reports (host-api-app.ts:92-149), cut to the ones Barbican has a row for. Plain values only, nothing about other people and nothing that addresses a server.</summary>
    internal static readonly IReadOnlyList<string> Shared =
    [
        "fontSize", "density", "interfaceLayout", "mailLayout", "toolbarPosition", "showToolbarLabels", "reduceMotion", "proInterface", "contrast", "underlineLinks", "focusRing", "largeTargets", "singleKeyShortcuts",
        "toastDuration", "dateFormat", "timeFormat", "firstDayOfWeek", "timeZone", "disableThreading", "showPreview", "listColumns", "emailsPerPage", "showVerificationCodes", "showAttachmentChips", "groupListByDate",
        "markAsReadDelay", "mailAttachmentAction", "emailAlwaysLightMode", "returnToListAfterAction", "plainTextFont", "deleteAction", "archiveMode", "permanentlyDeleteJunk", "confirmBulkDelete", "sendDelaySeconds",
        "sendConfirmation", "signaturePosition", "signatureSeparatorEnabled", "signatureNewMailEnabled", "signatureReplyForwardEnabled", "autoSelectReplyIdentity", "showWeekNumbers", "enableCalendarTasks", "showBirthdayCalendar",
        "emailNotificationsEnabled", "emailNotificationSound", "calendarNotificationsEnabled", "enableUnifiedMailbox", "colorfulSidebarIcons", "showFolderTotalCount", "sidebarAccountSwitcher", "showRailAccountList",
        "unifiedCrossAccount", "includeGroupInUnified", "tintListRowsByAccount", "showSharedMailAccounts", "compactSharedMailboxes", "dateLocale", "showOnboardingOnNewDevices", "mvInboxType", "messageListOrderScope",
        "tintListRowsByTag", "senderFavicons", "showAvatarsInJunk", "hoverActions", "swipeRightAction", "swipeLeftAction", "searchDefaultScope", "clearSearchOnFolderChange", "messageSpacing", "attachmentImagePreviewsEnabled",
        "hideInlineImageAttachments", "filenameSpaceReplacement", "filenameLowercase", "filenameStripDiacritics", "filenameCollapseSeparators", "postExportAction", "recipientMentionsEnabled", "mentionAddsRecipientTo", "rtlEditingSupport",
        "quickReplyEnabled", "replyUnderMessage", "replyIdentityMatch", "attachmentReminderEnabled", "emptySubjectWarningEnabled", "placeholderWarningEnabled", "notificationSoundChoice", "pushNotifyInboxOnly", "faviconUnreadBadge",
        "unreadBadgeAllAccounts", "calendarNotificationSound", "doNotDisturb", "visualAlerts", "calendarInvitationParsingEnabled", "showTimeInMonthView", "calendarTintedEvents", "calendarMonthTimedEvents", "calendarLimitHours",
        "calendarDayEndHour", "calendarHideNonWorkingDays", "calendarFreeScroll", "calendarHoverPreview", "showTasksOnCalendar", "groupContactsByLetter", "sortContactsByLastName", "showContactsDirectory", "lineSpacing",
        "letterSpacing", "wordSpacing", "paragraphSpacing", "readingWidth", "showRowActionButton", "mailViewMode", "mailTextScale", "mailMinFontSize", "mailOverrideStyles", "mailFitWidth", "confirmDelete", "confirmArchive",
        "unreadCountStyle", "colorBlindPalette", "theme", "calendarView",
    ];

    /// <summary>The names an add-on may change (the table), for tests and diagnostics.</summary>
    internal static IReadOnlyCollection<string> AllowedNames => Rules.Keys;

    /// <summary>The names refused on purpose.</summary>
    internal static IReadOnlyCollection<string> DeniedNames => Denied;

    /// <summary>Bulwark's two reader-friendly names for stored settings (c.settings.ts:57-58): calendarView is the calendar's viewMode.</summary>
    internal static string Canonical(string name) => string.Equals(name, "calendarView", StringComparison.Ordinal) ? "viewMode" : name;

    /// <summary>The setting an add-on named, or why there is none (c.settings.ts:73-78).</summary>
    internal static (PluginResolvedSetting? Setting, PluginSettingRefusal? Refusal) Resolve(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = Canonical(name);
        if (!Rules.TryGetValue(key, out var rule))
        {
            return (null, Denied.Contains(key) || SettingsCatalog.RowOfKey(key) is not null ? PluginSettingRefusal.NotAllowed : PluginSettingRefusal.Unknown);
        }

        var row = rule.RowId is { } id ? SettingsCatalog.RowOf(id) : SettingsCatalog.RowOfKey(key);
        if (row is null) return (null, PluginSettingRefusal.Unknown);
        var setting = new PluginResolvedSetting(key, rule, row, rule.PrefsKey ?? key);
        // nothing in Barbican reads an unwired key: a change would be reported as applied and do nothing
        return row.Status == SettingRowStatus.Unwired ? (setting, PluginSettingRefusal.Unavailable) : (setting, null);
    }

    /// <summary>The setting of a key the app.getSettings list names (no refusal logic: reading is not allow-listed one by one).</summary>
    internal static SettingRow? ReadRow(string name)
    {
        var key = Canonical(name);
        return Rules.TryGetValue(key, out var rule) && rule.RowId is { } id ? SettingsCatalog.RowOf(id) : SettingsCatalog.RowOfKey(key);
    }

    internal static string ReadPrefsKey(string name)
    {
        var key = Canonical(name);
        return Rules.TryGetValue(key, out var rule) ? rule.PrefsKey ?? key : key;
    }

    private static FrozenDictionary<string, PluginSettingRule> BuildRules()
    {
        var rules = new Dictionary<string, PluginSettingRule>(StringComparer.Ordinal);
        foreach (var key in Switches) rules[key] = new PluginSettingRule(PluginSettingKind.Boolean);
        foreach (var key in Choices) rules[key] = new PluginSettingRule(PluginSettingKind.Choice);
        // the delete and privacy safety nets move one way only (settings-schema.ts:247-259,370)
        rules["confirmBulkDelete"] = new PluginSettingRule(PluginSettingKind.Boolean, Only: true);
        rules["confirmDelete"] = new PluginSettingRule(PluginSettingKind.Boolean, Only: true);
        rules["permanentlyDeleteJunk"] = new PluginSettingRule(PluginSettingKind.Boolean, Only: false);
        rules["senderFavicons"] = new PluginSettingRule(PluginSettingKind.Boolean, Only: false);
        rules["showAvatarsInJunk"] = new PluginSettingRule(PluginSettingKind.Boolean, Only: false);
        // values Bulwark's table leaves out: "permanent" makes every later delete final, "trash" deletes the exported mail, 60 s is not one of its undo-send choices
        rules["deleteAction"] = new PluginSettingRule(PluginSettingKind.Choice, Left: ["permanent"]);
        rules["postExportAction"] = new PluginSettingRule(PluginSettingKind.Choice, Left: ["trash"]);
        rules["sendDelaySeconds"] = new PluginSettingRule(PluginSettingKind.Choice, Left: ["60"]);
        rules["messageListOrderScope"] = new PluginSettingRule(PluginSettingKind.Choice, Values: ["inbox", "all"]);
        rules["timeZone"] = new PluginSettingRule(PluginSettingKind.TimeZone);
        // the theme and the calendar's opening view are keyless catalog rows (S-keyless-025, S-keyless-049) stored under their own prefs keys
        rules["theme"] = new PluginSettingRule(PluginSettingKind.Choice, RowId: "S-keyless-025");
        rules["viewMode"] = new PluginSettingRule(PluginSettingKind.Choice, PrefsKey: "calendarViewMode", RowId: "S-keyless-049");
        return rules.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

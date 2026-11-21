# M6b feature parity backlog

The goal is FUNCTIONAL parity with Bulwark 1.21.2: a feature counts as done only when it works
end to end against the server behaviour, not when a placeholder or an untested adapter exists.
Feature work comes before further UI polish. Linux (M9) is deferred indefinitely.

| status | id(s) | feature | area | size | main files likely touched | depends on |
|---|---|---|---|---|---|---|
| open | M6b-api-inventory.md:133 | Admin config API for add-ons (`admin.*`) | add-on API | S | `src/Barbican.Core/Plugins/Api/App/PluginAppSettingsMethods.cs` | admin (M7) |
| open | M6b-api-inventory.md:134 | Crypto API: public keys, WebAuthn, encryption-at-rest | add-on API | M | `src/Barbican.Core/Plugins/Api/AccountSettings/PluginAccountSettingsMethods.cs`, `src/Barbican.Core/Settings/Account/AccountSecurity.cs` | account security |
| open | M6b-api-inventory.md:139 | Keystore API: agree, list, decrypt, sign | add-on API | M | `src/Barbican.Core/Plugins/Api/AccountSettings/PluginAccountSettingsMethods.cs` | user consent, crypto |
| open | M6b-api-inventory.md:137 | Plugin network: `http.post`, `proxy.*` | add-on API | M | `src/Barbican.Core/Plugins/Bridge/PluginHttpFetch.cs` | proxy/secret service |
| open | M6b-api-inventory.md:138 | Scoped JMAP blob and keyword methods (6) | add-on API | M | `src/Barbican.Core/Plugins/Api/MailActions/PluginMailBlobs.cs`, `PluginEmailSetKeyword.cs` | JMAP client extensions |
| open | M6b-api-inventory.md:135 | `email.forward` and `email.getAttachmentImage` | add-on API | M | `src/Barbican.Core/Plugins/Api/MailActions/PluginMailActionMethods.cs` | send approvals; JPEG/WebP decode |
| open | M6b-api-inventory.md:136 | `files.attach` for add-ons | add-on API | S | `src/Barbican.Core/Plugins/Api/Composer/PluginComposerEditMethods.cs`, `src/Barbican.Core/Plugins/Api/Files/PluginFilesMethods.cs` | composer mediation |
| open | M6b-api-inventory.md:141 | `search.run`/`search.clear` plugin filter chip | add-on API | M | `src/Barbican.Core/Plugins/Api/Mail/PluginEmailSearch.cs`, `src/Barbican.Presentation/Mail/MessageListViewModel.cs` | plugin-filter state |
| open | M6b-api-inventory.md:142 | `tabs.*` category-tab API | add-on API | M | `src/Barbican.Core/Plugins/Api/MailTags/PluginKeywordMethods.cs` | category tabs |
| partial | M6b-api-inventory.md:143 | Slot hosts: `ui.setDayLabels` done (calendar day heads, month cells and mini month) and `ui.openDialog` done (`plugin-dialog` slot in the dialog host, `onResult` callback channel; see FABLE-REVIEW "add-on dialogs"); setPanel, setSidePanel, setListFilters, setSectionTitle open | add-on API | M | `src/Barbican.App/Views/Plugins/PluginDialogPresenter.cs`, `src/Barbican.App/Views/Plugins/PluginPageScreen.cs` | slot host work |
| open | M6b-api-inventory.md:143 | Print-theme host: `ui.registerPrintTheme`/`unregisterPrintTheme` | add-on API | S | `src/Barbican.App/Views/Plugins/PluginPrintPresenter.cs` | print settings |
| open | M6b-api-inventory.md:144 | Staged upload store for `upfiles.get`/`save` | add-on API | M | `src/Barbican.Core/Plugins/Payloads/FileUploadPayload.cs` | upload pipeline |
| open | M6b-api-inventory.md:145 | `user.returnToOwnAccount` impersonation return | add-on API | S | `src/Barbican.Core/Plugins/Api/Account/PluginAccountMethods.cs` | impersonation session |
| open | M6b-plan.md:38 | `compose.open` keeps HTML body and attachments | add-on API | M | `src/Barbican.Core/Plugins/Api/MailActions/PluginComposeOpen.cs` | composer API |
| done | SH-10, AO-03 | Add-on `app-page` frame mount inside plugin pages (`PluginPageFrameHost`, `PluginSlotFrames`). Evidence: `tools/frames-probe-replay.py` hidden run on the pinned bootstrap (36 steps, 0 failed): frame mounted beside the rail and sized 1224x687 in a 1280x720 window, page props `pageId`/`path` drawn, day labels read from the automation tree in day, week and month views and as a mini-month dot on a free day | add-on API | M | `src/Barbican.App/Views/Plugins/PluginPageScreen.cs` | slot host |
| open | G-080 | Admin writes: accounts, groups, domains/DKIM, API keys, OAuth, settings, spam training, queue retry | admin | L | `src/Barbican.Core/Accounts/AccountStore.cs`, `src/Barbican.App/Views/Settings/SetUiScreenFactory.cs` | M7; admin client |
| open | G-086 | Organisation announcements and disclaimers | admin | M | `src/Barbican.Core/Settings/Pages/SettingsCatalog.cs`, `src/Barbican.App/Views/SignIn/SignInView.xaml.cs` | admin config source |
| open | G-043 | Share calendars, address books and mail folders | calendar | M | `src/Barbican.Core/Calendar/CalendarStore.cs`, `src/Barbican.Core/Contacts/AddressBooks.cs`, `src/Barbican.App/Views/SettingsServer/SrvUiOrganizePage.cs` | JMAP shareWith/ACL |
| open | CA-03 | Event editor date/time pickers and calendar select | calendar | M | `src/Barbican.App/Views/Calendar/Editors/CalendarCardKit.cs` | in-window picker |
| done (0382848) | G-004 | ICS import asks before keeping file alerts | calendar | S | `src/Barbican.App/Views/Calendar/Editors/CalendarCardHost.Dialogs.cs` | import flow |
| done (0382848) | G-005 | Event alerts more than 8 days ahead still fire | calendar | S | `src/Barbican.Core/Calendar/CalendarSettings.cs`, `src/Barbican.App/Views/Calendar/Reminders/ReminderPopupView.cs` | alert engine |
| open | G-076 | Attach from Files picker | composer | M | `src/Barbican.App/Views/Compose/ComposerView.Files.cs`, `src/Barbican.App/Views/Files/FilesScreen.cs` | Files service |
| open | G-079 | Insert a link to a Files file | composer | S | `src/Barbican.App/Views/Editor/LinkDialogRequest.cs`, `src/Barbican.App/Views/Compose/ComposerView.Files.cs` | Files service |
| open | CP-02 | Composer Tags menu (compose-time tags) | composer | M | `src/Barbican.App/Views/Compose/ComposerView.Menus.cs`, `src/Barbican.Presentation/Compose/ComposerViewModel.cs` | tag model |
| open | G-075 | @mention recipients with chips | composer | M | `src/Barbican.App/Views/Compose/RecipientFieldView.cs`, `src/Barbican.App/Views/Editor/EditorToolbarView.cs` | contact lookup |
| open | G-063 | Right-to-left editing support | composer | M | `src/Barbican.App/Views/Editor/EditorView.cs`, `src/Barbican.Core/Editor/Doc.cs` | text layout |
| open | G-078 | Proofreading next/previous issue navigation | composer | S | `src/Barbican.App/Views/Compose/ComposerView.Proofreading.cs` | checker registry |
| open | CP-10 | Composer offline hint | composer | S | `src/Barbican.App/Views/Compose/ComposerView.xaml.cs`, `src/Barbican.App/Shell/ShellLifecycle.cs` | connectivity service |
| open | CP-06 | Editor: table editing, image resize, alt text, H3 menu | composer | M | `src/Barbican.App/Views/Editor/EditorToolbarView.cs`, `src/Barbican.Core/Editor/TableOp.cs` | editor core |
| open | G-011 | Contact activity tiles (related people already shown) | contacts | M | `src/Barbican.App/Views/Contacts/ContactsDetailView.cs`, `src/Barbican.Core/Contacts/ContactDetail.cs` | activity feed |
| open | CT-02 | Contact form person/organisation switch and extra fields | contacts | M | `src/Barbican.App/Views/Contacts/ContactsFormPanel.cs`, `src/Barbican.Core/Contacts/ContactFields.cs` | contacts model |
| open | FL-02 | File thumbnails and zoomable image preview | files | M | `src/Barbican.App/Views/Files/FilesListView.cs`, `src/Barbican.App/Views/Files/FilesPreviewView.cs` | image decode |
| open | G-083 | Files sync, WebDAV and WOPI editing | files | L | `src/Barbican.Core/Files/FileStore.cs`, `src/Barbican.App/Views/Files/FilesScreen.cs` | new sync service |
| open | FL-01 | File breadcrumb overflow and folder tree | files | S | `src/Barbican.App/Views/Files/FilesToolbarView.cs`, `src/Barbican.App/Views/Files/FilesSidebarView.cs` | none |
| open | G-073, ML-10 | Category/list tabs above the message list | mail list | L | `src/Barbican.App/Views/List/MessageListView.xaml.cs`, `src/Barbican.App/Views/Sidebar/SidebarView.xaml.cs` | `tabs.*` |
| open | G-062, G-073 | Message list column layout | mail list | M | `src/Barbican.App/Views/List/MessageListView.xaml.cs` | list view model |
| open | G-073 | Structured-mail cards (booking, receipt, flight) | mail list | L | `src/Barbican.App/Views/List/MessageListView.xaml.cs`, `src/Barbican.App/Views/List/ListFmt.cs` | schema normaliser |
| open | ML-11 | Swipe actions and long-press on rows | mail list | M | `src/Barbican.App/Views/List/MessageListView.xaml.cs` | touch input |
| open | G-073 | Hover tag picker on list rows | mail list | S | `src/Barbican.App/Views/List/MessageListView.xaml.cs`, `src/Barbican.Presentation/Mail/RowTag.cs` | tag model |
| open | ML-04 | Row density setting actually resizes rows | mail list | S | `src/Barbican.App/Theme/StyleState.cs`, `src/Barbican.App/Views/List/MessageListView.xaml.cs` | density tokens |
| open | ML-01 | Scheduled-send chip in list rows | mail list | S | `src/Barbican.App/Views/List/MessageListView.xaml.cs`, `src/Barbican.Presentation/Mail/MessageRowViewModel.cs` | submission id on rows |
| open | G-048 | Copy a message to another account | mail list | M | `src/Barbican.Presentation/Mail/MessageListViewModel.Actions.cs` | JMAP `Email/copy` |
| open | G-042 | Folder export and import | mail list | M | `src/Barbican.Presentation/Unified/TransferCommands.cs` | mail-transfer service |
| partial | G-045, SH-08 | Notification centre, rail bell, share/calendar notifications | notifications | M | `src/Barbican.App/Views/Rail/RailView.xaml.cs`, `src/Barbican.Core/Sync/ICalendarPush.cs` | push events |
| partial | G-059 | OS notifications, badge runtime, sounds and push relay | notifications | L | `src/Barbican.App/Shell/ShellLifecycle.cs`, `src/Barbican.App/Views/Settings/SetUiRowsView.cs` | Windows App SDK |
| done | G-041, SB-07 | Per-folder notification mute | notifications | S | `src/Barbican.App/Views/SettingsServer/SrvUiOrganizePage.cs` | notification routing |
| open | G-049 | Link-mismatch warning banner | reader | M | `src/Barbican.App/MailBody/BodyShell.cs`, `src/Barbican.Presentation/Reader/` | link scanner |
| open | G-050 | Phishing report flow | reader | M | `src/Barbican.App/Views/Reader/ReaderView.xaml.cs`, `src/Barbican.Presentation/Reader/` | report endpoint |
| open | G-051 | External-sender chip | reader | S | `src/Barbican.App/Views/Reader/ReaderView.xaml.cs`, `src/Barbican.Presentation/Reader/` | sender trust data |
| open | G-052 | BIMI verified badge (logo fetch exists) | reader | M | `src/Barbican.Core/Mail/Avatars/AvatarRules.cs`, `src/Barbican.App/Views/Reader/ReaderView.xaml.cs` | VMC validation |
| open | G-053 | Trusted-sender dialog and remote-content policy (backend exists) | reader | M | `src/Barbican.Core/Mail/Trust/TrustedSenders.cs`, `src/Barbican.App/Views/Reader/ReaderMenuOpener.cs` | UI + policy setting |
| open | RD-05 | Retention notice banner | reader | S | `src/Barbican.Presentation/Reader/ReceiptBannerSource.cs` | server retention data |
| open | RD-03 | Read-aloud bar and reader-view dialog | reader | M | `src/Barbican.App/Views/Reader/ReaderView.xaml.cs`, `src/Barbican.App/Views/Reader/ReaderMenuOpener.cs` | speech service |
| open | RD-11 | Reading mask overlay | reader | M | `src/Barbican.App/MailBody/BodyShell.cs`, `src/Barbican.Core/Settings/Accessibility/A11y.cs` | body JS |
| open | RD-06 | Rename an attachment | reader | S | `src/Barbican.App/Views/Reader/ReaderView.xaml.cs` | attachment rename command |
| open | G-056 | Fetch the full body when truncated | reader | M | `src/Barbican.Presentation/Mail/Loading/MessageLoader.cs`, `src/Barbican.Core/Mail/Model/EmailBody.cs` | body refetch |
| open | SR-02 | Search filter "has words" and Size fields | search | S | `src/Barbican.App/Views/Search/SearchBoxView.xaml.cs`, `src/Barbican.Presentation/Search/SearchCommands.cs` | search operators |
| open | SR-03 | Global search facets, scope chips, preview | search | L | `src/Barbican.App/Views/Palette/PaletteView.xaml.cs`, `src/Barbican.Presentation/Search/GlobalSearchPaletteProvider.cs` | search backend |
| open | G-070 | Built-in themes, accent colours, custom themes | settings | L | `src/Barbican.App/Theme/StyleState.cs`, `src/Barbican.App/Views/Settings/SetUiScreenFactory.cs`, `design/legacy-slint/theme.slint` | theme compiler |
| open | G-071 | Language selection and localisation | settings | L | `src/Barbican.Core/Settings/Pages/SettingsCatalog.cs`, `src/Barbican.App/Views/Settings/SetUiScreen.cs` | i18n service |
| open | G-027, G-028, G-029 | Live password, TOTP, app-password and API-key writes | settings | L | `src/Barbican.Core/Settings/Account/AccountSecurity.cs`, `src/Barbican.App/Views/Settings/SetUiSecurityPage.cs` | server `x:*` support |
| open | G-030 | Encryption-at-rest key setup | settings | M | `src/Barbican.Core/Settings/Account/AccountSecurity.cs`, `src/Barbican.App/Views/Settings/SetUiSecurityPage.cs` | crypto/keys |
| open | G-031 | App lock (PIN/Windows Hello) and session timeout | settings | M | `src/Barbican.App/Shell/ShellLifecycle.cs`, `src/Barbican.App/Views/SignIn/SignInView.xaml.cs` | platform auth |
| open | G-026 | Masked addresses and sub-address helper | settings | M | `src/Barbican.Core/Settings/Account/AccountSecurity.cs`, `src/Barbican.App/Views/Settings/SetUiAccountPage.cs` | `x:MaskedEmail` |
| open | G-024, G-033 | Rich signature and template body editing | settings | M | `src/Barbican.App/Views/SettingsServer/SrvUiIdentitiesPage.cs`, `src/Barbican.App/Views/SettingsServer/SrvUiTemplatesPage.cs`, `src/Barbican.App/Views/Editor/EditorView.cs` | editor |
| open | G-021 | Vacation policy template picker | settings | S | `src/Barbican.App/Views/SettingsServer/SrvUiVacationPage.cs`, `src/Barbican.Core/Settings/Vacation/VacationService.cs` | org policy |
| open | S-settingsSyncDisabled | Settings sync across devices | settings | M | `src/Barbican.Core/Settings/Prefs/SettingsStore.cs`, `src/Barbican.Core/Settings/Pages/SettingsCatalog.cs` | mailbox metadata |
| open | G-067, SH-10 | User-defined sidebar apps (URL, icon, open mode) | settings | M | `src/Barbican.App/Views/Rail/RailView.xaml.cs`, `src/Barbican.App/Views/Settings/AddonsPageFactory.cs` | sidebar-app host |
| open | G-068 | Protocol open mode for mail/calendar links | settings | M | `src/Barbican.App/Views/Compose/MailtoStartup.cs` | platform handlers |
| open | G-084 | Delegation and impersonation | settings | M | `src/Barbican.Core/Accounts/AccountSession.cs`, `src/Barbican.App/Views/SignIn/SignInView.xaml.cs` | server support |
| open | G-082 | Offline mode and outbox | settings | L | `src/Barbican.Core/Sync/MailSync.cs`, `src/Barbican.Presentation/Compose/` | new offline store |
| open | G-085 | Tour and onboarding | settings | M | `src/Barbican.App/Shell/ShellPage.xaml.cs`, `src/Barbican.App/Views/SignIn/SignInView.xaml.cs` | none |
| open | S-proInterface | Pro interface | settings | M | `src/Barbican.App/Theme/StyleState.cs`, `src/Barbican.App/Views/Settings/SetUiScreenFactory.cs` | themes |
| open | G-057 | Storage usage from the real quota (fake only today) | settings | S | `src/Barbican.App/Views/Settings/SetUiAccountPage.cs` | quota read |
| open | AX-01 | Focus-always ring on pointer focus | settings | S | `src/Barbican.App/Theme/FocusRing.cs`, `src/Barbican.App/Styles/Controls.xaml` | none |
| open | AX-02 | 44 px large targets on every control | settings | M | `src/Barbican.App/Theme/StyleState.cs`, `src/Barbican.App/Styles/Controls.xaml` | none |
| open | AX-04 | Live-region announcements beyond toasts | settings | S | `src/Barbican.App/Views/Actions/ToastHostView.xaml.cs`, `src/Barbican.App/Views/List/MessageListView.xaml.cs` | none |
| open | S-debugMode | Detailed logging (debugMode) control | settings | S | `src/Barbican.Core/Storage/CrashLog.cs`, `src/Barbican.App/Views/Settings/SetUiRowsView.cs` | logging |
| open | MX-04 | Customisable keyboard shortcuts | settings | M | `src/Barbican.App/Views/Shortcuts/ShortcutSheetView.xaml.cs`, `src/Barbican.Presentation/Commands/ShellCommands.cs` | none |
| done (pending merge) | SH-12 | Mail layouts: split, focus, horizontal, Mountain View | shell | L | `src/Barbican.App/Shell/ShellPage.xaml.cs`, `src/Barbican.Presentation/Shell/MailLayoutRules.cs`, `src/Barbican.App/Theme/StyleState.cs` | settings |
| open | G-055, RD-12 | Message and composer pop-out windows | shell | L | `src/Barbican.App/Shell/ShellPage.ComposerWindow.cs`, `src/Barbican.App/Shell/ShellPage.xaml.cs` | multi-window |
| open | SH-05 | Collapsible rail with labels | shell | M | `src/Barbican.App/Views/Rail/RailView.xaml.cs`, `src/Barbican.App/Theme/StyleState.cs` | none |
| open | SB-02 | Fold sidebar to a 48 px icon column | shell | M | `src/Barbican.App/Views/Sidebar/SidebarView.xaml.cs`, `src/Barbican.App/Theme/ShellLayout.cs` | none |
| open | SH-07 | Rail account stack: add account, sign-out menu | shell | S | `src/Barbican.App/Views/Rail/RailView.xaml.cs`, `src/Barbican.App/Views/Accounts/AccountPopoverView.xaml.cs` | none |
| open | SH-09 | Admin entry in the rail | shell | S | `src/Barbican.App/Views/Rail/RailView.xaml.cs` | admin (M7) |
| open | SH-11 | Phone layout for Calendar, Contacts, Files, Settings | shell | M | `src/Barbican.App/Shell/ShellPage.Phone.cs`, `src/Barbican.App/Views/Calendar/CalendarScreen.cs` | none |
| open | G-061 | Unwired appearance settings (toolbar position, account switcher, keep apps loaded, compact shared mailboxes) | shell | M | `src/Barbican.App/Theme/StyleState.cs`, `src/Barbican.App/Views/Rail/RailView.xaml.cs` | none |
| open | SB-09 | Scheduled mailbox, all-inboxes unread, shared accounts section | shell | M | `src/Barbican.App/Views/Unified/SharedSectionView.xaml.cs`, `src/Barbican.App/Views/Sidebar/SidebarView.xaml.cs` | shared mail |
| open | G-044 | Live push/delta for contacts, files and identities (mail only today) | shell | M | `src/Barbican.Core/Sync/MailSync.Push.cs`, `src/Barbican.Core/Sync/CalendarPushRelay.cs` | `/changes` per type |

(a) Need a new Core service: live push/delta for contacts-files-identities, notification centre/delivery, offline outbox, settings sync, sharing/ACL, i18n, contact activity feed, WebDAV/WOPI sync, delegation/impersonation session, admin client, mail-body refetch, and a structured-mail normaliser.

(b) Security-sensitive: add-on send (`email.forward`), account credentials/TOTP/app passwords/API keys, encryption-at-rest and `crypto.*`/`keystore.*`, `http.post`/`proxy.*` arbitrary network, admin writes and org policy, collection sharing ACLs, trusted-sender/remote-content policy, protocol open mode, phishing reporting, and the offline outbox (queued sends).

## How to close a row

- Implement the behaviour in code, with tests through FakeJmap or a fixture that exercises the real server contract.
- Run the hidden automation path for anything the user can see or click.
- In the same commit, set this row's status and update the owning ledger or `spec/gaps.csv` row so the two never disagree.

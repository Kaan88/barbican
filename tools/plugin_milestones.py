"""Default milestone and M1 owner for a hook or slot (spec/plugins/README.md; PORT-PLAN §5, M1-plan §2.4).

A hook belongs to the milestone that builds the surface that fires it. HOOK_OVERRIDE names the
hooks of an M1 group whose surface arrives later.
"""

# group -> milestone when nothing more specific is known
GROUP_MILESTONE = {
    "mail-list": "M1", "mail-read": "M1", "mail-mailboxes": "M1", "mail-search": "M1", "mail-sync": "M1",
    "navigation": "M1", "theme": "M1", "keyboard": "M1", "app": "M1", "ui": "M1", "auth": "M1",
    "mail-compose": "M3", "mail-send": "M3", "mail-attachments": "M2", "mail-organize": "M2",
    "calendar": "M4", "tasks": "M4", "contacts": "M5", "files": "M5",
    "settings": "M6", "identities": "M6", "filters": "M6", "vacation": "M6", "templates": "M6",
    "security": "M2", "notifications": "M8", "tabs": "M2",
}

# hooks that sit in an M1 group but fire from a later milestone's surface: name -> milestone
HOOK_OVERRIDE = {
    "onBeforeReply": "M3", "onBeforeReplyAll": "M3", "onBeforeForward": "M3",
    "onBeforeExternalLink": "M2", "onAvatarResolve": "M2",
    "onBeforeMailboxCreate": "M2", "onMailboxCreate": "M2", "onBeforeMailboxRename": "M2",
    "onMailboxRename": "M2", "onBeforeMailboxDelete": "M2", "onMailboxDelete": "M2",
    "onBeforeMailboxEmpty": "M2", "onMailboxEmpty": "M2",
    "onProvideSearchResults": "M2",
    "onAccountRemove": "M2", "onBeforeLogout": "M2", "onAfterLogout": "M2", "onOAuthCallback": "M6b",
    "onCustomThemeChange": "M10", "onLocaleChange": "M10",
    "onColumnResize": "M2", "onTextSelectionChange": "M2",
    "onDragStart": "M2", "onDragEnd": "M2", "onEmailDrop": "M2", "onTagDrop": "M2",
    "onViewChange": "n/a", "onMobileBack": "n/a", "onDeviceTypeChange": "n/a",
}

# status for a milestone of "n/a" (the desktop app has no such surface)
NA_STATUS = {
    "onViewChange": "n/a(Bulwark phone layout: the desktop app has one layout)",
    "onMobileBack": "n/a(Bulwark phone layout: the desktop app has one layout)",
    "onDeviceTypeChange": "n/a(Bulwark phone/tablet/desktop switch: the desktop app has one layout)",
}

GROUP_OWNER = {
    "mail-sync": "jmap", "auth": "accounts", "theme": "shell-ui", "mail-read": "body",
    "mail-list": "mailcore", "mail-mailboxes": "mailcore", "mail-search": "mailcore",
    "navigation": "mailcore", "keyboard": "mailcore", "app": "mailcore", "ui": "mailcore",
}


def hook_milestone(name, group):
    return HOOK_OVERRIDE.get(name) or GROUP_MILESTONE.get(group, "M6b")


def hook_owner(name, group):
    return GROUP_OWNER.get(group, "") if hook_milestone(name, group) == "M1" else ""


def slot_milestone(name, area):
    return "M6b"

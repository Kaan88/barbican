// Mirrors: spec/menus.md MENU-CONTACT, MENU-GROUP, MENU-CATEGORY, MENU-BOOK (Rust ctxmenu.rs:314-330, :454); M5 plan section 4.5. Additive contract of the plan commit (frozen): MenuContext.Contact. Owner con-ui provides the items; the menu ids are con-ui's own ContactsMenuIds.

namespace Barbican.Presentation.Contacts;

/// <summary>What a contacts menu was opened on: the contacts (ids in one account; several for the multi-select menu), or a group, category or address book of the sidebar.</summary>
public sealed record ContactMenuTarget(string AccountKey, IReadOnlyList<string> ContactIds, string? GroupId = null, string? Category = null, string? BookId = null);

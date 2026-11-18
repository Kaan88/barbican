// Mirrors: Bulwark lib/plugin-hook-catalog.ts:190 onContactOpen (ContactView), :198 onContactSelectionChange ({ contactIds }). The payloads of the two hooks con-ui fires (spec/plugins/hooks.csv).

namespace Barbican.Presentation.Contacts;

/// <summary>onContactOpen: the contact the user opened.</summary>
public sealed record ContactsOpenPayload(string Id, string Name, IReadOnlyList<string> Emails, string Organization);

// Mirrors: Bulwark lib/plugin-hook-catalog.ts:198 onContactSelectionChange ({ contactIds }). Owner con-ui.

namespace Barbican.Presentation.Contacts;

/// <summary>onContactSelectionChange: the ids now selected.</summary>
public sealed record ContactsSelectionPayload(IReadOnlyList<string> ContactIds);

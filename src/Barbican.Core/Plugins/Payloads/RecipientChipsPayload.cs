// Mirrors: Bulwark lib/plugin-hook-catalog.ts:98 (onRecipientChipsChange: Recipient[]). Owner composer-ui.

using Barbican.Core.Compose.Recipients;

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The recipients of one field ("to", "cc", "bcc") after the user changed it; handlers return the list to keep.</summary>
public sealed record RecipientChipsPayload(string Field, IReadOnlyList<Recipient> Recipients);

// Mirrors: RFC 8621 section 6.1 (Identity: textSignature, htmlSignature); Bulwark lib/jmap/client.ts getIdentities, stores/identity-store.ts. Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose;

/// <summary>An identity with its signatures (empty strings when it has none).</summary>
public sealed record ComposeIdentity(Identity Identity, string TextSignature, string HtmlSignature);

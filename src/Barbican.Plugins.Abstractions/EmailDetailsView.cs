// Mirrors: the Rust email.details hook input; Bulwark hook email-details payload. Stub from stage F of M2; owner unified.

namespace Barbican.Plugins;

/// <summary>The part of a message the email-details hook shows to a plugin.</summary>
public sealed record EmailDetailsView(IReadOnlyList<HeaderView> Headers);

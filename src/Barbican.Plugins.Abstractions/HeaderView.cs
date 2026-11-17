// Mirrors: the Rust email.details hook input (a JMAP header list: name, value); Bulwark onEmailDetails payload. Stub from stage F of M2; owner unified.

namespace Barbican.Plugins;

/// <summary>One header of the message a plugin looks at.</summary>
public sealed record HeaderView(string Name, string Value);

// Mirrors: ADR 0008 decision 2 (the import is offered to the user only in the default profile) and M1-plan §1 rule 3, §6.3 (an agent-only
// path into dev / auto-<port> profiles, password kind only, for live read-only checks).

namespace Barbican.Core.Accounts;

/// <summary>Who runs the import from the Rust app.</summary>
public enum ImportMode
{
    /// <summary>The user, in the default profile: both kinds, and the admin and assistant keys.</summary>
    User,

    /// <summary>An agent, in a dev or automation profile: password entries only, all marked read-only; OAuth entries are skipped.</summary>
    Agent,
}

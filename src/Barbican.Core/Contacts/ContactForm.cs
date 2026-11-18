// Mirrors: Rust contacts_x.rs:185 Form (the single-value fields of the contact form). Contract from the M5 plan section 4.1; owner con-data.

namespace Barbican.Core.Contacts;

/// <summary>The single-value fields of the contact form; the typed entries are <see cref="ContactField"/> rows.</summary>
public sealed record ContactForm
{
    public string Prefix { get; init; } = string.Empty;

    public string Given { get; init; } = string.Empty;

    public string Middle { get; init; } = string.Empty;

    public string Surname { get; init; } = string.Empty;

    public string Suffix { get; init; } = string.Empty;

    public string Nickname { get; init; } = string.Empty;

    public string Org { get; init; } = string.Empty;

    public string Dept { get; init; } = string.Empty;

    public string JobTitle { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;
}

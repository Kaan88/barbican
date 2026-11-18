// Mirrors: Rust contacts_x.rs:18 Field (one typed entry of the form: section, the key in the card's map, kind and kind2 as option indexes, five text slots a..e, the entry's own label). Contract from the M5 plan section 4.1; owner con-data.

namespace Barbican.Core.Contacts;

/// <summary>One entry row of the contact form (an email, phone, address, date, related person, link, ...). Sections: email, phone, address, date, related, url, im and the others of <c>ContactFields.Of</c>.</summary>
public sealed record ContactField
{
    public string Section { get; init; } = string.Empty;

    /// <summary>The entry's key in the card's map (null: a new entry). For related people the key is the name.</summary>
    public string? Key { get; init; }

    public int Kind { get; init; }

    public int Kind2 { get; init; }

    public string A { get; init; } = string.Empty;

    public string B { get; init; } = string.Empty;

    public string C { get; init; } = string.Empty;

    public string D { get; init; } = string.Empty;

    public string E { get; init; } = string.Empty;

    /// <summary>The entry's own label (Apple X-ABLabel): shown as the last type option and kept while picked.</summary>
    public string? Label { get; init; }
}

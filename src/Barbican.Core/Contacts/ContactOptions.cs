// Mirrors: Rust contacts_x.rs:14 CAP, :59-:63 CONTEXTS, PHONE_FEATURES, PHONE_CTX, DATE_KINDS, RELATIONS; Bulwark components/contacts/contact-form.tsx (the option lists); RFC 9610 (urn:ietf:params:jmap:contacts); RFC 9553 (JSContact). B-CON-007..010. Real data, not a stub. Owner con-data (M5 plan section 4.1).

namespace Barbican.Core.Contacts;

/// <summary>The fixed option lists of the contact form and the capability URNs.</summary>
public static class ContactOptions
{
    public const string Capability = "urn:ietf:params:jmap:contacts";

    public const string PrincipalsCapability = "urn:ietf:params:jmap:principals";

    public static IReadOnlyList<string> Contexts { get; } = ["Personal", "Work", "Other"];

    /// <summary>JSContact phone feature key and its label.</summary>
    public static IReadOnlyList<(string Key, string Label)> PhoneFeatures { get; } =
        [("voice", "Phone"), ("mobile", "Mobile"), ("fax", "Fax"), ("pager", "Pager"), ("text", "Text"), ("video", "Video"), ("main-number", "Main")];

    public static IReadOnlyList<string> PhoneContexts { get; } = ["Any", "Personal", "Work"];

    public static IReadOnlyList<(string Key, string Label)> DateKinds { get; } = [("birth", "Birthday"), ("wedding", "Anniversary"), ("death", "Date of death"), ("other", "Other date")];

    public static IReadOnlyList<(string Key, string Label)> Relations { get; } =
    [
        ("contact", "Contact"), ("spouse", "Spouse"), ("sweetheart", "Partner"), ("child", "Child"), ("parent", "Parent"),
        ("sibling", "Sibling"), ("friend", "Friend"), ("colleague", "Colleague"), ("co-worker", "Co-worker"), ("kin", "Relative"),
        ("emergency", "Emergency contact"), ("neighbor", "Neighbour"), ("acquaintance", "Acquaintance"),
    ];
}

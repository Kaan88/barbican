// Mirrors: Rust recipients.rs / chips.rs (tag rust-final); Bulwark lib/email-composer-utils.ts, lib/rfc5322-mailbox.ts, lib/idn.ts, lib/validation.ts, stores/contact-store.ts; RFC 5322 section 3.4 (mailbox, group), RFC 3490/5891 (IDNA). Contract stub from the M3 plan section 4.5; owner composer-ui replaces the bodies.

using Barbican.Core.Common;

namespace Barbican.Core.Compose.Recipients;

/// <summary>The recipient fields' pure text rules (port of Rust <c>recipients.rs</c>). Stub: every member throws <see cref="PortPendingException"/> until stream composer-ui lands.</summary>
public static class RecipientText
{
    /// <summary>A domain in punycode (bücher.example becomes xn--bcher-kva.example); null when it is not a valid domain (a space, an empty label).</summary>
    public static string? ToAsciiDomain(string domain) => throw new PortPendingException("RecipientText.ToAsciiDomain");

    /// <summary>The address with its domain in ASCII; the local part keeps its case, the domain is left as typed when it is already ASCII.</summary>
    public static string ToAsciiEmail(string address) => throw new PortPendingException("RecipientText.ToAsciiEmail");

    /// <summary>A punycode domain shown in Unicode only when it decodes back to the same ASCII form (xn--zz-zz.de stays).</summary>
    public static string ToUnicodeDomain(string domain) => throw new PortPendingException("RecipientText.ToUnicodeDomain");

    /// <summary>The address with its domain in Unicode (case-insensitive xn-- prefix).</summary>
    public static string ToUnicodeEmail(string address) => throw new PortPendingException("RecipientText.ToUnicodeEmail");

    /// <summary>Plus-tags and internationalised domains are valid; empty, no @, empty or dashed labels, a trailing dot, whitespace, control characters, a second @ or &lt; and a local part over 64 characters are not.</summary>
    public static bool IsValidEmail(string email) => throw new PortPendingException("RecipientText.IsValidEmail");

    /// <summary>A display name without its surrounding quotes and quoted-pairs.</summary>
    public static string UnquoteName(string name) => throw new PortPendingException("RecipientText.UnquoteName");

    /// <summary>A display name cleaned for the chip (quotes removed, control characters dropped).</summary>
    public static string SanitizeDisplayName(string name) => throw new PortPendingException("RecipientText.SanitizeDisplayName");

    /// <summary>Name and address of a mailbox string, with or without a space before the angle bracket, an unclosed bracket tolerated, a quoted name that contains an address reduced to the name.</summary>
    public static (string Name, string Email) SplitMailbox(string value) => throw new PortPendingException("RecipientText.SplitMailbox");

    /// <summary>Splits on the separators honouring quoted names, angle brackets and group syntax (Team: a@x.example, b@y.example;); a colon no semicolon closes is not a group.</summary>
    public static IReadOnlyList<string> SplitRecipients(string value, string separators) => throw new PortPendingException("RecipientText.SplitRecipients");

    /// <summary>One entry as a Recipient: a mailbox, or a group with its members (a quoted group name may contain commas).</summary>
    public static Recipient ParseRecipient(string text) => throw new PortPendingException("RecipientText.ParseRecipient");

    /// <summary>A field text as recipients (split, then parse).</summary>
    public static IReadOnlyList<Recipient> ParseList(string value) => throw new PortPendingException("RecipientText.ParseList");

    /// <summary>Bulwark formatRecipient: the bare address when there is no distinct name, else Name &lt;email&gt;, quoting (and escaping) the name only when it holds one of , &lt; &gt; " @ ; :.</summary>
    public static string FormatRecipient(string name, string email) => throw new PortPendingException("RecipientText.FormatRecipient");

    /// <summary>One entry as field text, groups as RFC 5322 group syntax.</summary>
    public static string FormatEntry(Recipient recipient) => throw new PortPendingException("RecipientText.FormatEntry");

    /// <summary>The field text of a list; parsing it again gives the same list.</summary>
    public static string FormatList(IReadOnlyList<Recipient> list) => throw new PortPendingException("RecipientText.FormatList");

    /// <summary>Groups replaced by their members, de-duplicated by address (case-insensitive).</summary>
    public static IReadOnlyList<Recipient> Expand(IReadOnlyList<Recipient> list) => throw new PortPendingException("RecipientText.Expand");

    /// <summary>The field text as it goes into the message: groups expanded, domains in ASCII.</summary>
    public static string ExpandForSend(string value) => throw new PortPendingException("RecipientText.ExpandForSend");

    /// <summary>The addresses that do not validate (reported, never dropped).</summary>
    public static IReadOnlyList<string> InvalidAddresses(IReadOnlyList<Recipient> list) => throw new PortPendingException("RecipientText.InvalidAddresses");

    /// <summary>A pasted list split on whitespace, commas, semicolons and newlines into valid new recipients (not in <c>existing</c>, de-duplicated case-insensitively) and the invalid leftovers; a quoted name with a comma stays one name; an unclosed angle bracket is repaired.</summary>
    public static (IReadOnlyList<Recipient> Recipients, IReadOnlyList<string> Invalid) SplitPasted(string text, IReadOnlyList<string> existing) => throw new PortPendingException("RecipientText.SplitPasted");

    /// <summary>The suggestion Enter or Tab takes before the arrows moved: -1 for blank input or no suggestions; for a complete valid typed address its own suggestion (case-insensitive) or -1 (add as typed); for anything else the top one (0).</summary>
    public static int DefaultSuggestionIndex(string input, IReadOnlyList<Suggestion> suggestions) => throw new PortPendingException("RecipientText.DefaultSuggestionIndex");

    /// <summary>0 when a word of any text starts with the query (case-insensitive, accents kept), 1 when a text only contains it, else -1.</summary>
    public static int MatchRank(string query, IReadOnlyList<string> texts) => throw new PortPendingException("RecipientText.MatchRank");

    /// <summary>Bulwark getAutocomplete: groups and contacts, then recent recipients not already suggested; stable-sorted by rank; cleaned names; 10 at most; a blank query gives none.</summary>
    public static IReadOnlyList<Suggestion> Suggest(string query, SuggestionSources sources) => throw new PortPendingException("RecipientText.Suggest");

    /// <summary>Two letters for the avatar: first letters of the first two name words, else of the first two parts of the address's local part.</summary>
    public static string Initials(string name, string email) => throw new PortPendingException("RecipientText.Initials");
}

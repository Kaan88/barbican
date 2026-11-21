// Mirrors: Bulwark components/trusted-senders-modal.tsx:63-100 (the list, the search shown with 5 or more senders, the count wording),
// :110-125 (the address is trimmed and lower-cased, validated, and a duplicate is refused), :96-99 (the address test); settings-store.ts:2171-2201
// (add, remove, isTrusted; kept lower-cased). locales/en/common.json settings.email_behavior.trusted_senders.*. Owner reader, I-209.

using System.Globalization;
using System.Text.RegularExpressions;

namespace Barbican.Presentation.Settings;

/// <summary>The pure rules of the trusted senders list.</summary>
public static partial class TrustedSendersRules
{
    /// <summary>From this many senders the list offers a search box (Bulwark trusted-senders-modal.tsx:76-77).</summary>
    public const int SearchFrom = 5;

    /// <summary>"Please enter a valid email address" (Bulwark trusted_senders.invalid_email).</summary>
    public const string InvalidEmail = "Please enter a valid email address";

    /// <summary>"This sender is already trusted" (Bulwark trusted_senders.already_added).</summary>
    public const string AlreadyAdded = "This sender is already trusted";

    /// <summary>Bulwark's trusted_senders.remove_error.</summary>
    public const string RemoveFailed = "Couldn't remove the sender";

    /// <summary>The address as the list keeps it: trimmed and lower-cased (Bulwark trusted-senders-modal.tsx:110).</summary>
    public static string Normalize(string? address) => (address ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Bulwark's address test: something, an @, a domain with a dot and a suffix (trusted-senders-modal.tsx:96-99).</summary>
    public static bool IsValid(string? address) => Address().IsMatch(Normalize(address));

    /// <summary>Whether the list already has the address, compared as it is kept (<c>TrustedSenders</c> lower-cases too).</summary>
    public static bool Contains(IReadOnlyList<string> all, string? address)
    {
        ArgumentNullException.ThrowIfNull(all);
        var key = Normalize(address);
        return key.Length > 0 && all.Contains(key, StringComparer.Ordinal);
    }

    /// <summary>The list for a search: everything for an empty query, else the addresses that contain it (Bulwark trusted-senders-modal.tsx:70-74).</summary>
    public static IReadOnlyList<string> Filter(IReadOnlyList<string> all, string? query)
    {
        ArgumentNullException.ThrowIfNull(all);
        var text = Normalize(query);
        return text.Length == 0 ? [.. all] : [.. all.Where(a => a.Contains(text, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>How the settings row and the dialog count the list: "None", "1 sender", "N senders".</summary>
    public static string CountText(int count) => count switch
    {
        <= 0 => "None",
        1 => "1 sender",
        _ => string.Create(CultureInfo.InvariantCulture, $"{count} senders"),
    };

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Address();
}

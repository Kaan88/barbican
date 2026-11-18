// Mirrors: Rust platform open_url (http and https only); Bulwark components/contacts/contact-detail.tsx (phone numbers as tel: links, I-205; M5 con-ui, D-M5-I06). Owner con-ui.

namespace Barbican.Platform;

/// <summary>Which addresses the system launchers hand to the operating system: web addresses, and a phone number in the plain form <c>tel:+441234567</c> (digits with an optional leading plus, nothing else).</summary>
public static class BrowserLaunchRules
{
    /// <summary>The most digits a phone number may have (E.164: 15; a little room for extensions that people type into the number).</summary>
    public const int MaxDigits = 20;

    /// <summary>True for an absolute http(s) address, or a <c>tel:</c> address of 3 to <see cref="MaxDigits"/> digits with an optional leading plus.</summary>
    public static bool Allows(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(uri.Scheme, "tel", StringComparison.Ordinal))
        {
            return false;
        }

        var number = uri.AbsoluteUri["tel:".Length..];
        var digits = number.StartsWith('+') ? number[1..] : number;
        return digits.Length is >= 3 and <= MaxDigits && digits.All(char.IsAsciiDigit);
    }
}

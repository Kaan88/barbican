// Mirrors: Bulwark components/email/link-dialog.tsx:44-47 (the address cannot contain spaces; Save needs an address) and image-url-dialog.tsx:16-24 (pictureAddress: https, a host, no spaces).

namespace Barbican.Presentation.Editor;

/// <summary>Validation of the link dialog and the picture-from-address dialog (I-266, I-267).</summary>
public static class LinkValidator
{
    /// <summary>Bulwark's <c>email_composer.toolbar.link_invalid</c>.</summary>
    public const string InvalidLink = "The address can't contain spaces";

    /// <summary>Bulwark's <c>ux_p3_settings.picture.invalid</c> wording.</summary>
    public const string InvalidPicture = "Enter an https:// address of a picture";

    /// <summary>The error under the address field, or null.</summary>
    public static string? LinkError(string href) => (href ?? string.Empty).Trim().Any(char.IsWhiteSpace) ? InvalidLink : null;

    /// <summary>Save is enabled with an address and no error.</summary>
    public static bool CanSubmitLink(string href)
    {
        var address = (href ?? string.Empty).Trim();
        return address.Length > 0 && LinkError(address) is null;
    }

    /// <summary>The picture URL when the text is an https address with a host and no spaces, else null (nothing that could run: no http, data or javascript).</summary>
    public static string? PictureAddress(string raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0 || text.Any(char.IsWhiteSpace))
        {
            return null;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && uri.Host.Length > 0 ? uri.AbsoluteUri : null;
    }
}

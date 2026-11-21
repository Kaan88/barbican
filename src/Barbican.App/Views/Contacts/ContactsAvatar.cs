// Mirrors: Bulwark components/ui/avatar.tsx:339-345 (sm 32, lg 48), :274-279 initialsColor, :416-420 (circle, semibold initials), lib/account-utils.ts:25-45 generateAvatarColor (12 hues from the address, Bulwark style); Bulwark components/contacts/contact-avatar.tsx; B-CON-027 (avatars without a photo show initials); B-CON-030 (squircle in Bento, circle in Nordic: the corner radius is the caller's); Rust cal_editor.rs:69 initials.
// A photo is a data: URI the card holds (blob photos are fetched by con-data later); an image that cannot be decoded shows the initials instead. Brushes are cached, the cache never holds more than a few hundred.

using Barbican.App.Theme;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Barbican.App.Views.Contacts;

/// <summary>The avatar of a contact: a photo brush, or a tint chosen from the name.</summary>
internal static class ContactsAvatar
{
    private const int CacheLimit = 400;
    private static readonly string[] Families = ["Sky", "Mint", "Amber", "Rose"];
    private static readonly Dictionary<string, ImageBrush?> Photos = new(StringComparer.Ordinal);

    /// <summary>The tint background and foreground brushes of the initials, the same name always the same tint.</summary>
    public static (Brush Background, Brush Foreground) Tint(string seed)
    {
        var hash = 17;
        foreach (var ch in seed ?? string.Empty)
        {
            hash = unchecked((hash * 31) + ch);
        }

        var family = Families[Math.Abs(hash % Families.Length)];
        return (AppResources.Brush(family + "Bg"), AppResources.Brush(family + "Fg"));
    }

    // lib/account-utils.ts:31-43: the 12 hues of an avatar, picked from the address (the name when there is none)
    private static readonly uint[] BulwarkHues = [0x2563eb, 0x7c3aed, 0xdb2777, 0xdc2626, 0xea580c, 0xd97706, 0x65a30d, 0x16a34a, 0x0d9488, 0x0891b2, 0x6366f1, 0x9333ea];

    /// <summary>The Bulwark avatar colours: a hue from the address (or name) and white or black initials, whichever contrasts more (avatar.tsx:274-279).</summary>
    public static (Brush Background, Brush Foreground) BulwarkTint(string name, string? email)
    {
        var key = string.IsNullOrWhiteSpace(email) ? (name ?? string.Empty).Trim() : email.Trim().ToLowerInvariant();
        var hash = 0;
        foreach (var ch in key)
        {
            hash = unchecked((hash << 5) - hash + ch);
        }

        var rgb = BulwarkHues[(int)(Math.Abs((long)hash) % BulwarkHues.Length)];
        var (r, g, b) = ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        var l = (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));
        var white = 1.05 / (l + 0.05) >= (l + 0.05) / 0.05;
        return (new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b)), new SolidColorBrush(white ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black));
    }

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>The brush of a data: URI photo, or null when there is none or it cannot be shown.</summary>
    public static ImageBrush? Photo(string? dataUri)
    {
        if (string.IsNullOrEmpty(dataUri))
        {
            return null;
        }

        if (Photos.TryGetValue(dataUri, out var cached))
        {
            return cached;
        }

        ImageBrush? brush = null;
        try
        {
            var comma = dataUri.IndexOf(',', StringComparison.Ordinal);
            if (comma > 0 && dataUri.AsSpan(0, comma).Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Convert.FromBase64String(dataUri[(comma + 1)..]);
                // the stream adapter is not disposed here: disposing it closes the stream it wraps and the decoder would read nothing (found in the M5 hidden run, D-M5-I02)
                var stream = new InMemoryRandomAccessStream();
#pragma warning disable CA2000 // closing the adapter closes the stream the bitmap is about to read
                var output = stream.AsStreamForWrite();
#pragma warning restore CA2000
                output.Write(bytes, 0, bytes.Length);
                output.Flush();
                stream.Seek(0);
                var image = new BitmapImage();
                image.SetSource(stream);
                brush = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
            }
        }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            brush = null;
        }

        if (Photos.Count >= CacheLimit)
        {
            Photos.Clear();
        }

        Photos[dataUri] = brush;
        return brush;
    }

    /// <summary>The corner radius of an avatar of <paramref name="size"/>: a squircle in Bento, a circle in Nordic.</summary>
    public static CornerRadius Radius(bool bento, double size) => new(bento ? size * 0.33 : size / 2);
}

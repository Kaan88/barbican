// Mirrors: Bulwark components/contacts/contact-avatar.tsx; B-CON-027 (avatars without a photo show initials); B-CON-030 (squircle in Bento, circle in Nordic: the corner radius is the caller's); Rust cal_editor.rs:69 initials.
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

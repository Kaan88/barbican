namespace Barbican.Editor.Layout.Tests;

/// <summary>The body font each OS has: Segoe UI on Windows, DejaVu Sans on the Linux VM (Debian fonts-dejavu-core).</summary>
internal static class TestFonts
{
    public static string Body => OperatingSystem.IsWindows() ? "Segoe UI" : "DejaVu Sans";

    public static string Mono => OperatingSystem.IsWindows() ? "Consolas" : "DejaVu Sans Mono";

    public static LayoutStyle Style { get; } = new() { BodyFont = Body, MonoFont = Mono, BaseSize = 15f };
}

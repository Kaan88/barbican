using Uno.UI.Hosting;

namespace Barbican.App;

/// <summary>Entry point of the Uno Skia desktop head (net10.0-desktop): X11 on Linux, Win32 on Windows.</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new BarbicanApp())
            .UseX11()
            .UseWin32()
            .Build();
        host.Run();
    }
}
